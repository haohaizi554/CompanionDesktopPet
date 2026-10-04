using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace CompanionDesktopPet.Services;

internal interface IVoiceSpeaker : IDisposable
{
    bool Enabled { get; set; }

    void Speak(string text, string? tone, string? trigger = null, bool urgent = false);

    void ApplySpeech(double speed, double temperature, double repetitionPenalty, int topK, double topP);

    bool HasPendingSpeech { get; }

    void Stop();

    void NotifyPlaybackCompleted();

    event Action<string>? SynthesisStarted;

    event Action<double>? InferenceProgress;

    event Action<VoiceClip>? PlaybackReady;

    event Action? SynthesisContinuing;

    event Action? VoiceIdle;
}

internal sealed class JiayiVoiceSpeaker : IVoiceSpeaker
{
    private readonly VoiceRuntime _runtime;
    private readonly JiayiVoicePack _pack;
    private readonly string _scriptPath;
    private readonly string _outputDirectory;
    private readonly string _logPath;
    private readonly object _pendingGate = new();
    private readonly object _stdinGate = new();
    private readonly AutoResetEvent _work = new(false);
    private readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private Process? _process;
    private StreamWriter? _stdin;
    private Thread? _thread;
    private readonly VoiceTurnQueue _queue = new();
    private readonly SpeechClipCache _cache;
    private int _requestId;
    private int _activeId;
    private int _started;
    private int _disposed;
    private int _ready;
    private int _parked;
    private int _releaseWhenIdle;
    private int _speechEpoch;
    private DateTime _lastInferenceUtc = DateTime.UtcNow;
    // Backs off restarting a process that already exited. A loaded model stays resident.
    private static readonly TimeSpan InferenceParkAfter = TimeSpan.FromMinutes(8);
    private string? _lastReferenceId;
    private string? _retriedText;
    private string? _activeCacheKey;
    private double _speechSpeed = 1;
    private double _speechTemperature = 1;
    private double _speechRepetition = 1.4;
    private int _topK = 15;
    private double _topP = 1;

    private JiayiVoiceSpeaker(VoiceRuntime runtime, JiayiVoicePack pack, string scriptPath)
    {
        _runtime = runtime;
        _pack = pack;
        _scriptPath = scriptPath;
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CompanionDesktopPet",
            "voice");
        Directory.CreateDirectory(directory);
        _outputDirectory = directory;
        _logPath = Path.Combine(directory, "voice.log");
        _cache = new SpeechClipCache(Path.Combine(directory, "cache"));
    }

    public bool Enabled { get; set; } = true;

    public event Action<string>? SynthesisStarted;

    public event Action<double>? InferenceProgress;

    public event Action<VoiceClip>? PlaybackReady;

    public event Action? SynthesisContinuing;

    public event Action? VoiceIdle;

    public static JiayiVoiceSpeaker? TryCreate(string startDirectory)
    {
        var runtime = VoiceRuntime.TryLoad(startDirectory);
        if (runtime is null)
        {
            return null;
        }

        var packDirectory = Path.Combine(runtime.VoiceRoot, "voice", "packs", "jiayi");
        if (!File.Exists(Path.Combine(packDirectory, "manifest.json")))
        {
            return null;
        }

        var pack = JiayiVoicePack.Load(packDirectory);
        if (pack.MissingAsset() is not null)
        {
            return null;
        }

        var scriptPath = Path.Combine(runtime.VoiceRoot, "voice", "infer_stdio.py");
        if (!File.Exists(scriptPath))
        {
            return null;
        }

        return new JiayiVoiceSpeaker(runtime, pack, scriptPath);
    }

    internal void Prepare() => StartSession();

    internal void ParkWhenQuiet()
    {
        Interlocked.Exchange(ref _releaseWhenIdle, 1);
        _work.Set();
    }

    public void Speak(string text, string? tone, string? trigger = null, bool urgent = false)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(text) || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var pieces = SpeechFragments.SplitForSpeech(text);
        if (pieces.Count == 0)
        {
            return;
        }

        lock (_pendingGate)
        {
            _queue.Enqueue(new VoiceTurn(pieces[0], tone, trigger, urgent));
            foreach (var piece in pieces.Skip(1))
            {
                _queue.Enqueue(new VoiceTurn(piece, tone, trigger));
            }
        }

        StartSession();
        _work.Set();
    }

    public void ApplySpeech(double speed, double temperature, double repetitionPenalty, int topK, double topP)
    {
        lock (_pendingGate)
        {
            _speechSpeed = speed;
            _speechTemperature = temperature;
            _speechRepetition = repetitionPenalty;
            _topK = topK;
            _topP = topP;
        }
    }

    public bool HasPendingSpeech
    {
        get
        {
            lock (_pendingGate)
            {
                return _activeId >= 0 || !_queue.IsIdle;
            }
        }
    }

    public void Stop()
    {
        lock (_pendingGate)
        {
            _queue.Clear();
            _activeId = -1;
            _activeCacheKey = null;
        }

        Interlocked.Increment(ref _speechEpoch);
        TryWrite("{\"cmd\":\"cancel\"}");
        _work.Set();
    }

    public void NotifyPlaybackCompleted()
    {
        VoiceClip? next = null;
        var continuing = false;
        var idle = false;
        lock (_pendingGate)
        {
            _queue.CompletePlayback();
            next = _queue.TryStartPlayback();
            continuing = next is null && _queue.IsSynthesizing;
            idle = next is null && _queue.IsIdle;
        }

        _work.Set();
        if (next is not null)
        {
            PlaybackReady?.Invoke(next.Value);
        }
        else if (continuing)
        {
            SynthesisContinuing?.Invoke();
        }
        else if (idle)
        {
            VoiceIdle?.Invoke();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _work.Set();
        TryWrite("{\"cmd\":\"exit\"}");
        var process = _process;
        if (process is not null)
        {
            try
            {
                if (!process.WaitForExit(1500))
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // The process had already exited.
            }
        }

        _thread?.Join(1500);
        process?.Dispose();
    }

    private void StartSession()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            return;
        }

        _thread = new Thread(RunSession)
        {
            IsBackground = true,
            Name = "JiayiVoice"
        };
        _thread.Start();
    }

    private void RunSession()
    {
        try
        {
            while (Volatile.Read(ref _disposed) == 0)
            {
                if (Volatile.Read(ref _parked) == 1)
                {
                    _work.WaitOne();
                    if (Volatile.Read(ref _disposed) != 0)
                    {
                        break;
                    }

                    Interlocked.Exchange(ref _parked, 0);
                }

                VoiceTurn? turn = null;
                var id = 0;
                var speed = _speechSpeed;
                var temperature = _speechTemperature;
                var repetition = _speechRepetition;
                var topK = _topK;
                var topP = _topP;
                lock (_pendingGate)
                {
                    speed = _speechSpeed;
                    temperature = _speechTemperature;
                    repetition = _speechRepetition;
                    topK = _topK;
                    topP = _topP;
                    if (Enabled)
                    {
                        turn = _queue.TryStartSynthesis();
                        if (turn is not null)
                        {
                            id = Interlocked.Increment(ref _requestId);
                            _activeId = id;
                        }
                    }
                }

                var speechEpoch = Volatile.Read(ref _speechEpoch);
                VoiceReference? reference = null;
                if (turn is not null)
                {
                    reference = _pack.Select(
                        turn.Value.Tone,
                        turn.Value.Text,
                        turn.Value.Trigger,
                        _lastReferenceId);
                    _lastReferenceId = reference.Id;
                    var cacheKey = SpeechClipCache.Key(
                        turn.Value.Text,
                        reference.Id,
                        speed,
                        temperature,
                        repetition,
                        topK,
                        topP);
                    var cached = _cache.Find(cacheKey);
                    if (cached is not null)
                    {
                        var copy = Path.Combine(_outputDirectory, $"{id}.wav");
                        try
                        {
                            File.Copy(cached, copy, overwrite: true);
                            if (Volatile.Read(ref _speechEpoch) != speechEpoch)
                            {
                                continue;
                            }

                            SynthesisStarted?.Invoke(turn.Value.Text);
                            DeliverSynthesized(copy);
                            continue;
                        }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                        {
                            Log(error.Message);
                        }
                    }

                    if (Volatile.Read(ref _ready) != 1)
                    {
                        lock (_pendingGate)
                        {
                            _queue.ReleaseSynthesisToFront();
                            _activeId = -1;
                        }

                        if (!EnsureProcess())
                        {
                            _work.WaitOne(1000);
                        }
                        else
                        {
                            _work.WaitOne(TimeSpan.FromSeconds(30));
                        }

                        continue;
                    }

                    lock (_pendingGate)
                    {
                        _activeCacheKey = cacheKey;
                    }
                }

                if (turn is null)
                {
                    var idle = false;
                    lock (_pendingGate)
                    {
                        idle = _queue.IsIdle;
                    }

                    var quietFor = DateTime.UtcNow - _lastInferenceUtc;
                    if (idle && quietFor >= InferenceParkAfter && !ProcessAlive())
                    {
                        Interlocked.Exchange(ref _parked, 1);
                        continue;
                    }

                    if (!EnsureProcess())
                    {
                        _work.WaitOne(1000);
                        continue;
                    }

                    if (Volatile.Read(ref _releaseWhenIdle) == 1
                        && ShouldParkInference(
                            Volatile.Read(ref _ready) == 1,
                            idle,
                            quietFor,
                            TimeSpan.Zero))
                    {
                        Interlocked.Exchange(ref _releaseWhenIdle, 0);
                        ParkInference();
                        continue;
                    }

                    _work.WaitOne(TimeSpan.FromSeconds(30));
                    continue;
                }

                _lastInferenceUtc = DateTime.UtcNow;
                if (reference is null)
                {
                    continue;
                }

                var outPath = Path.Combine(_outputDirectory, $"{id}.wav");
                var request = JsonSerializer.Serialize(new
                {
                    id,
                    text = turn.Value.Text,
                    text_lang = _pack.TextLang,
                    ref_audio_path = reference.AudioPath,
                    prompt_text = reference.Prompt,
                    prompt_lang = reference.PromptLang,
                    out_path = outPath,
                    speed_factor = speed,
                    temperature,
                    repetition_penalty = repetition,
                    top_k = topK,
                    top_p = topP
                });
                SynthesisStarted?.Invoke(turn.Value.Text);
                if (Volatile.Read(ref _speechEpoch) != speechEpoch)
                {
                    continue;
                }

                if (!TryWrite(request) && Volatile.Read(ref _disposed) == 0)
                {
                    var idle = false;
                    lock (_pendingGate)
                    {
                        RequeueOnce(_queue.FailSynthesis());
                        _activeId = -1;
                        _activeCacheKey = null;
                        idle = _queue.IsIdle;
                    }

                    Volatile.Write(ref _ready, 0);
                    StartProcess();
                    _work.Set();
                    if (idle)
                    {
                        VoiceIdle?.Invoke();
                    }
                }
            }
        }
        catch (Exception error)
        {
            Log(error.ToString());
            lock (_pendingGate)
            {
                RequeueOnce(_queue.FailSynthesis());
                _activeId = -1;
                _activeCacheKey = null;
            }
        }
        finally
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                Interlocked.Exchange(ref _started, 0);
            }
        }
    }

    private bool ProcessAlive()
    {
        var process = _process;
        return process is not null && !HasExited(process);
    }

    private void DeliverSynthesized(string path)
    {
        VoiceClip? clip = null;
        var idle = false;
        lock (_pendingGate)
        {
            _activeId = -1;
            _activeCacheKey = null;
            _retriedText = null;
            if (_queue.CompleteSynthesis(path))
            {
                clip = _queue.TryStartPlayback();
            }

            idle = clip is null && _queue.IsIdle;
        }

        if (clip is not null)
        {
            PlaybackReady?.Invoke(clip.Value);
        }
        else if (idle)
        {
            VoiceIdle?.Invoke();
        }

        _work.Set();
    }

    private bool EnsureProcess()
    {
        var process = _process;
        if (process is not null && !HasExited(process))
        {
            return true;
        }

        return StartProcess();
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return true;
        }
    }

    private bool StartProcess()
    {
        var previous = _process;
        if (previous is not null)
        {
            try
            {
                if (!previous.HasExited)
                {
                    previous.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // The previous process had already exited.
            }

            previous.Dispose();
        }

        Volatile.Write(ref _ready, 0);
        var start = new ProcessStartInfo
        {
            FileName = _runtime.PythonPath,
            WorkingDirectory = _runtime.RootPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = _utf8,
            StandardErrorEncoding = _utf8
        };
        start.ArgumentList.Add(_scriptPath);
        start.ArgumentList.Add("--root");
        start.ArgumentList.Add(_runtime.RootPath);
        start.ArgumentList.Add("--gpt");
        start.ArgumentList.Add(_pack.GptPath);
        start.ArgumentList.Add("--sovits");
        start.ArgumentList.Add(_pack.SovitsPath);
        start.ArgumentList.Add("--bert");
        start.ArgumentList.Add(_runtime.BertPath);
        start.ArgumentList.Add("--hubert");
        start.ArgumentList.Add(_runtime.HubertPath);
        var warmup = _pack.References.FirstOrDefault(reference => reference.Fallback)
            ?? _pack.References[0];
        if (File.Exists(warmup.AudioPath))
        {
            start.ArgumentList.Add("--warmup-ref");
            start.ArgumentList.Add(warmup.AudioPath);
            start.ArgumentList.Add("--warmup-prompt");
            start.ArgumentList.Add(warmup.Prompt);
        }
        start.Environment["PYTHONUTF8"] = "1";
        start.Environment["PYTHONIOENCODING"] = "utf-8";

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start())
        {
            process.Dispose();
            return false;
        }

        _process = process;
        _stdin = new StreamWriter(process.StandardInput.BaseStream, _utf8) { AutoFlush = true };
        process.OutputDataReceived += OnOutput;
        process.ErrorDataReceived += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.Data))
            {
                return;
            }

            if (TryReadDecodeFraction(args.Data, out var fraction))
            {
                InferenceProgress?.Invoke(fraction);
                return;
            }

            if (IsEngineNoise(args.Data))
            {
                return;
            }

            Log(args.Data);
        };
        process.Exited += (_, _) => OnProcessExited();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return true;
    }

    internal static bool ShouldParkInference(bool ready, bool queueIdle, TimeSpan quietFor, TimeSpan limit) =>
        ready && queueIdle && quietFor >= limit;

    private void ParkInference()
    {
        if (Interlocked.Exchange(ref _parked, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _ready, 0);
        TryWrite("{\"cmd\":\"exit\"}");
        var process = _process;
        _process = null;
        _stdin = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited && !process.WaitForExit(1500))
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The process had already exited.
        }

        process.Dispose();
    }

    private void OnProcessExited()
    {
        if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _parked) == 1)
        {
            Volatile.Write(ref _ready, 0);
            return;
        }

        Volatile.Write(ref _ready, 0);
        var idle = false;
        lock (_pendingGate)
        {
            RequeueOnce(_queue.FailSynthesis());
            _activeId = -1;
            idle = _queue.IsIdle;
        }

        _work.Set();
        if (idle)
        {
            VoiceIdle?.Invoke();
        }
    }

    private void RequeueOnce(VoiceTurn? turn)
    {
        if (turn is null)
        {
            return;
        }

        if (string.Equals(_retriedText, turn.Value.Text, StringComparison.Ordinal))
        {
            _retriedText = null;
            return;
        }

        _retriedText = turn.Value.Text;
        _queue.EnqueueFront(turn.Value);
    }

    private bool TryWrite(string line)
    {
        lock (_stdinGate)
        {
            try
            {
                var stdin = _stdin;
                if (stdin is null)
                {
                    return false;
                }

                stdin.WriteLine(line);
                return true;
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException)
            {
                Log(error.Message);
                return false;
            }
        }
    }

    private void OnOutput(object sender, DataReceivedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Data) || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(args.Data);
            var root = document.RootElement;
            if (root.TryGetProperty("ready", out var ready) && ready.ValueKind == JsonValueKind.True)
            {
                Volatile.Write(ref _ready, 1);
                _work.Set();
                return;
            }

            if (!root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out var id))
            {
                return;
            }

            var succeeded = root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
            VoiceClip? clip = null;
            var idle = false;
            lock (_pendingGate)
            {
                if (id != _activeId)
                {
                    return;
                }

                _activeId = -1;
                if (succeeded
                    && root.TryGetProperty("path", out var pathElement)
                    && pathElement.GetString() is { Length: > 0 } path
                    && File.Exists(path))
                {
                    var cacheKey = _activeCacheKey;
                    _activeCacheKey = null;
                    _retriedText = null;
                    if (cacheKey is not null)
                    {
                        _cache.Store(cacheKey, path);
                    }

                    if (_queue.CompleteSynthesis(path))
                    {
                        clip = _queue.TryStartPlayback();
                    }
                }
                else
                {
                    _activeCacheKey = null;
                    RequeueOnce(_queue.FailSynthesis());
                    if (root.TryGetProperty("error", out var error))
                    {
                        Log(error.GetString() ?? "tts failed");
                    }
                }

                idle = clip is null && _queue.IsIdle;
            }

            if (clip is not null)
            {
                PlaybackReady?.Invoke(clip.Value);
            }
            else if (idle)
            {
                VoiceIdle?.Invoke();
            }

            _work.Set();
        }
        catch (JsonException)
        {
            Log(args.Data);
        }
    }

    private static bool IsEngineNoise(string line) =>
        line.Contains("it/s", StringComparison.Ordinal)
        || line.Contains("%|", StringComparison.Ordinal)
        || line.Contains("T2S Decoding", StringComparison.Ordinal);

    private static bool TryReadDecodeFraction(string line, out double fraction)
    {
        fraction = 0;
        var slash = line.LastIndexOf('/');
        if (slash <= 0 || slash >= line.Length - 1)
        {
            return false;
        }

        var left = slash - 1;
        while (left >= 0 && char.IsDigit(line[left]))
        {
            left--;
        }

        var right = slash + 1;
        while (right < line.Length && char.IsDigit(line[right]))
        {
            right++;
        }

        if (left == slash - 1 || right == slash + 1)
        {
            return false;
        }

        if (!int.TryParse(line.AsSpan(left + 1, slash - left - 1), out var current)
            || !int.TryParse(line.AsSpan(slash + 1, right - slash - 1), out var total)
            || total < 100
            || current < 0)
        {
            return false;
        }

        fraction = Math.Min(0.92, current / 280d);
        return true;
    }

    private void Log(string message)
    {
        try
        {
            var info = new FileInfo(_logPath);
            if (info.Exists && info.Length > 256 * 1024)
            {
                var tail = File.ReadAllText(_logPath, _utf8);
                if (tail.Length > 32 * 1024)
                {
                    tail = tail[^ (32 * 1024)..];
                }

                File.WriteAllText(_logPath, tail, _utf8);
            }

            File.AppendAllText(_logPath, $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}", _utf8);
        }
        catch (IOException)
        {
            // Logging must not take down playback.
        }
    }
}
