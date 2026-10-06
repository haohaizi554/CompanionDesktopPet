using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace CompanionDesktopPet.Services;

internal sealed class GpuHostVoiceSpeaker : IVoiceSpeaker
{
    private readonly string _baseUrl;
    private readonly HttpClient _http;
    private readonly string _cache;
    private readonly object _gate = new();
    private readonly Queue<SpeechPiece> _queue = new();
    private readonly SemaphoreSlim _wake = new(0);
    private CancellationTokenSource? _request;
    private int _generation;
    private int _inFlight;
    private int _playing;
    private int _disposed;
    private double _speed = 1;
    private double _temperature = 1;
    private double _repetition = 1.35;
    private int _topK = 15;
    private double _topP = 1;

    private GpuHostVoiceSpeaker(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            PooledConnectionLifetime = TimeSpan.Zero
        };
        _http = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _http.DefaultRequestHeaders.ConnectionClose = true;
        _cache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CompanionDesktopPet",
            "gpu-voice");
        Directory.CreateDirectory(_cache);
        _ = PumpAsync();
    }

    public bool Enabled { get; set; } = true;

    public event Action<string>? SynthesisStarted;

    public event Action<double>? InferenceProgress;

    public event Action<VoiceClip>? PlaybackReady;

    public event Action? SynthesisContinuing;

    public event Action? VoiceIdle;

    public static GpuHostVoiceSpeaker? TryCreate(string startDirectory)
    {
        var raw = ReadEmbeddedHost() ?? ReadSidecarHost(startDirectory);
        if (string.IsNullOrWhiteSpace(raw)
            || !Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        return new GpuHostVoiceSpeaker(uri.GetLeftPart(UriPartial.Authority));
    }

    private static string? ReadEmbeddedHost()
    {
        using var stream = typeof(GpuHostVoiceSpeaker).Assembly.GetManifestResourceStream(
            "CompanionDesktopPet.voice-host.txt");
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd().Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string? ReadSidecarHost(string startDirectory)
    {
        var marker = Path.Combine(startDirectory, "voice-host.txt");
        if (!File.Exists(marker))
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(marker).Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool HasPendingSpeech
    {
        get
        {
            lock (_gate)
            {
                return _queue.Count > 0 || _inFlight != 0 || _playing != 0;
            }
        }
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

        lock (_gate)
        {
            var generation = Volatile.Read(ref _generation);
            _queue.Enqueue(new SpeechPiece(pieces[0], tone, trigger, generation));
            for (var index = 1; index < pieces.Count; index++)
            {
                _queue.Enqueue(new SpeechPiece(pieces[index], tone, trigger, generation));
            }
        }

        _wake.Release();
    }

    public void ApplySpeech(double speed, double temperature, double repetitionPenalty, int topK, double topP)
    {
        lock (_gate)
        {
            _speed = speed;
            _temperature = temperature;
            _repetition = repetitionPenalty;
            _topK = topK;
            _topP = topP;
        }
    }

    public void Stop()
    {
        CancellationTokenSource? request;
        lock (_gate)
        {
            _queue.Clear();
            Interlocked.Increment(ref _generation);
            _playing = 0;
            request = _request;
            _request = null;
        }

        request?.Cancel();
        VoiceIdle?.Invoke();
    }

    public void NotifyPlaybackCompleted()
    {
        var continuing = false;
        var idle = false;
        lock (_gate)
        {
            if (_playing > 0)
            {
                _playing--;
            }

            continuing = _queue.Count > 0 || _inFlight != 0;
            idle = !continuing && _playing == 0;
        }

        if (continuing)
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

        CancellationTokenSource? request;
        lock (_gate)
        {
            _queue.Clear();
            Interlocked.Increment(ref _generation);
            request = _request;
            _request = null;
        }

        request?.Cancel();
        _wake.Release();
        _http.Dispose();
    }

    private async Task PumpAsync()
    {
        while (Volatile.Read(ref _disposed) == 0)
        {
            try
            {
                await _wake.WaitAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            SpeechPiece piece;
            double speed;
            double temperature;
            double repetition;
            int topK;
            double topP;
            CancellationTokenSource request;
            lock (_gate)
            {
                if (_inFlight != 0 || _queue.Count == 0)
                {
                    continue;
                }

                piece = _queue.Dequeue();
                if (piece.Generation != Volatile.Read(ref _generation))
                {
                    _wake.Release();
                    continue;
                }

                _inFlight = 1;
                speed = _speed;
                temperature = _temperature;
                repetition = _repetition;
                topK = _topK;
                topP = _topP;
                request = new CancellationTokenSource();
                _request = request;
            }

            SynthesisStarted?.Invoke(piece.Text);
            var sent = false;
            try
            {
                for (var attempt = 0; attempt < 2 && !sent; attempt++)
                {
                    try
                    {
                        request.CancelAfter(TimeSpan.FromSeconds(80));
                        var payload = JsonSerializer.Serialize(new
                        {
                            text = piece.Text,
                            tone = piece.Tone,
                            trigger = piece.Trigger,
                            speed_factor = speed,
                            temperature,
                            repetition_penalty = repetition,
                            top_k = topK,
                            top_p = topP
                        });
                        using var response = await _http.PostAsync(
                            _baseUrl + "/v1/speak",
                            new StringContent(payload, Encoding.UTF8, "application/json"),
                            request.Token).ConfigureAwait(false);
                        var bytes = await response.Content.ReadAsByteArrayAsync(request.Token).ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode || bytes.Length < 44)
                        {
                            throw new HttpRequestException("voice returned no audio");
                        }

                        if (piece.Generation != Volatile.Read(ref _generation))
                        {
                            break;
                        }

                        var path = Path.Combine(_cache, piece.Generation + "-" + Guid.NewGuid().ToString("N") + ".wav");
                        await File.WriteAllBytesAsync(path, bytes, request.Token).ConfigureAwait(false);
                        if (piece.Generation != Volatile.Read(ref _generation))
                        {
                            break;
                        }

                        lock (_gate)
                        {
                            _playing++;
                        }

                        InferenceProgress?.Invoke(1);
                        PlaybackReady?.Invoke(new VoiceClip(piece.Text, path));
                        sent = true;
                    }
                    catch (Exception) when (
                        attempt == 0
                        && piece.Generation == Volatile.Read(ref _generation)
                        && !request.IsCancellationRequested)
                    {
                    }
                }
            }
            catch (Exception)
            {
                sent = false;
            }
            finally
            {
                lock (_gate)
                {
                    _inFlight = 0;
                    if (ReferenceEquals(_request, request))
                    {
                        _request = null;
                    }
                }

                request.Dispose();
                if (piece.Generation == Volatile.Read(ref _generation))
                {
                    var idle = false;
                    var more = false;
                    lock (_gate)
                    {
                        more = _queue.Count > 0;
                        idle = !sent && !more && _playing == 0;
                    }

                    if (more)
                    {
                        _wake.Release();
                    }
                    else if (idle)
                    {
                        VoiceIdle?.Invoke();
                    }
                }
            }
        }
    }

    private readonly record struct SpeechPiece(string Text, string? Tone, string? Trigger, int Generation);
}
