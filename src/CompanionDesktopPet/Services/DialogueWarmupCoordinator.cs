using System.IO;

namespace CompanionDesktopPet.Services;

internal enum DialogueWarmupOutcome
{
    Ready,
    PermanentFailure,
    RetriesExhausted,
    Cancelled
}

internal sealed class DialogueWarmupCoordinator
{
    private static readonly IReadOnlyList<TimeSpan> RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30)
    ];

    private readonly object _sync = new();
    private readonly DialogueService _dialogue;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private Task<DialogueWarmupOutcome>? _run;
    private Exception? _lastError;

    internal DialogueWarmupCoordinator(
        DialogueService dialogue,
        TimeProvider? timeProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _dialogue = dialogue ?? throw new ArgumentNullException(nameof(dialogue));
        var clock = timeProvider ?? TimeProvider.System;
        _delayAsync = delayAsync
            ?? ((delay, cancellationToken) => Task.Delay(delay, clock, cancellationToken));
    }

    internal Exception? LastError
    {
        get
        {
            lock (_sync)
            {
                return _lastError;
            }
        }
    }

    internal Task<DialogueWarmupOutcome> StartAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _run ??= RunCoreAsync(cancellationToken);
            return _run;
        }
    }

    private async Task<DialogueWarmupOutcome> RunCoreAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return DialogueWarmupOutcome.Cancelled;
            }

            bool ready;
            Exception? error = null;
            try
            {
                ready = await _dialogue.WarmupAsync().ConfigureAwait(false);
                error = _dialogue.LastWarmupException;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return DialogueWarmupOutcome.Cancelled;
            }
            catch (Exception exception) when (!IsFatalException(exception))
            {
                ready = false;
                error = exception;
            }

            if (ready)
            {
                SetLastError(null);
                return DialogueWarmupOutcome.Ready;
            }

            SetLastError(error);
            if (IsPermanent(error))
            {
                return DialogueWarmupOutcome.PermanentFailure;
            }

            if (attempt >= RetryDelays.Count)
            {
                return DialogueWarmupOutcome.RetriesExhausted;
            }

            try
            {
                await _delayAsync(RetryDelays[attempt], cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return DialogueWarmupOutcome.Cancelled;
            }
        }
    }

    private void SetLastError(Exception? error)
    {
        lock (_sync)
        {
            _lastError = error;
        }
    }

    private static bool IsPermanent(Exception? exception) =>
        exception is InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or FormatException
            or NotSupportedException
            or TypeInitializationException;

    private static bool IsFatalException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException;
}
