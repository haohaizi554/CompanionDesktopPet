using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace CompanionDesktopPet.Services;

internal readonly record struct PersonaDialogueReply(bool Ok, string Text, bool Fallback);

internal sealed class PersonaDialogueClient : IDisposable
{
    private readonly ProcessStartInfo _start;
    private readonly object _gate = new();
    private readonly Dictionary<int, TaskCompletionSource<PersonaDialogueReply>> _pending = new();
    private readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Process? _process;
    private StreamWriter? _stdin;
    private int _nextId;
    private int _disposed;
    private string? _lastError;

    internal PersonaDialogueClient(ProcessStartInfo start)
    {
        _start = start ?? throw new ArgumentNullException(nameof(start));
    }

    internal static PersonaDialogueClient? TryCreate()
    {
        var paths = PersonaDialoguePaths.Resolve();
        if (paths is null)
        {
            return null;
        }

        var start = new ProcessStartInfo
        {
            FileName = paths.PythonPath,
            WorkingDirectory = Path.GetDirectoryName(paths.ServePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        };
        start.ArgumentList.Add(paths.ServePath);
        start.ArgumentList.Add("--config");
        start.ArgumentList.Add(paths.ConfigPath);
        start.ArgumentList.Add("--soul");
        start.ArgumentList.Add(paths.SoulPath);
        start.ArgumentList.Add("--corpus");
        start.ArgumentList.Add(paths.CorpusPath);
        start.ArgumentList.Add("--checkpoint");
        start.ArgumentList.Add(paths.CheckpointPath);
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        start.Environment["PYTHONPATH"] = start.WorkingDirectory;
        return new PersonaDialogueClient(start);
    }

    internal bool IsReady => _ready.Task.IsCompletedSuccessfully && _process is { HasExited: false };

    internal string? StartupError => _lastError;

    internal async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsReady)
        {
            return true;
        }

        TaskCompletionSource<bool> ready;
        lock (_gate)
        {
            if (_process is { HasExited: false })
            {
                ready = _ready;
            }
            else
            {
                ready = _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var process = new Process { StartInfo = _start, EnableRaisingEvents = true };
                if (!process.Start())
                {
                    process.Dispose();
                    _lastError = "对话进程没有启动。";
                    ready.TrySetResult(false);
                    return false;
                }

                _process = process;
                _stdin = new StreamWriter(process.StandardInput.BaseStream, _utf8) { AutoFlush = true };
                var readySignal = ready;
                process.OutputDataReceived += (_, args) => OnOutput(args.Data, readySignal);
                process.ErrorDataReceived += (_, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                    {
                        _lastError = args.Data;
                    }
                };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
        }

        try
        {
            return await ready.Task.WaitAsync(TimeSpan.FromSeconds(90), cancellationToken);
        }
        catch (TimeoutException)
        {
            _lastError ??= "对话进程没有在时限内准备好。";
            Stop();
            return false;
        }
        catch (OperationCanceledException)
        {
            Stop();
            return false;
        }
    }

    internal async Task<PersonaDialogueReply> ReplyAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!IsReady || _stdin is null)
        {
            return new PersonaDialogueReply(false, "对话还没准备好。", false);
        }

        var id = Interlocked.Increment(ref _nextId);
        var pending = new TaskCompletionSource<PersonaDialogueReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _pending[id] = pending;
        }

        try
        {
            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["id"] = id,
                ["type"] = "reply",
                ["text"] = text,
                ["thread_id"] = "jiayi"
            });
            lock (_gate)
            {
                _stdin.WriteLine(payload);
            }

            return await pending.Task.WaitAsync(TimeSpan.FromSeconds(50), cancellationToken);
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or ObjectDisposedException)
        {
            lock (_gate)
            {
                _pending.Remove(id);
            }

            return new PersonaDialogueReply(false, "这句话我没接住。", false);
        }
    }

    internal void Stop()
    {
        var process = _process;
        _process = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited && _stdin is not null)
            {
                _stdin.WriteLine("""{"type":"exit"}""");
            }
        }
        catch (IOException)
        {
        }

        try
        {
            if (!process.WaitForExit(1500))
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        process.Dispose();
        _stdin?.Dispose();
        _stdin = null;
        FailPending();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        Stop();
    }

    private void OnOutput(string? line, TaskCompletionSource<bool> ready)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : null;
            if (type == "ready")
            {
                ready.TrySetResult(true);
                return;
            }

            var id = root.TryGetProperty("id", out var idValue) && idValue.TryGetInt32(out var parsed) ? parsed : -1;
            TaskCompletionSource<PersonaDialogueReply>? pending;
            lock (_gate)
            {
                _pending.Remove(id, out pending);
            }

            if (pending is null)
            {
                if (type == "error")
                {
                    _lastError = root.TryGetProperty("message", out var message) ? message.GetString() : line;
                    ready.TrySetResult(false);
                }

                return;
            }

            if (type == "reply")
            {
                var text = root.TryGetProperty("text", out var textValue) ? textValue.GetString() ?? "" : "";
                var fallback = root.TryGetProperty("fallback", out var fallbackValue) && fallbackValue.ValueKind == JsonValueKind.True;
                pending.TrySetResult(new PersonaDialogueReply(true, text, fallback));
                return;
            }

            var error = root.TryGetProperty("message", out var errorValue) ? errorValue.GetString() ?? "" : "对话没有回答。";
            pending.TrySetResult(new PersonaDialogueReply(false, error, false));
        }
        catch (JsonException)
        {
            _lastError = line;
        }
    }

    private void FailPending()
    {
        List<TaskCompletionSource<PersonaDialogueReply>> pending;
        lock (_gate)
        {
            pending = _pending.Values.ToList();
            _pending.Clear();
        }

        foreach (var item in pending)
        {
            item.TrySetResult(new PersonaDialogueReply(false, "对话停了。", false));
        }
    }
}
