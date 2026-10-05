using System.Net.Http;
using System.Text.Json;
using Android.Content;
using Android.Media;
using Android.OS;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Android;

internal static class LinkDefaults
{
    public const string VoiceDomain = "inanshan.sjhcip.com";
    public const string VoiceHost = "http://43.138.138.200:8765";
    public const string DialogueHost = "http://192.168.1.46:8766";
    public const string ModelUrl = "http://43.138.138.200:8588/v1";
    public const string ModelName = "Qwen3.6-35B-A3B-oQ4-fp16-mtp";
    public const string ApiKey = "8588";
    public const string Auth = "x-api-key";
}

internal static class VoiceHostStore
{
    public const string DefaultHost = LinkDefaults.VoiceHost;

    public static string Get(Context context)
    {
        var preferences = context.GetSharedPreferences("jiayi", FileCreationMode.Private);
        var saved = preferences?.GetString("voice_host", DefaultHost) ?? "";
        var staleLan = saved.Contains("192.168.1.", StringComparison.Ordinal)
            || saved.Contains("10.0.2.2", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(saved) || (!IsEmulator() && staleLan))
        {
            preferences?.Edit()?.PutString("voice_host", DefaultHost)?.Apply();
            return DefaultHost;
        }

        return saved.Trim();
    }

    private static bool IsEmulator()
    {
        var fingerprint = Build.Fingerprint ?? "";
        var model = Build.Model ?? "";
        return fingerprint.Contains("generic", StringComparison.OrdinalIgnoreCase)
            || fingerprint.Contains("emulator", StringComparison.OrdinalIgnoreCase)
            || model.Contains("sdk", StringComparison.OrdinalIgnoreCase)
            || model.Contains("Emulator", StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(Context context, string value)
    {
        context.GetSharedPreferences("jiayi", FileCreationMode.Private)
            ?.Edit()
            ?.PutString("voice_host", value.Trim())
            ?.Apply();
    }
}

internal sealed class PcGpuVoiceSpeaker : IVoiceSpeaker
{
    private readonly string _baseUrl;
    private readonly string _cache;
    private readonly Context _context;
    private readonly HttpClient _http = new(new SocketsHttpHandler
    {
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(8)
    })
    { Timeout = Timeout.InfiniteTimeSpan };
    private readonly Handler _main = new(Looper.MainLooper!);
    private int _generation;
    private int _pending;
    private int _pump;
    private readonly object _speechGate = new();
    private CancellationTokenSource? _requestCancel;
    private string? _nextText;
    private string? _nextTone;
    private string? _nextTrigger;
    private MediaPlayer? _player;
    private byte[] _levels = [];
    private MouthTick? _tick;
    private double _speed = 1;
    private double _temperature = 1;
    private double _repetition = 1.35;
    private int _topK = 15;
    private double _topP = 1;

    private PcGpuVoiceSpeaker(Context context, string baseUrl, string cache)
    {
        _context = context;
        _baseUrl = baseUrl.TrimEnd('/');
        _cache = cache;
        Directory.CreateDirectory(cache);
        _tick = new MouthTick(this);
    }

    public static PcGpuVoiceSpeaker? TryCreate(Context context, string? baseUrl, string? stateDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        var cache = Path.Combine(
            stateDirectory ?? context.CacheDir!.AbsolutePath,
            "voice-cache");
        return new PcGpuVoiceSpeaker(context, baseUrl, cache);
    }

    public bool Enabled { get; set; } = true;

    public bool HasPendingSpeech => Volatile.Read(ref _pending) != 0;

    public event Action<string>? SynthesisStarted;

    public event Action<double>? InferenceProgress;

    public event Action<VoiceClip>? PlaybackReady;

    public event Action? SynthesisContinuing;

    public event Action? VoiceIdle;

    public event Action<string>? Failed;

    public event Action<byte>? Mouth;

    public event Action<float>? PlaybackFraction;

    public void Speak(string text, string? tone, string? trigger = null, bool urgent = false)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        CancellationTokenSource? previous;
        lock (_speechGate)
        {
            _nextText = text.Trim();
            _nextTone = tone;
            _nextTrigger = trigger;
            Interlocked.Increment(ref _generation);
            previous = _requestCancel;
            _requestCancel = new CancellationTokenSource();
        }

        previous?.Cancel();
        Interlocked.Exchange(ref _pending, 1);
        SynthesisStarted?.Invoke(text);
        if (Interlocked.CompareExchange(ref _pump, 1, 0) == 0)
        {
            _ = Task.Run(PumpSpeechAsync);
        }
    }

    private async Task PumpSpeechAsync()
    {
        while (true)
        {
            string? text;
            string? tone;
            string? trigger;
            int generation;
            lock (_speechGate)
            {
                if (_nextText is null)
                {
                    _pump = 0;
                    if (_nextText is null)
                    {
                        return;
                    }

                    _pump = 1;
                }

                text = _nextText;
                _nextText = null;
                tone = _nextTone;
                trigger = _nextTrigger;
                generation = Volatile.Read(ref _generation);
            }

            if (text is null || generation != Volatile.Read(ref _generation))
            {
                continue;
            }

            var request = _requestCancel;
            if (request is null)
            {
                continue;
            }

            try
            {
                request.CancelAfter(TimeSpan.FromSeconds(45));
                var payload = JsonSerializer.Serialize(new
                {
                    text,
                    tone,
                    trigger,
                    speed_factor = _speed,
                    temperature = _temperature,
                    repetition_penalty = _repetition,
                    top_k = _topK,
                    top_p = _topP
                });
                using var response = await _http.PostAsync(
                    _baseUrl + "/v1/speak",
                    new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
                    request.Token).ConfigureAwait(false);
                var bytes = await response.Content.ReadAsByteArrayAsync(request.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode || bytes.Length < 44)
                {
                    throw new HttpRequestException("voice returned no audio");
                }

                if (generation != Volatile.Read(ref _generation))
                {
                    continue;
                }

                var path = Path.Combine(_cache, generation + ".wav");
                await File.WriteAllBytesAsync(path, bytes, request.Token).ConfigureAwait(false);
                var levels = SpeechMouthTimeline.FromWav(path);
                PlaybackReady?.Invoke(new VoiceClip(text, path));
                _main.Post(() => Play(path, generation, levels));
            }
            catch (Exception)
            {
                if (generation == Volatile.Read(ref _generation))
                {
                    Failed?.Invoke("这句话的语音没回来。对话还能继续，你接着说就行。");
                    Finish(generation);
                }
            }
        }
    }

    public void ApplySpeech(double speed, double temperature, double repetitionPenalty, int topK, double topP)
    {
        _speed = speed;
        _temperature = temperature;
        _repetition = repetitionPenalty;
        _topK = topK;
        _topP = topP;
    }

    public void Stop()
    {
        CancellationTokenSource? previous;
        int generation;
        lock (_speechGate)
        {
            _nextText = null;
            generation = Interlocked.Increment(ref _generation);
            previous = _requestCancel;
            _requestCancel = null;
        }

        previous?.Cancel();
        Interlocked.Exchange(ref _pending, 0);
        _main.Post(() => CompleteOnMain(generation));
    }

    public void NotifyPlaybackCompleted() => DetachPlayer();

    public void Dispose()
    {
        Stop();
        _http.Dispose();
    }

    private void Play(string path, int generation, byte[] levels)
    {
        if (generation != Volatile.Read(ref _generation))
        {
            return;
        }

        DetachPlayer();
        _levels = levels;
        var player = new MediaPlayer();
        player.Prepared += (_, _) =>
        {
            if (generation != Volatile.Read(ref _generation))
            {
                ReleasePlayer(player);
                return;
            }

            player.Start();
            _player = player;
            InferenceProgress?.Invoke(1);
            if (_tick is not null)
            {
                _main.RemoveCallbacks(_tick);
                _main.Post(_tick);
            }
        };
        player.Completion += (_, _) => Finish(generation);
        player.Error += (_, _) => Finish(generation);
        player.SetAudioAttributes(new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.Media)!
            .SetContentType(AudioContentType.Speech)!
            .Build()!);
        player.SetDataSource(path);
        player.PrepareAsync();
    }

    private void DetachPlayer()
    {
        if (Looper.MyLooper() != _main.Looper)
        {
            _main.Post(DetachPlayer);
            return;
        }

        if (_tick is not null)
        {
            _main.RemoveCallbacks(_tick);
        }

        _levels = [];
        Mouth?.Invoke(0);
        var player = _player;
        _player = null;
        if (player is not null)
        {
            ReleasePlayer(player);
        }
    }

    private static void ReleasePlayer(MediaPlayer player)
    {
        Task.Run(() =>
        {
            try
            {
                player.Release();
            }
            catch (Exception)
            {
            }
        });
    }

    private void Finish(int generation)
    {
        if (generation != Volatile.Read(ref _generation))
        {
            return;
        }

        Interlocked.Exchange(ref _pending, 0);
        _main.Post(() => CompleteOnMain(generation));
    }

    private void CompleteOnMain(int generation)
    {
        if (generation != Volatile.Read(ref _generation))
        {
            return;
        }

        DetachPlayer();
        VoiceIdle?.Invoke();
    }

    private void TickMouth()
    {
        var player = _player;
        if (player is null || _tick is null)
        {
            Mouth?.Invoke(0);
            return;
        }

        byte level;
        float fraction;
        try
        {
            var position = Math.Max(0, player.CurrentPosition);
            var duration = Math.Max(1, player.Duration);
            fraction = position / (float)duration;
            level = SpeechMouthTimeline.LevelAt(_levels, TimeSpan.FromMilliseconds(position));
        }
        catch (Exception)
        {
            Mouth?.Invoke(0);
            return;
        }

        Mouth?.Invoke(level);
        PlaybackFraction?.Invoke(fraction);
        _main.PostDelayed(_tick, 40);
    }

    private sealed class MouthTick : Java.Lang.Object, Java.Lang.IRunnable
    {
        private readonly PcGpuVoiceSpeaker _owner;

        public MouthTick(PcGpuVoiceSpeaker owner) => _owner = owner;

        public void Run() => _owner.TickMouth();
    }
}
