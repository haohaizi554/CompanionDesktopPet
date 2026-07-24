using System.Diagnostics;
using System.Threading;

namespace CompanionDesktopPet.Services;

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly object _lifetimeGate = new();
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private RegisteredWaitHandle? _activationRegistration;
    private Action? _activationCallback;
    private bool _disposed;

    public SingleInstanceGuard(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _mutex = new Mutex(initiallyOwned: false, name, out var createdNew);
        try
        {
            _activationEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                name + "-Activation");
            IsPrimaryInstance = createdNew;
        }
        catch
        {
            _mutex.Dispose();
            throw;
        }
    }

    public bool IsPrimaryInstance { get; }

    public void RegisterActivationCallback(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!IsPrimaryInstance)
            {
                throw new InvalidOperationException(
                    "Only the primary instance can listen for activation.");
            }

            if (_activationRegistration is not null)
            {
                throw new InvalidOperationException(
                    "The activation callback has already been registered.");
            }

            _activationCallback = callback;
            _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
                _activationEvent,
                static (state, timedOut) =>
                {
                    if (!timedOut)
                    {
                        ((SingleInstanceGuard)state!).HandleActivationSignal();
                    }
                },
                this,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
    }

    public bool SignalPrimaryInstance()
    {
        lock (_lifetimeGate)
        {
            if (_disposed)
            {
                return false;
            }

            return _activationEvent.Set();
        }
    }

    private void HandleActivationSignal()
    {
        Action? callback;
        lock (_lifetimeGate)
        {
            if (_disposed)
            {
                return;
            }

            callback = _activationCallback;
        }

        try
        {
            callback?.Invoke();
        }
        catch (Exception exception) when (!IsFatalException(exception))
        {
            Trace.TraceError("Primary activation callback failed: {0}", exception);
        }
    }

    public void Dispose()
    {
        RegisteredWaitHandle? activationRegistration;
        lock (_lifetimeGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _activationCallback = null;
            activationRegistration = _activationRegistration;
            _activationRegistration = null;
        }

        if (activationRegistration is not null)
        {
            using var callbacksCompleted = new ManualResetEvent(false);
            if (activationRegistration.Unregister(callbacksCompleted))
            {
                callbacksCompleted.WaitOne();
            }
        }

        _activationEvent.Dispose();
        _mutex.Dispose();
    }

    private static bool IsFatalException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException;
}
