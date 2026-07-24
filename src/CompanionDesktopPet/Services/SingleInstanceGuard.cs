using System.Diagnostics;
using System.Threading;

namespace CompanionDesktopPet.Services;

internal interface ISingleInstanceKernelObjectFactory
{
    EventWaitHandle CreateActivationEvent(string name);
    Mutex CreateMutex(string name, out bool createdNew);
}

internal sealed class WindowsSingleInstanceKernelObjectFactory
    : ISingleInstanceKernelObjectFactory
{
    internal static WindowsSingleInstanceKernelObjectFactory Instance { get; } = new();

    public EventWaitHandle CreateActivationEvent(string name) =>
        new(initialState: false, EventResetMode.AutoReset, name);

    public Mutex CreateMutex(string name, out bool createdNew) =>
        new(initiallyOwned: false, name, out createdNew);
}

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly object _lifetimeGate = new();
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly HashSet<int> _callbackThreadIds = [];
    private RegisteredWaitHandle? _activationRegistration;
    private Action? _activationCallback;
    private bool _disposed;
    private bool _nativeResourcesDisposed;

    public SingleInstanceGuard(string name)
        : this(name, WindowsSingleInstanceKernelObjectFactory.Instance)
    {
    }

    internal SingleInstanceGuard(
        string name,
        ISingleInstanceKernelObjectFactory kernelObjectFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(kernelObjectFactory);

        EventWaitHandle? activationEvent = null;
        Mutex? mutex = null;
        try
        {
            // Hold the signal object before publishing the mutex marker so a duplicate
            // can never observe a primary that does not yet own the event lifetime.
            activationEvent = kernelObjectFactory.CreateActivationEvent(name + "-Activation")
                ?? throw new InvalidOperationException(
                    "The activation event factory returned null.");
            mutex = kernelObjectFactory.CreateMutex(name, out var createdNew)
                ?? throw new InvalidOperationException("The mutex factory returned null.");

            _activationEvent = activationEvent;
            _mutex = mutex;
            IsPrimaryInstance = createdNew;
        }
        catch
        {
            mutex?.Dispose();
            activationEvent?.Dispose();
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
        var callbackThreadId = Environment.CurrentManagedThreadId;
        lock (_lifetimeGate)
        {
            if (_disposed)
            {
                return;
            }

            _callbackThreadIds.Add(callbackThreadId);
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
        finally
        {
            lock (_lifetimeGate)
            {
                _callbackThreadIds.Remove(callbackThreadId);
            }
        }
    }

    public void Dispose()
    {
        RegisteredWaitHandle? activationRegistration;
        bool calledFromActivationCallback;
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
            calledFromActivationCallback = _callbackThreadIds.Contains(
                Environment.CurrentManagedThreadId);
        }

        if (activationRegistration is null)
        {
            DisposeNativeResources();
            return;
        }

        var callbacksCompleted = new ManualResetEvent(false);
        bool unregisterSucceeded;
        try
        {
            unregisterSucceeded = activationRegistration.Unregister(callbacksCompleted);
        }
        catch (Exception exception) when (!IsFatalException(exception))
        {
            callbacksCompleted.Dispose();
            Trace.TraceError(
                "Could not unregister the single-instance activation wait; " +
                "native handles remain until process exit: {0}",
                exception);
            return;
        }

        if (!unregisterSucceeded)
        {
            callbacksCompleted.Dispose();
            Trace.TraceError(
                "Could not unregister the single-instance activation wait; " +
                "native handles remain until process exit.");
            return;
        }

        if (calledFromActivationCallback)
        {
            QueueDeferredNativeCleanup(callbacksCompleted);
            return;
        }

        try
        {
            callbacksCompleted.WaitOne();
        }
        finally
        {
            callbacksCompleted.Dispose();
        }

        DisposeNativeResources();
    }

    private void QueueDeferredNativeCleanup(ManualResetEvent callbacksCompleted)
    {
        if (ThreadPool.QueueUserWorkItem(
                static state =>
                {
                    var cleanup = (DeferredCleanup)state!;
                    try
                    {
                        cleanup.CallbacksCompleted.WaitOne();
                    }
                    finally
                    {
                        cleanup.CallbacksCompleted.Dispose();
                        cleanup.Guard.DisposeNativeResources();
                    }
                },
                new DeferredCleanup(this, callbacksCompleted)))
        {
            return;
        }

        Trace.TraceError(
            "Could not queue deferred single-instance cleanup; the completion event " +
            "and native handles remain until process exit.");
    }

    private void DisposeNativeResources()
    {
        lock (_lifetimeGate)
        {
            if (_nativeResourcesDisposed)
            {
                return;
            }

            _nativeResourcesDisposed = true;
        }

        try
        {
            _activationEvent.Dispose();
        }
        finally
        {
            _mutex.Dispose();
        }
    }

    private static bool IsFatalException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException;

    private sealed record DeferredCleanup(
        SingleInstanceGuard Guard,
        ManualResetEvent CallbacksCompleted);
}
