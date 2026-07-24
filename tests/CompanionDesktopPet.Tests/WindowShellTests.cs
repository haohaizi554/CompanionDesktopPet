using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class WindowShellTests
{
    private static readonly Lazy<StaTestHost> StaHost = new(() => new StaTestHost());

    [Fact]
    public void MainWindow_ProvidesAmbientLayersAndSchedulesTheFirstGreeting()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var window = CreateWindow(settingsDirectory);

            Assert.IsType<System.Windows.Controls.Image>(window.FindName("BlinkOverlay"));
            var greetingBadge = Assert.IsType<Border>(window.FindName("GreetingBadge"));
            Assert.Equal("嗨♡", Assert.IsType<TextBlock>(greetingBadge.Child).Text);
            Assert.IsType<TranslateTransform>(window.FindName("GreetingBadgeOffset"));
            Assert.IsType<MenuItem>(window.FindName("GreetingMenuItem"));

            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var ambientTimer = GetPrivateField<DispatcherTimer>(window, "_ambientTimer");
            Assert.True(ambientTimer.IsEnabled);
            Assert.Equal(TimeSpan.FromMilliseconds(650), ambientTimer.Interval);
            Assert.Equal(
                PetAmbientAction.Greeting,
                GetPrivateField<PetAmbientAction>(window, "_pendingAmbientAction"));

            window.Close();
            DeleteSettingsDirectory(settingsDirectory);
        });
    }

    [Fact]
    public async Task MainWindow_StartupGreetingCompletesThenSchedulesOneFreshBlink()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var window = CreateWindowWithScheduler(
                settingsDirectory,
                new AmbientActionScheduler(() => 0.5));
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            await Task.Delay(800);

            var badge = Assert.IsType<Border>(window.FindName("GreetingBadge"));
            var coordinator = GetPrivateField<PetActionCoordinator>(window, "_actionCoordinator");
            Assert.Equal(PetActionState.Greeting, coordinator.State);
            Assert.True(badge.HasAnimatedProperties);

            await Task.Delay(1_100);

            var ambientTimer = GetPrivateField<DispatcherTimer>(window, "_ambientTimer");
            Assert.Equal(PetActionState.Idle, coordinator.State);
            Assert.True(ambientTimer.IsEnabled);
            Assert.Equal(TimeSpan.FromSeconds(5), ambientTimer.Interval);
            Assert.Equal(
                PetAmbientAction.Blink,
                GetPrivateField<PetAmbientAction>(window, "_pendingAmbientAction"));
            AssertNeutralAmbientVisuals(window);

            window.Close();
            DeleteSettingsDirectory(settingsDirectory);
        });
    }

    [Fact]
    public async Task MainWindow_PersistedPauseKeepsStartupPendingUntilResumeAndRunsItOnce()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var settings = PetSettings.Default with { AnimationPaused = true };
            var window = CreateWindowWithScheduler(
                settingsDirectory,
                new AmbientActionScheduler(() => 0.5),
                settings);
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var timer = GetPrivateField<DispatcherTimer>(window, "_ambientTimer");
            var coordinator = GetPrivateField<PetActionCoordinator>(window, "_actionCoordinator");

            Assert.False(timer.IsEnabled);
            Assert.Equal("Pending", GetPrivateFieldValue(window, "_startupGreetingState").ToString());
            InvokePrivate(window, "AmbientTimer_Tick", null, EventArgs.Empty);
            Assert.Equal(PetActionState.Paused, coordinator.State);
            Assert.Equal("Pending", GetPrivateFieldValue(window, "_startupGreetingState").ToString());
            Assert.False(timer.IsEnabled);

            Assert.IsType<MenuItem>(window.FindName("PauseMenuItem"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(timer.IsEnabled);
            Assert.Equal(TimeSpan.FromMilliseconds(650), timer.Interval);
            Assert.Equal("Scheduled", GetPrivateFieldValue(window, "_startupGreetingState").ToString());

            await Task.Delay(800);
            Assert.Equal(PetActionState.Greeting, coordinator.State);
            Assert.Equal("Running", GetPrivateFieldValue(window, "_startupGreetingState").ToString());

            await Task.Delay(1_100);
            Assert.Equal(PetActionState.Idle, coordinator.State);
            Assert.Equal("Completed", GetPrivateFieldValue(window, "_startupGreetingState").ToString());
            Assert.True(timer.IsEnabled);
            Assert.Equal(TimeSpan.FromSeconds(5), timer.Interval);
            Assert.Equal(PetAmbientAction.Blink, GetPrivateField<PetAmbientAction>(window, "_pendingAmbientAction"));

            InvokePrivate(window, "Window_ContentRendered", null, EventArgs.Empty);
            Assert.Equal("Completed", GetPrivateFieldValue(window, "_startupGreetingState").ToString());
            Assert.Equal(PetAmbientAction.Blink, GetPrivateField<PetAmbientAction>(window, "_pendingAmbientAction"));

            window.Close();
            DeleteSettingsDirectory(settingsDirectory);
        });
    }

    [Fact]
    public async Task MainWindow_ManualGreetingDoesNotConsumeScheduledStartupGreeting()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var window = CreateWindowWithScheduler(
                settingsDirectory,
                new AmbientActionScheduler(() => 0.5));
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var startupReply = GetLastReply(window);
            var timer = GetPrivateField<DispatcherTimer>(window, "_ambientTimer");

            Assert.Equal("Scheduled", GetPrivateFieldValue(window, "_startupGreetingState").ToString());
            Assert.IsType<MenuItem>(window.FindName("GreetingMenuItem"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Same(startupReply, GetLastReply(window));
            Assert.Equal("Pending", GetPrivateFieldValue(window, "_startupGreetingState").ToString());
            Assert.False(timer.IsEnabled);

            await Task.Delay(1_200);

            Assert.Equal("Scheduled", GetPrivateFieldValue(window, "_startupGreetingState").ToString());
            Assert.True(timer.IsEnabled);
            Assert.Equal(TimeSpan.FromMilliseconds(650), timer.Interval);
            Assert.Equal(PetAmbientAction.Greeting, GetPrivateField<PetAmbientAction>(window, "_pendingAmbientAction"));

            InvokePrivate(window, "AmbientTimer_Tick", null, EventArgs.Empty);
            Assert.Equal(PetActionState.Idle, GetPrivateField<PetActionCoordinator>(window, "_actionCoordinator").State);
            Assert.Equal("Scheduled", GetPrivateFieldValue(window, "_startupGreetingState").ToString());
            Assert.True(timer.IsEnabled);

            window.Close();
            DeleteSettingsDirectory(settingsDirectory);
        });
    }

    [Fact]
    public void MainWindow_CancelledRunningStartupGreetingIsConsumedAndDoesNotReplay()
    {
        var settingsDirectory = CreateSettingsDirectory();
        RunOnStaThread(() =>
        {
            var monotonicTime = new ManualTimeProvider();
            var window = CreateWindowWithScheduler(
                settingsDirectory,
                CreateSchedulerWithTimeProvider(() => 0.5, monotonicTime));
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var timer = GetPrivateField<DispatcherTimer>(window, "_ambientTimer");

            monotonicTime.Advance(TimeSpan.FromMilliseconds(650));
            InvokePrivate(window, "AmbientTimer_Tick", null, EventArgs.Empty);
            Assert.Equal("Running", GetPrivateFieldValue(window, "_startupGreetingState").ToString());

            var pause = Assert.IsType<MenuItem>(window.FindName("PauseMenuItem"));
            pause.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal("Completed", GetPrivateFieldValue(window, "_startupGreetingState").ToString());
            pause.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.True(timer.IsEnabled);
            Assert.Equal(TimeSpan.FromSeconds(5), timer.Interval);
            Assert.Equal(PetAmbientAction.Blink, GetPrivateField<PetAmbientAction>(window, "_pendingAmbientAction"));

            window.Close();
        });

        Assert.True(SpinWait.SpinUntil(
            () => !File.Exists(Path.Combine(settingsDirectory, "settings.json.tmp")),
            TimeSpan.FromSeconds(5)));
        DeleteSettingsDirectory(settingsDirectory);
    }

    [Fact]
    public void MainWindow_AmbientDeadlineUsesMonotonicTimeNotWallClock()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var monotonicTime = new ManualTimeProvider();
            var window = CreateWindowWithScheduler(
                settingsDirectory,
                CreateSchedulerWithTimeProvider(() => 0.5, monotonicTime));
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var timer = GetPrivateField<DispatcherTimer>(window, "_ambientTimer");
            var coordinator = GetPrivateField<PetActionCoordinator>(window, "_actionCoordinator");

            monotonicTime.SetUtcNow(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));
            InvokePrivate(window, "AmbientTimer_Tick", null, EventArgs.Empty);

            Assert.Equal(PetActionState.Idle, coordinator.State);
            Assert.Equal("Scheduled", GetPrivateFieldValue(window, "_startupGreetingState").ToString());
            Assert.True(timer.IsEnabled);

            monotonicTime.SetUtcNow(new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero));
            monotonicTime.Advance(TimeSpan.FromMilliseconds(650));
            InvokePrivate(window, "AmbientTimer_Tick", null, EventArgs.Empty);

            Assert.Equal(PetActionState.Greeting, coordinator.State);
            Assert.Equal("Running", GetPrivateFieldValue(window, "_startupGreetingState").ToString());
            window.Close();
            DeleteSettingsDirectory(settingsDirectory);
        });
    }

    [Fact]
    public void MainWindow_GreetingMenuIsLocalAndRejectedTicksPreserveStartupWork()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var window = CreateWindowWithScheduler(
                settingsDirectory,
                new AmbientActionScheduler(() => 0.5));
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var startupReply = GetLastReply(window);

            var greeting = Assert.IsType<MenuItem>(window.FindName("GreetingMenuItem"));
            Assert.Equal("打个招呼♡", greeting.Header);
            greeting.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            var badge = Assert.IsType<Border>(window.FindName("GreetingBadge"));
            var badgeOffset = Assert.IsType<TranslateTransform>(
                window.FindName("GreetingBadgeOffset"));
            var coordinator = GetPrivateField<PetActionCoordinator>(window, "_actionCoordinator");
            Assert.Same(startupReply, GetLastReply(window));
            Assert.Equal(PetActionState.Greeting, coordinator.State);
            Assert.True(badge.HasAnimatedProperties);
            Assert.True(badgeOffset.HasAnimatedProperties);
            Assert.True(Assert.IsType<RotateTransform>(window.FindName("ActionRotation"))
                .HasAnimatedProperties);

            InvokePrivate(window, "AmbientTimer_Tick", null, EventArgs.Empty);

            var ambientTimer = GetPrivateField<DispatcherTimer>(window, "_ambientTimer");
            Assert.Equal(PetActionState.Greeting, coordinator.State);
            Assert.False(ambientTimer.IsEnabled);
            Assert.Equal("Pending", GetPrivateFieldValue(window, "_startupGreetingState").ToString());
            Assert.Equal(
                PetAmbientAction.Greeting,
                GetPrivateField<PetAmbientAction>(window, "_pendingAmbientAction"));

            window.Close();
            DeleteSettingsDirectory(settingsDirectory);
        });
    }

    [Fact]
    public void MainWindow_PauseResumeAndCloseControlAmbientWorkWithoutBlockingHearts()
    {
        var settingsDirectory = CreateSettingsDirectory();
        RunOnStaThread(() =>
        {
            var window = CreateWindowWithScheduler(
                settingsDirectory,
                new AmbientActionScheduler(() => 0.5));
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var greeting = Assert.IsType<MenuItem>(window.FindName("GreetingMenuItem"));
            var pause = Assert.IsType<MenuItem>(window.FindName("PauseMenuItem"));
            var say = Assert.IsType<MenuItem>(window.FindName("SayMenuItem"));
            var ambientTimer = GetPrivateField<DispatcherTimer>(window, "_ambientTimer");
            var coordinator = GetPrivateField<PetActionCoordinator>(window, "_actionCoordinator");

            greeting.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            pause.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.False(ambientTimer.IsEnabled);
            Assert.Equal(PetActionState.Paused, coordinator.State);
            AssertNeutralAmbientVisuals(window);

            say.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(Assert.IsType<TextBlock>(window.FindName("HeartOne"))
                .HasAnimatedProperties);

            pause.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Equal(PetActionState.Idle, coordinator.State);
            Assert.True(ambientTimer.IsEnabled);
            Assert.Equal(TimeSpan.FromMilliseconds(650), ambientTimer.Interval);
            Assert.Equal(
                PetAmbientAction.Greeting,
                GetPrivateField<PetAmbientAction>(window, "_pendingAmbientAction"));

            window.Close();
            Assert.False(ambientTimer.IsEnabled);
        });

        Assert.True(SpinWait.SpinUntil(
            () => !File.Exists(Path.Combine(settingsDirectory, "settings.json.tmp")),
            TimeSpan.FromSeconds(5)));
        DeleteSettingsDirectory(settingsDirectory);
    }

    [Fact]
    public void MainWindow_DragAndLandingTakePriorityAndRestoreThePriorPauseState()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var window = CreateWindowWithScheduler(
                settingsDirectory,
                new AmbientActionScheduler(() => 0.5));
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var ambientTimer = GetPrivateField<DispatcherTimer>(window, "_ambientTimer");
            var coordinator = GetPrivateField<PetActionCoordinator>(window, "_actionCoordinator");

            Assert.IsType<MenuItem>(window.FindName("GreetingMenuItem"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            InvokePrivate(window, "BeginDragAction");
            Assert.False(ambientTimer.IsEnabled);
            Assert.Equal(PetActionState.Dragging, coordinator.State);
            AssertNeutralAmbientVisuals(window);

            InvokePrivate(window, "BeginLandingAction");
            Assert.Equal(PetActionState.Landing, coordinator.State);
            WaitForCondition(
                () => coordinator.State == PetActionState.Idle,
                TimeSpan.FromSeconds(3),
                () => $"Landing did not complete to Idle; current state: {coordinator.State}.");
            Assert.Equal(PetActionState.Idle, coordinator.State);
            Assert.True(ambientTimer.IsEnabled);
            Assert.Equal(TimeSpan.FromMilliseconds(650), ambientTimer.Interval);
            Assert.Equal(PetAmbientAction.Greeting, GetPrivateField<PetAmbientAction>(window, "_pendingAmbientAction"));

            Assert.IsType<MenuItem>(window.FindName("PauseMenuItem"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            InvokePrivate(window, "BeginDragAction");
            InvokePrivate(window, "BeginLandingAction");
            WaitForCondition(
                () => coordinator.State == PetActionState.Paused,
                TimeSpan.FromSeconds(3),
                () => $"Landing did not restore Paused; current state: {coordinator.State}.");

            Assert.Equal(PetActionState.Paused, coordinator.State);
            Assert.False(ambientTimer.IsEnabled);
            AssertNeutralAmbientVisuals(window);

            window.Close();
            DeleteSettingsDirectory(settingsDirectory);
        });
    }

    [Fact]
    public async Task MainWindow_CloseDuringDragDoesNotStartLandingOrPostDragWork()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var window = CreateWindowWithScheduler(
                settingsDirectory,
                new AmbientActionScheduler(() => 0.5));
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var ambientTimer = GetPrivateField<DispatcherTimer>(window, "_ambientTimer");
            var automaticTimer = GetPrivateField<DispatcherTimer>(window, "_automaticTimer");
            var coordinator = GetPrivateField<PetActionCoordinator>(window, "_actionCoordinator");
            var replyBeforeClose = GetLastReply(window);

            InvokePrivate(window, "BeginDragAction");
            Assert.Equal(PetActionState.Dragging, coordinator.State);
            window.Close();
            Assert.False(ambientTimer.IsEnabled);
            Assert.False(automaticTimer.IsEnabled);

            InvokePrivate(window, "BeginLandingAction");

            Assert.Equal(PetActionState.Dragging, coordinator.State);
            AssertNeutralAmbientVisuals(window);

            await InvokePrivateAsync(window, "CompleteDragAfterMoveAsync");
            await Task.Delay(100);

            Assert.Same(replyBeforeClose, GetLastReply(window));
            Assert.False(ambientTimer.IsEnabled);
            Assert.False(automaticTimer.IsEnabled);
            AssertNeutralAmbientVisuals(window);
            Assert.False(File.Exists(Path.Combine(settingsDirectory, "settings.json")));
            Assert.False(File.Exists(Path.Combine(settingsDirectory, "settings.json.tmp")));
            DeleteSettingsDirectory(settingsDirectory);
        });
    }

    [Fact]
    public void MainWindow_PostCloseAmbientTickCannotMutateStateOrRestartWork()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var window = CreateWindowWithScheduler(
                settingsDirectory,
                new AmbientActionScheduler(() => 0.5));
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var timer = GetPrivateField<DispatcherTimer>(window, "_ambientTimer");
            var coordinator = GetPrivateField<PetActionCoordinator>(window, "_actionCoordinator");

            window.Close();
            var stateAfterClose = coordinator.State;
            var startupStateAfterClose = GetPrivateFieldValue(window, "_startupGreetingState").ToString();
            InvokePrivate(window, "AmbientTimer_Tick", null, EventArgs.Empty);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            Assert.Equal(stateAfterClose, coordinator.State);
            Assert.Equal(startupStateAfterClose, GetPrivateFieldValue(window, "_startupGreetingState").ToString());
            Assert.False(timer.IsEnabled);
            AssertNeutralAmbientVisuals(window);
            DeleteSettingsDirectory(settingsDirectory);
        });
    }

    [Fact]
    public async Task MainWindow_SmokeActionProbeCompletesRealActionsAndRestoresNeutralState()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var window = CreateWindow(settingsDirectory);
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var stopwatch = Stopwatch.StartNew();

            var completed = await window.RunSmokeActionProbeAsync();

            Assert.True(completed);
            Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(3));
            var ambientTimer = GetPrivateField<DispatcherTimer>(window, "_ambientTimer");
            Assert.True(ambientTimer.IsEnabled);
            Assert.Equal(TimeSpan.FromMilliseconds(650), ambientTimer.Interval);
            Assert.Equal("Scheduled", GetPrivateFieldValue(window, "_startupGreetingState").ToString());
            AssertNeutralAmbientVisuals(window);

            window.Close();
            DeleteSettingsDirectory(settingsDirectory);
        });
    }

    [Fact]
    public void MainWindow_CloseRequestsShutdownUnlessExplicitlySuppressed()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var shutdownRequests = 0;
            var normalWindow = new MainWindow(
                PetSettings.Default,
                new SettingsService(settingsDirectory),
                shutdownApplication: () => shutdownRequests++);
            normalWindow.Show();
            normalWindow.Close();

            var suppressedWindow = new MainWindow(
                PetSettings.Default,
                new SettingsService(settingsDirectory),
                suppressApplicationShutdownOnClose: true,
                shutdownApplication: () => shutdownRequests++);
            suppressedWindow.Show();
            suppressedWindow.Close();

            Assert.Equal(1, shutdownRequests);
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, true);
            }
        });
    }

    [Fact]
    public void MainWindow_SmokeReadinessRequiresRenderedImageBubbleAndReply()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var window = new MainWindow(
                PetSettings.Default,
                new SettingsService(settingsDirectory),
                suppressApplicationShutdownOnClose: true);

            Assert.False(window.TryVerifySmokeReadiness(out var beforeRenderFailure));
            Assert.False(string.IsNullOrWhiteSpace(beforeRenderFailure));

            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            Assert.True(window.TryVerifySmokeReadiness(out var renderedFailure), renderedFailure);
            window.Close();
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, true);
            }
        });
    }

    [Fact]
    public void MainWindow_StartupReply_DoesNotDriveActionAnimationsOrHearts()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var window = new MainWindow(
                PetSettings.Default,
                new SettingsService(settingsDirectory),
                suppressApplicationShutdownOnClose: true);
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            Assert.Equal("none", GetLastReply(window).AnimationCue);
            Assert.False(Assert.IsType<ScaleTransform>(window.FindName("ActionScale")).HasAnimatedProperties);
            Assert.False(Assert.IsType<RotateTransform>(window.FindName("ActionRotation")).HasAnimatedProperties);
            Assert.False(Assert.IsType<TranslateTransform>(window.FindName("ActionOffset")).HasAnimatedProperties);
            foreach (var name in new[] { "HeartOne", "HeartTwo", "HeartThree" })
            {
                var heart = Assert.IsType<TextBlock>(window.FindName(name));
                Assert.False(heart.HasAnimatedProperties);
                Assert.Equal(0, heart.Opacity);
            }

            window.Close();
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, true);
            }
        });
    }

    [Fact]
    public void MainWindow_ConstructorAcceptsAnInjectableIdleTimeProvider()
    {
        var providerType = typeof(MainWindow).Assembly.GetType(
            "CompanionDesktopPet.Services.IIdleTimeProvider",
            throwOnError: false);
        Assert.NotNull(providerType);
        Assert.Contains(
            typeof(MainWindow).GetConstructors().SelectMany(constructor => constructor.GetParameters()),
            parameter => parameter.ParameterType == providerType
                         && parameter.Name == "idleTimeProvider"
                         && parameter.HasDefaultValue);
    }

    [Fact]
    public void MainWindow_PreservesLegacyConstructorAndExposesSchedulerInjection()
    {
        var publicConstructors = typeof(MainWindow).GetConstructors();
        var publicConstructor = Assert.Single(publicConstructors);
        Assert.Equal(
        [
            typeof(PetSettings),
            typeof(SettingsService),
            typeof(AgentMemoryService),
            typeof(AgentMemorySnapshot),
            typeof(IIdleTimeProvider),
            typeof(bool),
            typeof(Action)
        ], publicConstructor.GetParameters().Select(parameter => parameter.ParameterType));

        var injectionConstructor = Assert.Single(
            typeof(MainWindow).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.Equal(
        [
            typeof(PetSettings),
            typeof(SettingsService),
            typeof(AgentMemoryService),
            typeof(AgentMemorySnapshot),
            typeof(IIdleTimeProvider),
            typeof(bool),
            typeof(Action),
            typeof(AmbientActionScheduler)
        ], injectionConstructor.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.DoesNotContain(
            injectionConstructor.GetParameters(),
            parameter => parameter.HasDefaultValue);
    }

    [Fact]
    public void MainWindow_LegacyNullAgentMemoryServiceCallRemainsUnambiguous()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var window = new MainWindow(
                PetSettings.Default,
                new SettingsService(settingsDirectory),
                null);

            Assert.NotNull(window);
            DeleteSettingsDirectory(settingsDirectory);
        });
    }

    [Fact]
    public void MainWindow_EventTimerRunsOnlyWhileTheWindowIsLoaded()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var window = new MainWindow(
                PetSettings.Default,
                new SettingsService(settingsDirectory),
                suppressApplicationShutdownOnClose: true);
            var field = typeof(MainWindow).GetField(
                "_eventTimer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var timer = Assert.IsType<DispatcherTimer>(field!.GetValue(window));
            Assert.Equal(TimeSpan.FromSeconds(30), timer.Interval);
            Assert.False(timer.IsEnabled);

            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.True(timer.IsEnabled);

            window.Close();
            Assert.False(timer.IsEnabled);
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, true);
            }
        });
    }

    [Fact]
    public void MainWindow_StartupThenSaySomething_ReplacesTheStartupReplyWithNewV2Text()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var window = new MainWindow(
                PetSettings.Default,
                new SettingsService(settingsDirectory),
                suppressApplicationShutdownOnClose: true);
            window.Show();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var startup = GetLastReply(window);
            var say = Assert.IsType<MenuItem>(window.FindName("SayMenuItem"));

            say.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            var click = GetLastReply(window);
            var speech = Assert.IsType<TextBlock>(window.FindName("SpeechText"));
            Assert.Equal(CompanionEvent.Click, click.Trigger);
            Assert.True(click.ShouldDisplayText);
            Assert.NotNull(click.SourceLine);
            Assert.NotEqual(startup.SourceLine!.Id, click.SourceLine.Id);
            Assert.Equal(click.Text, speech.Text);
            window.Close();
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, true);
            }
        });
    }

    [Fact]
    public void MainWindow_BackgroundSilence_PreservesThePreviousBubble()
    {
        RunOnStaThread(() =>
        {
            var automaticTick = typeof(MainWindow).GetMethod(
                "AutomaticTimer_Tick",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(automaticTick);
            var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var window = new MainWindow(
                PetSettings.Default,
                new SettingsService(settingsDirectory),
                suppressApplicationShutdownOnClose: true);
            window.Show();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var bubble = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("SpeechBubble"));
            var speech = Assert.IsType<TextBlock>(window.FindName("SpeechText"));
            Assert.Equal(Visibility.Visible, bubble.Visibility);
            var startupText = speech.Text;

            automaticTick!.Invoke(window, [null, EventArgs.Empty]);

            var reply = GetLastReply(window);
            Assert.False(reply.ShouldDisplayText);
            Assert.Equal(Visibility.Visible, bubble.Visibility);
            Assert.Equal(startupText, speech.Text);
            window.Close();
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, true);
            }
        });
    }

    [Fact]
    public void MainWindow_ExplicitUserSilence_ClearsThePreviousBubble()
    {
        RunOnStaThread(() =>
        {
            var presentReply = typeof(MainWindow).GetMethod(
                "PresentReply",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(presentReply);
            var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var window = new MainWindow(
                PetSettings.Default,
                new SettingsService(settingsDirectory),
                suppressApplicationShutdownOnClose: true);
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var bubble = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("SpeechBubble"));
            var speech = Assert.IsType<TextBlock>(window.FindName("SpeechText"));
            Assert.Equal(Visibility.Visible, bubble.Visibility);

            presentReply!.Invoke(window,
            [
                new AgentReply(
                    string.Empty,
                    DialogueCategory.DailyCare,
                    DialogueTreeKind.Companion,
                    CompanionEvent.Click,
                    ShouldDisplayText: false)
            ]);

            Assert.Equal(Visibility.Collapsed, bubble.Visibility);
            Assert.Equal(string.Empty, speech.Text);
            window.Close();
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, true);
            }
        });
    }

    [Fact]
    public void MainWindow_DisplaysStartupClickAndAutomaticRepliesWithEnabledV2Provenance()
    {
        RunOnStaThread(() =>
        {
            var lastReply = typeof(MainWindow).GetProperty(
                "LastReply",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var automaticTick = typeof(MainWindow).GetMethod(
                "AutomaticTimer_Tick",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(lastReply);
            Assert.NotNull(automaticTick);

            foreach (var (trigger, enterThroughRealHandler) in new (CompanionEvent, Action<MainWindow>)[]
                     {
                         (CompanionEvent.Startup, window =>
                         {
                             window.Show();
                             window.Dispatcher.Invoke(
                                 () => { },
                                 System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                         }),
                         (CompanionEvent.Click, window =>
                         {
                             var say = Assert.IsType<MenuItem>(window.FindName("SayMenuItem"));
                             say.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                         }),
                         (CompanionEvent.Automatic, window =>
                             automaticTick!.Invoke(window, [null, EventArgs.Empty]))
                     })
            {
                var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                var window = new MainWindow(
                    PetSettings.Default,
                    new SettingsService(settingsDirectory),
                    suppressApplicationShutdownOnClose: true);

                enterThroughRealHandler(window);

                var reply = Assert.IsType<AgentReply>(lastReply!.GetValue(window));
                var source = Assert.IsType<DialogueLine>(reply.SourceLine);
                var speech = Assert.IsType<TextBlock>(window.FindName("SpeechText"));
                Assert.Equal(trigger, reply.Trigger);
                Assert.True(reply.ShouldDisplayText);
                Assert.True(source.Enabled);
                Assert.Contains(source, PersonaCorpus.All);
                Assert.Equal(source.Text, reply.Text);
                Assert.Equal(reply.Text, speech.Text);
                window.Close();
                if (Directory.Exists(settingsDirectory))
                {
                    Directory.Delete(settingsDirectory, true);
                }
            }
        });
    }

    [Fact]
    public void MainWindow_BubbleHover_PausesUntilEveryHoverSourceLeaves()
    {
        var settingsDirectory = CreateSettingsDirectory();
        RunOnStaThread(() =>
        {
            var window = CreateWindow(settingsDirectory);
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            InvokePrivate(window, "ShowBubble", "hover countdown");
            var stage = Assert.IsType<Grid>(window.FindName("CharacterStage"));
            var bubble = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("SpeechBubble"));

            InvokePrivate(window, "BubbleHover_MouseEnter", stage, null);
            Assert.False(GetPrivateField<DispatcherTimer>(window, "_bubbleTimer").IsEnabled);
            InvokePrivate(window, "BubbleHover_MouseEnter", bubble, null);
            InvokePrivate(window, "BubbleHover_MouseLeave", stage, null);
            Assert.False(GetPrivateField<DispatcherTimer>(window, "_bubbleTimer").IsEnabled);

            InvokePrivate(window, "BubbleTimer_Tick", null, EventArgs.Empty);

            Assert.Equal(Visibility.Visible, bubble.Visibility);
            InvokePrivate(window, "BubbleHover_MouseLeave", bubble, null);
            Assert.True(GetPrivateField<DispatcherTimer>(window, "_bubbleTimer").IsEnabled);
            window.Close();
        });
        DeleteSettingsDirectory(settingsDirectory);
    }

    [Theory]
    [InlineData(PetScale.Small)]
    [InlineData(PetScale.Normal)]
    [InlineData(PetScale.Large)]
    public void MainWindow_BubbleGap_IsThirtyDipsAtEveryScale(PetScale scale)
    {
        var settingsDirectory = CreateSettingsDirectory();
        RunOnStaThread(() =>
        {
            var window = CreateWindow(settingsDirectory);
            window.Show();
            InvokePrivate(window, "ApplyScale", scale);
            InvokePrivate(window, "ShowBubble", "layout measurement");
            var stage = Assert.IsType<Grid>(window.FindName("CharacterStage"));
            var bubble = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("SpeechBubble"));
            stage.RenderTransform = Transform.Identity;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();

            var bubbleBottom = bubble.TranslatePoint(
                new System.Windows.Point(0, bubble.ActualHeight), window).Y;
            var characterTop = stage.TranslatePoint(new System.Windows.Point(0, 0), window).Y;

            Assert.InRange(characterTop - bubbleBottom, 29.5, 30.5);
            window.Close();
        });
        DeleteSettingsDirectory(settingsDirectory);
    }

    [Fact]
    public void MainWindow_BubbleLayout_LongestEnabledLineIsNotClippedAtLargeScale()
    {
        var longestLine = PersonaCorpus.All
            .Where(line => line.Enabled)
            .OrderByDescending(line => line.Text.Length)
            .ThenBy(line => line.Id, StringComparer.Ordinal)
            .First();
        var settingsDirectory = CreateSettingsDirectory();
        RunOnStaThread(() =>
        {
            var window = CreateWindow(settingsDirectory);
            window.Show();
            InvokePrivate(window, "ApplyScale", PetScale.Large);
            InvokePrivate(window, "ShowBubble", longestLine.Text);
            var stage = Assert.IsType<Grid>(window.FindName("CharacterStage"));
            var bubble = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("SpeechBubble"));
            stage.RenderTransform = Transform.Identity;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();

            var bubbleTop = bubble.TranslatePoint(new System.Windows.Point(0, 0), window).Y;
            var bubbleBottom = bubble.TranslatePoint(
                new System.Windows.Point(0, bubble.ActualHeight), window).Y;
            var characterTop = stage.TranslatePoint(new System.Windows.Point(0, 0), window).Y;
            var gap = characterTop - bubbleBottom;

            Assert.True(
                bubbleTop >= 0,
                $"Bubble top {bubbleTop:F3} clips line {longestLine.Id} ({longestLine.Text.Length} chars); "
                + $"bubble height={bubble.ActualHeight:F3}, gap={gap:F3}.");
            Assert.InRange(gap, 29.5, 30.5);
            window.Close();
        });
        DeleteSettingsDirectory(settingsDirectory);
    }

    [Fact]
    public void MainWindow_KawaiiContextMenu_PreservesShellAndSubmenuBehavior()
    {
        var settingsDirectory = CreateSettingsDirectory();
        RunOnStaThread(() =>
        {
            var window = CreateWindow(settingsDirectory);
            ContextMenu? menu = null;
            MenuItem? size = null;
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                var stage = Assert.IsType<Grid>(window.FindName("CharacterStage"));
                menu = Assert.IsType<ContextMenu>(stage.ContextMenu);
                var kawaiiStyle = Assert.IsType<Style>(
                    window.FindResource("KawaiiContextMenuStyle"));
                Assert.Same(kawaiiStyle, menu.Style);

                var surface = Assert.IsType<LinearGradientBrush>(
                    window.FindResource("MenuSurfaceBrush"));
                Assert.Equal(2, surface.GradientStops.Count);

                menu.IsOpen = true;
                menu.ApplyTemplate();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var shell = Assert.IsType<Border>(menu.Template.FindName("MenuShell", menu));
                Assert.Equal(new CornerRadius(24), shell.CornerRadius);
                Assert.Equal(new Thickness(2), shell.BorderThickness);
                Assert.IsType<DropShadowEffect>(shell.Effect);

                var say = Assert.IsType<MenuItem>(window.FindName("SayMenuItem"));
                say.ApplyTemplate();
                var chrome = Assert.IsType<Border>(
                    say.Template.FindName("MenuItemChrome", say));
                Assert.Equal(new CornerRadius(14), chrome.CornerRadius);
                Assert.Equal(35, say.MinHeight);

                size = menu.Items
                    .OfType<MenuItem>()
                    .Single(item => Equals(item.Header, "大小"));
                size.ApplyTemplate();
                size.IsSubmenuOpen = true;
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var popup = Assert.IsType<Popup>(
                    size.Template.FindName("PART_Popup", size));
                Assert.True(popup.IsOpen);
            }
            finally
            {
                if (size is not null)
                {
                    size.IsSubmenuOpen = false;
                }

                if (menu is not null)
                {
                    menu.IsOpen = false;
                }

                window.Close();
            }
        });
        DeleteSettingsDirectory(settingsDirectory);
    }

    [Fact]
    public void MainWindow_UsesTransparentDesktopPetChrome()
    {
        var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        RunOnStaThread(() =>
        {
            var window = new MainWindow(
                PetSettings.Default,
                new SettingsService(settingsDirectory),
                suppressApplicationShutdownOnClose: true);

            Assert.Equal(WindowStyle.None, window.WindowStyle);
            Assert.True(window.AllowsTransparency);
            Assert.Null(window.Background);
            Assert.False(window.ShowInTaskbar);
            Assert.True(window.Topmost);
            Assert.Equal("角色桌宠", window.Title);
            Assert.NotNull(window.FindName("SpeechBubble"));
            var stage = Assert.IsType<Grid>(window.FindName("CharacterStage"));
            var hearts = Assert.IsType<Canvas>(window.FindName("HeartLayer"));
            Assert.False(hearts.IsHitTestVisible);
            var image = Assert.IsType<System.Windows.Controls.Image>(window.FindName("PetImage"));
            Assert.NotNull(image.Source);
            Assert.NotNull(stage.ContextMenu);
            Assert.True(stage.ContextMenu.Items.Count >= 8);

            window.Show();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var bubble = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("SpeechBubble"));
            var speech = Assert.IsType<TextBlock>(window.FindName("SpeechText"));
            Assert.Equal(Visibility.Visible, bubble.Visibility);
            Assert.False(string.IsNullOrWhiteSpace(speech.Text));

            var say = Assert.IsType<MenuItem>(window.FindName("SayMenuItem"));
            say.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.False(string.IsNullOrWhiteSpace(speech.Text));

            var pause = Assert.IsType<MenuItem>(window.FindName("PauseMenuItem"));
            pause.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal("继续动画", pause.Header);

            var large = Assert.IsType<MenuItem>(window.FindName("LargeSizeMenuItem"));
            large.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(390, stage.Width);

            var topmost = Assert.IsType<MenuItem>(window.FindName("TopmostMenuItem"));
            topmost.IsChecked = false;
            topmost.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.False(window.Topmost);
            window.Close();
        });

        Assert.True(SpinWait.SpinUntil(
            () => !File.Exists(Path.Combine(settingsDirectory, "settings.json.tmp")),
            TimeSpan.FromSeconds(5)));
        if (Directory.Exists(settingsDirectory))
        {
            Directory.Delete(settingsDirectory, true);
        }
    }

    private static void RunOnStaThread(Action action) => StaHost.Value.Invoke(action);

    private static Task RunOnStaThreadAsync(Func<Task> action) =>
        StaHost.Value.InvokeAsync(action);

    private static MainWindow CreateWindow(string settingsDirectory) =>
        new(
            PetSettings.Default,
            new SettingsService(settingsDirectory),
            suppressApplicationShutdownOnClose: true);

    private static MainWindow CreateWindowWithScheduler(
        string settingsDirectory,
        AmbientActionScheduler ambientScheduler,
        PetSettings? settings = null)
    {
        var constructor = typeof(MainWindow).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(PetSettings),
                typeof(SettingsService),
                typeof(AgentMemoryService),
                typeof(AgentMemorySnapshot),
                typeof(IIdleTimeProvider),
                typeof(bool),
                typeof(Action),
                typeof(AmbientActionScheduler)
            ],
            modifiers: null);
        Assert.NotNull(constructor);
        return Assert.IsType<MainWindow>(constructor!.Invoke(
        [
            settings ?? PetSettings.Default,
            new SettingsService(settingsDirectory),
            null,
            null,
            null,
            true,
            null,
            ambientScheduler
        ]));
    }

    private static object GetPrivateFieldValue(MainWindow window, string fieldName)
    {
        var field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field!.GetValue(window);
        Assert.NotNull(value);
        return value;
    }

    private static AmbientActionScheduler CreateSchedulerWithTimeProvider(
        Func<double> sample,
        TimeProvider timeProvider)
    {
        return Assert.IsType<AmbientActionScheduler>(Activator.CreateInstance(
            typeof(AmbientActionScheduler),
            [sample, timeProvider]));
    }

    private static T GetPrivateField<T>(MainWindow window, string fieldName)
    {
        var field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field!.GetValue(window));
    }

    private static void InvokePrivate(
        MainWindow window,
        string methodName,
        params object?[] arguments)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(window, arguments);
    }

    private static void WaitForCondition(
        Func<bool> condition,
        TimeSpan timeout,
        Func<string> failureMessage)
    {
        if (condition())
        {
            return;
        }

        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        var stopwatch = Stopwatch.StartNew();
        var poll = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        poll.Tick += (_, _) =>
        {
            if (condition() || stopwatch.Elapsed >= timeout)
            {
                frame.Continue = false;
            }
        };

        poll.Start();
        try
        {
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            poll.Stop();
        }

        Assert.True(condition(), failureMessage());
    }

    private static Task InvokePrivateAsync(
        MainWindow window,
        string methodName,
        params object?[] arguments)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Task>(method!.Invoke(window, arguments));
    }

    private static void AssertNeutralAmbientVisuals(MainWindow window)
    {
        var blink = Assert.IsType<System.Windows.Controls.Image>(window.FindName("BlinkOverlay"));
        var badge = Assert.IsType<Border>(window.FindName("GreetingBadge"));
        var badgeOffset = Assert.IsType<TranslateTransform>(
            window.FindName("GreetingBadgeOffset"));
        var scale = Assert.IsType<ScaleTransform>(window.FindName("ActionScale"));
        var rotation = Assert.IsType<RotateTransform>(window.FindName("ActionRotation"));
        var offset = Assert.IsType<TranslateTransform>(window.FindName("ActionOffset"));

        Assert.False(blink.HasAnimatedProperties);
        Assert.False(badge.HasAnimatedProperties);
        Assert.False(badgeOffset.HasAnimatedProperties);
        Assert.False(scale.HasAnimatedProperties);
        Assert.False(rotation.HasAnimatedProperties);
        Assert.False(offset.HasAnimatedProperties);
        Assert.Equal(0, blink.Opacity);
        Assert.Equal(0, badge.Opacity);
        Assert.Equal(0, badgeOffset.X);
        Assert.Equal(8, badgeOffset.Y);
        Assert.Equal(1, scale.ScaleX);
        Assert.Equal(1, scale.ScaleY);
        Assert.Equal(0, rotation.Angle);
        Assert.Equal(0, offset.X);
        Assert.Equal(0, offset.Y);
    }

    private static string CreateSettingsDirectory() =>
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    private static void DeleteSettingsDirectory(string settingsDirectory)
    {
        if (Directory.Exists(settingsDirectory))
        {
            Directory.Delete(settingsDirectory, true);
        }
    }

    private sealed class StaTestHost
    {
        private readonly Application _application;
        private readonly Dispatcher _dispatcher;

        public StaTestHost()
        {
            Application? application = null;
            Dispatcher? dispatcher = null;
            Exception? initializationException = null;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                try
                {
                    // Window tests need a real WPF lifetime without invoking production App.OnStartup.
                    application = new Application
                    {
                        ShutdownMode = ShutdownMode.OnExplicitShutdown
                    };
                    application.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri(
                            "/CompanionDesktopPet;component/Themes/PetTheme.xaml",
                            UriKind.Relative)
                    });
                    dispatcher = Dispatcher.CurrentDispatcher;
                }
                catch (Exception caught)
                {
                    initializationException = caught;
                }
                finally
                {
                    ready.Set();
                }

                if (initializationException is null)
                {
                    application!.Run();
                }
            })
            {
                IsBackground = true,
                Name = "CompanionDesktopPet.WpfTests"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();

            if (initializationException is not null)
            {
                ExceptionDispatchInfo.Capture(initializationException).Throw();
            }

            _application = application
                ?? throw new InvalidOperationException("The WPF test application did not start.");
            _dispatcher = dispatcher
                ?? throw new InvalidOperationException("The WPF test dispatcher did not start.");
        }

        public void Invoke(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            _dispatcher.Invoke(action);
        }

        public Task InvokeAsync(Func<Task> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            return _dispatcher.InvokeAsync(action).Task.Unwrap();
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        private DateTimeOffset _utcNow = new(2026, 7, 24, 0, 0, 0, TimeSpan.Zero);

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan elapsed) => _timestamp += elapsed.Ticks;

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }

    private static AgentReply GetLastReply(MainWindow window)
    {
        var property = typeof(MainWindow).GetProperty(
            "LastReply",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return Assert.IsType<AgentReply>(property!.GetValue(window));
    }
}
