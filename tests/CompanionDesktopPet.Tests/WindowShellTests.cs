using System.IO;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;
using CompanionDesktopPet.UI;
using DrawingIcon = System.Drawing.Icon;

namespace CompanionDesktopPet.Tests;

[Collection(WpfApplicationCollection.Name)]
public sealed class WindowShellTests
{
    private static readonly TimeSpan BlockingCallTimeout = TimeSpan.FromSeconds(10);
    private static readonly Lazy<StaTestHost> StaHost = new(() => new StaTestHost());

    [Theory]
    [InlineData(0, 320, ClickSide.Left)]
    [InlineData(159.99, 320, ClickSide.Left)]
    [InlineData(160, 320, ClickSide.Right)]
    [InlineData(320, 320, ClickSide.Right)]
    [InlineData(100, 0, ClickSide.Left)]
    public void MainWindow_ClickPosition_ResolvesTheTouchedHalf(
        double horizontalPosition,
        double renderedWidth,
        ClickSide expected)
    {
        Assert.Equal(
            expected,
            MainWindow.ResolveClickSide(horizontalPosition, renderedWidth));
    }

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

            var ambient = window.CaptureAmbientRuntime();
            Assert.True(ambient.IsScheduled);
            Assert.Equal(TimeSpan.FromMilliseconds(650), ambient.ScheduledDelay);
            Assert.Equal(
                PetAmbientAction.Greeting,
                ambient.PendingAction);

            window.Close();
            DeleteSettingsDirectory(settingsDirectory);
        });
    }

    [Fact]
    public void MainWindow_StartupGreetingCompletesThenSchedulesOneFreshBlink()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var monotonicTime = new ManualTimeProvider();
            var animations = new ControlledAnimationController();
            var window = CreateWindowWithScheduler(
                settingsDirectory,
                CreateSchedulerWithTimeProvider(() => 0.5, monotonicTime),
                animationController: animations);
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            monotonicTime.Advance(TimeSpan.FromMilliseconds(650));
            window.ProcessAmbientSchedule();

            var ambient = window.CaptureAmbientRuntime();
            Assert.Equal(PetActionState.Greeting, ambient.ActionState);
            Assert.Equal(StartupGreetingPhase.Running, ambient.StartupGreeting);

            animations.CompleteAmbientAction();

            ambient = window.CaptureAmbientRuntime();
            Assert.Equal(PetActionState.Idle, ambient.ActionState);
            Assert.Equal(StartupGreetingPhase.Completed, ambient.StartupGreeting);
            Assert.True(ambient.IsScheduled);
            Assert.Equal(TimeSpan.FromSeconds(5), ambient.ScheduledDelay);
            Assert.Equal(
                PetAmbientAction.Blink,
                ambient.PendingAction);

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
            var monotonicTime = new ManualTimeProvider();
            var animations = new ControlledAnimationController();
            var window = CreateWindowWithScheduler(
                settingsDirectory,
                CreateSchedulerWithTimeProvider(() => 0.5, monotonicTime),
                settings,
                animations);
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var ambient = window.CaptureAmbientRuntime();
            Assert.False(ambient.IsScheduled);
            Assert.Equal(StartupGreetingPhase.Pending, ambient.StartupGreeting);
            window.ProcessAmbientSchedule();
            ambient = window.CaptureAmbientRuntime();
            Assert.Equal(PetActionState.Paused, ambient.ActionState);
            Assert.Equal(StartupGreetingPhase.Pending, ambient.StartupGreeting);
            Assert.False(ambient.IsScheduled);

            await window.ToggleAnimationAsync();
            ambient = window.CaptureAmbientRuntime();
            Assert.True(ambient.IsScheduled);
            Assert.Equal(TimeSpan.FromMilliseconds(650), ambient.ScheduledDelay);
            Assert.Equal(StartupGreetingPhase.Scheduled, ambient.StartupGreeting);

            monotonicTime.Advance(TimeSpan.FromMilliseconds(650));
            window.ProcessAmbientSchedule();
            ambient = window.CaptureAmbientRuntime();
            Assert.Equal(PetActionState.Greeting, ambient.ActionState);
            Assert.Equal(StartupGreetingPhase.Running, ambient.StartupGreeting);

            animations.CompleteAmbientAction();
            ambient = window.CaptureAmbientRuntime();
            Assert.Equal(PetActionState.Idle, ambient.ActionState);
            Assert.Equal(StartupGreetingPhase.Completed, ambient.StartupGreeting);
            Assert.True(ambient.IsScheduled);
            Assert.Equal(TimeSpan.FromSeconds(5), ambient.ScheduledDelay);
            Assert.Equal(PetAmbientAction.Blink, ambient.PendingAction);

            window.ProcessPresentationRendered();
            ambient = window.CaptureAmbientRuntime();
            Assert.Equal(StartupGreetingPhase.Completed, ambient.StartupGreeting);
            Assert.Equal(PetAmbientAction.Blink, ambient.PendingAction);

            window.Close();
            DeleteSettingsDirectory(settingsDirectory);
        });
    }

    [Fact]
    public void MainWindow_ManualGreetingDoesNotConsumeScheduledStartupGreeting()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var monotonicTime = new ManualTimeProvider();
            var animations = new ControlledAnimationController();
            var window = CreateWindowWithScheduler(
                settingsDirectory,
                CreateSchedulerWithTimeProvider(() => 0.5, monotonicTime),
                animationController: animations);
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var startupReply = GetLastReply(window);

            Assert.Equal(
                StartupGreetingPhase.Scheduled,
                window.CaptureAmbientRuntime().StartupGreeting);
            Assert.IsType<MenuItem>(window.FindName("GreetingMenuItem"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Same(startupReply, GetLastReply(window));
            var ambient = window.CaptureAmbientRuntime();
            Assert.Equal(StartupGreetingPhase.Pending, ambient.StartupGreeting);
            Assert.Equal(PetActionState.Greeting, ambient.ActionState);
            Assert.False(ambient.IsScheduled);

            animations.CompleteAmbientAction();
            ambient = window.CaptureAmbientRuntime();
            Assert.Equal(StartupGreetingPhase.Scheduled, ambient.StartupGreeting);
            Assert.True(ambient.IsScheduled);
            Assert.Equal(TimeSpan.FromMilliseconds(650), ambient.ScheduledDelay);
            Assert.Equal(PetAmbientAction.Greeting, ambient.PendingAction);

            window.ProcessAmbientSchedule();
            ambient = window.CaptureAmbientRuntime();
            Assert.Equal(PetActionState.Idle, ambient.ActionState);
            Assert.Equal(StartupGreetingPhase.Scheduled, ambient.StartupGreeting);
            Assert.True(ambient.IsScheduled);

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

            monotonicTime.Advance(TimeSpan.FromMilliseconds(650));
            window.ProcessAmbientSchedule();
            Assert.Equal(
                StartupGreetingPhase.Running,
                window.CaptureAmbientRuntime().StartupGreeting);

            var pause = Assert.IsType<MenuItem>(window.FindName("PauseMenuItem"));
            pause.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(
                StartupGreetingPhase.Completed,
                window.CaptureAmbientRuntime().StartupGreeting);
            pause.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            var ambient = window.CaptureAmbientRuntime();
            Assert.True(ambient.IsScheduled);
            Assert.Equal(TimeSpan.FromSeconds(5), ambient.ScheduledDelay);
            Assert.Equal(PetAmbientAction.Blink, ambient.PendingAction);

            window.Close();
        });

        Assert.True(SpinWait.SpinUntil(
            () => !HasPendingSettingsWrite(settingsDirectory),
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

            monotonicTime.SetUtcNow(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));
            window.ProcessAmbientSchedule();

            var ambient = window.CaptureAmbientRuntime();
            Assert.Equal(PetActionState.Idle, ambient.ActionState);
            Assert.Equal(StartupGreetingPhase.Scheduled, ambient.StartupGreeting);
            Assert.True(ambient.IsScheduled);

            monotonicTime.SetUtcNow(new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero));
            monotonicTime.Advance(TimeSpan.FromMilliseconds(650));
            window.ProcessAmbientSchedule();

            ambient = window.CaptureAmbientRuntime();
            Assert.Equal(PetActionState.Greeting, ambient.ActionState);
            Assert.Equal(StartupGreetingPhase.Running, ambient.StartupGreeting);
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
            Assert.Same(startupReply, GetLastReply(window));
            Assert.Equal(
                PetActionState.Greeting,
                window.CaptureAmbientRuntime().ActionState);
            Assert.True(badge.HasAnimatedProperties);
            Assert.True(badgeOffset.HasAnimatedProperties);
            Assert.True(Assert.IsType<RotateTransform>(window.FindName("ActionRotation"))
                .HasAnimatedProperties);

            window.ProcessAmbientSchedule();

            var ambient = window.CaptureAmbientRuntime();
            Assert.Equal(PetActionState.Greeting, ambient.ActionState);
            Assert.False(ambient.IsScheduled);
            Assert.Equal(StartupGreetingPhase.Pending, ambient.StartupGreeting);
            Assert.Equal(
                PetAmbientAction.Greeting,
                ambient.PendingAction);

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
            greeting.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            pause.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            var ambient = window.CaptureAmbientRuntime();
            Assert.False(ambient.IsScheduled);
            Assert.Equal(PetActionState.Paused, ambient.ActionState);
            AssertNeutralAmbientVisuals(window);

            say.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(Assert.IsType<TextBlock>(window.FindName("HeartOne"))
                .HasAnimatedProperties);

            pause.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            ambient = window.CaptureAmbientRuntime();
            Assert.Equal(PetActionState.Idle, ambient.ActionState);
            Assert.True(ambient.IsScheduled);
            Assert.Equal(TimeSpan.FromMilliseconds(650), ambient.ScheduledDelay);
            Assert.Equal(
                PetAmbientAction.Greeting,
                ambient.PendingAction);

            window.Close();
            Assert.False(window.CaptureAmbientRuntime().IsScheduled);
        });

        Assert.True(SpinWait.SpinUntil(
            () => !HasPendingSettingsWrite(settingsDirectory),
            TimeSpan.FromSeconds(5)));
        DeleteSettingsDirectory(settingsDirectory);
    }

    [Fact]
    public void MainWindow_DragAndLandingTakePriorityAndRestoreThePriorPauseState()
    {
        var settingsDirectory = CreateSettingsDirectory();
        RunOnStaThread(() =>
        {
            var animations = new ControlledAnimationController();
            var window = CreateWindowWithScheduler(
                settingsDirectory,
                new AmbientActionScheduler(() => 0.5),
                animationController: animations);
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            Assert.IsType<MenuItem>(window.FindName("GreetingMenuItem"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            window.BeginDragAction();
            var ambient = window.CaptureAmbientRuntime();
            Assert.False(ambient.IsScheduled);
            Assert.Equal(PetActionState.Dragging, ambient.ActionState);
            AssertNeutralAmbientVisuals(window);

            window.BeginLandingAction();
            Assert.Equal(PetActionState.Landing, window.CaptureAmbientRuntime().ActionState);
            animations.CompleteAmbientAction();
            ambient = window.CaptureAmbientRuntime();
            Assert.Equal(PetActionState.Idle, ambient.ActionState);
            Assert.True(ambient.IsScheduled);
            Assert.Equal(TimeSpan.FromMilliseconds(650), ambient.ScheduledDelay);
            Assert.Equal(PetAmbientAction.Greeting, ambient.PendingAction);

            Assert.IsType<MenuItem>(window.FindName("PauseMenuItem"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            window.BeginDragAction();
            window.BeginLandingAction();
            animations.CompleteAmbientAction();

            ambient = window.CaptureAmbientRuntime();
            Assert.Equal(PetActionState.Paused, ambient.ActionState);
            Assert.False(ambient.IsScheduled);
            AssertNeutralAmbientVisuals(window);

            window.Close();
        });

        Assert.True(SpinWait.SpinUntil(
            () => !HasPendingSettingsWrite(settingsDirectory),
            TimeSpan.FromSeconds(5)));
        DeleteSettingsDirectory(settingsDirectory);
    }

    [Fact]
    public void MainWindow_TrayHideDuringLandingResumesAndCompletesTheActionLifecycle()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var animations = new ControlledAnimationController();
            var window = CreateWindowWithScheduler(
                settingsDirectory,
                new AmbientActionScheduler(() => 0.5),
                animationController: animations);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.SetTrayAvailability(true);

                window.BeginDragAction();
                window.BeginLandingAction();
                Assert.Equal(PetActionState.Landing, window.CaptureAmbientRuntime().ActionState);

                window.HideToTray();
                Assert.Equal(PetActionState.Landing, window.CaptureAmbientRuntime().ActionState);

                window.ToggleVisibilityFromTray();
                animations.CompleteAmbientAction();

                var ambient = window.CaptureAmbientRuntime();
                Assert.Equal(PetActionState.Idle, ambient.ActionState);
                Assert.True(ambient.IsScheduled);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
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
            var replyBeforeClose = GetLastReply(window);

            window.BeginDragAction();
            Assert.Equal(PetActionState.Dragging, window.CaptureRuntimeState().ActionState);
            window.Close();
            Assert.False(window.CaptureRuntimeState().IsAmbientTimerEnabled);
            Assert.False(window.CaptureRuntimeState().IsAutomaticTimerEnabled);

            window.BeginLandingAction();

            Assert.Equal(PetActionState.Dragging, window.CaptureRuntimeState().ActionState);
            AssertNeutralAmbientVisuals(window);

            await window.CompleteDragAfterMoveAsync();

            Assert.Same(replyBeforeClose, GetLastReply(window));
            Assert.False(window.CaptureRuntimeState().IsAmbientTimerEnabled);
            Assert.False(window.CaptureRuntimeState().IsAutomaticTimerEnabled);
            AssertNeutralAmbientVisuals(window);
            Assert.False(File.Exists(Path.Combine(settingsDirectory, "settings.json")));
            Assert.False(HasPendingSettingsWrite(settingsDirectory));
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
            window.Close();
            var stateAfterClose = window.CaptureAmbientRuntime();
            window.ProcessAmbientSchedule();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            Assert.Equal(stateAfterClose, window.CaptureAmbientRuntime());
            Assert.False(stateAfterClose.IsScheduled);
            AssertNeutralAmbientVisuals(window);
            DeleteSettingsDirectory(settingsDirectory);
        });
    }

    [Fact]
    [Trait("Category", "Performance")]
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
            var ambient = window.CaptureAmbientRuntime();
            Assert.True(ambient.IsScheduled);
            Assert.Equal(TimeSpan.FromMilliseconds(650), ambient.ScheduledDelay);
            Assert.Equal(StartupGreetingPhase.Scheduled, ambient.StartupGreeting);
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
            var normalWindow = CreateWindow(
                settingsDirectory,
                suppressApplicationShutdownOnClose: false,
                shutdownApplication: () => shutdownRequests++);
            normalWindow.Show();
            normalWindow.Close();

            var suppressedWindow = CreateWindow(
                settingsDirectory,
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
    public async Task MainWindow_SmokeReadinessRejectsFallbackUntilRealStartupIsRendered()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            using var factory = new ControlledDialogueFactory("真实启动回复");
            var dialogue = DialogueService.CreateDeferred(factory.Create);
            var window = CreateWindowWithDialogue(settingsDirectory, dialogue, TimeProvider.System);

            Assert.False(window.TryVerifySmokeReadiness(out var beforeRenderFailure));
            Assert.False(string.IsNullOrWhiteSpace(beforeRenderFailure));

            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.True(factory.Entered.Wait(TimeSpan.FromSeconds(2)));
            Assert.StartsWith("fallback:", GetLastReply(window).SceneId, StringComparison.Ordinal);
            Assert.False(window.TryVerifySmokeReadiness(out var fallbackFailure));
            Assert.Contains("full", fallbackFailure, StringComparison.OrdinalIgnoreCase);

            factory.Release();
            Assert.True(await window.PrepareSmokeReadinessAsync(TimeSpan.FromSeconds(2)));

            Assert.True(window.TryVerifySmokeReadiness(out var renderedFailure), renderedFailure);
            Assert.True(dialogue.IsReady);
            Assert.Equal("full:test", GetLastReply(window).SceneId);
            Assert.NotEqual("builtin_fallback", GetLastReply(window).SourceLine!.SourceKind);
            window.Close();
            DeleteSettingsDirectory(settingsDirectory);
        });
    }

    [Fact]
    public async Task MainWindow_SmokeReadinessDeterministicFailureIsVisibleAndNeverAutoRetries()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var factoryCalls = 0;
            var dialogue = DialogueService.CreateDeferred(_ =>
            {
                factoryCalls++;
                throw new InvalidDataException("invalid corpus");
            });
            var window = CreateWindowWithDialogue(settingsDirectory, dialogue, TimeProvider.System);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                Assert.False(await window.PrepareSmokeReadinessAsync(TimeSpan.FromSeconds(1)));
                WaitForCondition(
                    () => Assert.IsType<TextBlock>(window.FindName("SpeechText")).Text
                        == "文库没醒，点我重试",
                    TimeSpan.FromSeconds(2),
                    () => "The deterministic warmup failure was not presented.");
                Assert.Equal(
                    "文库没醒，点我重试",
                    Assert.IsType<TextBlock>(window.FindName("SpeechText")).Text);
                Assert.Equal(
                    "重试文库 ♡",
                    Assert.IsType<MenuItem>(window.FindName("SayMenuItem")).Header);
                Assert.False(window.TryVerifySmokeReadiness(out var failure));
                Assert.Contains("failed", failure, StringComparison.OrdinalIgnoreCase);
                Assert.False(dialogue.IsReady);

                window.ProcessAutomaticTimerTick();
                window.ProcessAutomaticTimerTick();
                Assert.False(await window.PrepareSmokeReadinessAsync(TimeSpan.FromSeconds(1)));
                Assert.Equal(1, factoryCalls);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public async Task MainWindow_SmokeReadinessRejectsReadyAgentWithFallbackProvenance()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var agent = new FixedDialogueAgent(
                "伪装的全量回复",
                sceneId: "fallback:forged",
                sourceKind: "builtin_fallback");
            var dialogue = DialogueService.CreateDeferred(_ => agent);
            var window = CreateWindowWithDialogue(settingsDirectory, dialogue, TimeProvider.System);
            try
            {
                window.Show();

                Assert.False(await window.PrepareSmokeReadinessAsync(TimeSpan.FromSeconds(1)));
                Assert.True(dialogue.IsReady);
                Assert.False(window.TryVerifySmokeReadiness(out var failure));
                Assert.Contains("full", failure, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task MainWindow_SmokeReadinessTimesOutWithoutCancellingBlockedSingleFlightWarmup()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            using var factory = new ControlledDialogueFactory("迟到的回复");
            var dialogue = DialogueService.CreateDeferred(factory.Create);
            var window = CreateWindowWithDialogue(settingsDirectory, dialogue, TimeProvider.System);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(factory.Entered.Wait(TimeSpan.FromSeconds(2)));
                var stopwatch = Stopwatch.StartNew();

                var ready = await window.PrepareSmokeReadinessAsync(TimeSpan.FromMilliseconds(50));

                stopwatch.Stop();
                Assert.False(ready);
                Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(25), TimeSpan.FromSeconds(1));
                Assert.False(window.TryVerifySmokeReadiness(out _));
                Assert.Equal(1, factory.CallCount);
            }
            finally
            {
                window.Close();
                factory.Release();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task MainWindow_BlockedDialogueWarmupDoesNotBlockLoadedOrClickActions()
    {
        var settingsDirectory = CreateSettingsDirectory();
        var factory = new ControlledDialogueFactory("全量回复");
        var dialogue = DialogueService.CreateDeferred(factory.Create);
        var scenario = RunOnStaThreadAsync(() =>
        {
            var window = CreateWindowWithDialogue(
                settingsDirectory,
                dialogue,
                TimeProvider.System,
                _ => Task.CompletedTask);
            try
            {
                var showStopwatch = Stopwatch.StartNew();
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                showStopwatch.Stop();

                Assert.True(factory.Entered.Wait(TimeSpan.FromSeconds(2)));
                Assert.False(dialogue.IsReady);
                Assert.InRange(showStopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(1));
                Assert.StartsWith(
                    "fallback:",
                    GetLastReply(window).SceneId,
                    StringComparison.Ordinal);

                window.SaySomething();

                Assert.False(dialogue.IsReady);
                Assert.Equal(CompanionEvent.Click, GetLastReply(window).Trigger);
                Assert.StartsWith(
                    "fallback:",
                    GetLastReply(window).SceneId,
                    StringComparison.Ordinal);
                Assert.True(Assert.IsType<TextBlock>(window.FindName("HeartOne"))
                    .HasAnimatedProperties);
                Assert.True(Assert.IsType<RotateTransform>(window.FindName("ReactionRotation"))
                    .HasAnimatedProperties);
                Assert.IsType<System.Windows.Controls.Image>(window.FindName("BlinkOverlay"));
                Assert.IsType<MenuItem>(window.FindName("GreetingMenuItem"));
                Assert.False(window.CaptureRuntimeState().IsMemoryTimerEnabled);
                Assert.Equal(1, factory.CallCount);
                return Task.CompletedTask;
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
        _ = scenario.ContinueWith(
            _ => factory.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        try
        {
            await scenario.WaitAsync(BlockingCallTimeout);
        }
        catch (TimeoutException)
        {
            factory.Release();
            throw;
        }
        finally
        {
            factory.Release();
        }
    }

    [Fact]
    public async Task MainWindow_WarmupCompletionUsesDispatcherAndInjectedClockForFullStartup()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var clock = new ManualTimeProvider();
            clock.SetUtcNow(new DateTimeOffset(2026, 7, 24, 1, 45, 0, TimeSpan.Zero));
            using var factory = new ControlledDialogueFactory("全量文库准备好了。");
            var dialogue = DialogueService.CreateDeferred(factory.Create, clock);
            var window = CreateWindowWithDialogue(settingsDirectory, dialogue, clock);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(factory.Entered.Wait(TimeSpan.FromSeconds(2)));
                Assert.StartsWith(
                    "fallback:",
                    GetLastReply(window).SceneId,
                    StringComparison.Ordinal);

                factory.Release();
                Assert.True(await dialogue.WarmupAsync());
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

                Assert.Equal("full:test", GetLastReply(window).SceneId);
                Assert.Equal("全量文库准备好了。", GetLastReply(window).Text);
                Assert.Equal(
                    clock.GetLocalNow().LocalDateTime,
                    factory.Agent.LastRespondedAt);
                Assert.Equal(1, factory.CallCount);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_CaptureRuntimeState_DoesNotReadDialogueSnapshot()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0));
            var agent = new RecordingDialogueAgent();
            var window = CreateWindowWithCadence(
                settingsDirectory,
                time,
                new SequenceFullscreenDetector(false),
                agent);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.Equal(0, agent.CreateSnapshotCallCount);

                var runtime = window.CaptureRuntimeState();

                Assert.True(runtime.IsEventTimerEnabled);
                Assert.Equal(0, agent.CreateSnapshotCallCount);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public async Task MainWindow_HiddenReadyWarmupDefersReplayUntilRestore()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0));
            var detector = new SequenceFullscreenDetector(false, true);
            var schedulerRandom = new EndpointRandom();
            using var factory = new ControlledDialogueFactory("hidden warmup reply");
            var dialogue = DialogueService.CreateDeferred(factory.Create, time);
            var coordinator = new DialogueWarmupCoordinator(dialogue, time);
            var window = new MainWindow(new MainWindowOptions(
                PetSettings.Default,
                new SettingsService(settingsDirectory))
            {
                SuppressApplicationShutdownOnClose = true,
                AmbientScheduler = new AmbientActionScheduler(() => 0.5),
                AutoStartService = DisabledAutoStartService.Instance,
                DialogueService = dialogue,
                TimeProvider = time,
                WarmupCoordinator = coordinator,
                ForegroundFullscreenDetector = detector,
                DialogueScheduler = new DialogueScheduler(schedulerRandom)
            });
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(factory.Entered.Wait(TimeSpan.FromSeconds(2)));
                var replyBeforeHide = GetLastReply(window);
                var warmup = coordinator.StartAsync(CancellationToken.None);
                window.SetTrayAvailability(true);
                window.HideToTray();
                Assert.False(window.CaptureAutomaticDialogueRuntime().IsScheduled);

                factory.Release();
                Assert.Equal(DialogueWarmupOutcome.Ready, await warmup);
                await window.Dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.ApplicationIdle);

                Assert.Equal(1, detector.ObserveCount);
                Assert.Null(factory.Agent.LastRespondedAt);
                Assert.Same(replyBeforeHide, GetLastReply(window));
                Assert.False(window.CaptureAutomaticDialogueRuntime().IsScheduled);
                Assert.Equal(1, schedulerRandom.NextCount);

                window.ToggleVisibilityFromTray();

                var say = Assert.IsType<MenuItem>(window.FindName("SayMenuItem"));
                Assert.True(window.IsVisible);
                Assert.Equal(2, detector.ObserveCount);
                Assert.NotNull(factory.Agent.LastRespondedAt);
                Assert.NotSame(replyBeforeHide, GetLastReply(window));
                Assert.Equal(CompanionEvent.Startup, GetLastReply(window).Trigger);
                Assert.Equal("full:test", GetLastReply(window).SceneId);
                Assert.True(say.IsEnabled);
                Assert.Equal("说句话 ♡", say.Header);
                Assert.True(window.CaptureAutomaticDialogueRuntime().IsScheduled);
                Assert.Equal(2, schedulerRandom.NextCount);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public async Task MainWindow_HiddenPermanentWarmupFailureBecomesRetryableOnRestore()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0));
            var detector = new SequenceFullscreenDetector(false, true);
            var schedulerRandom = new EndpointRandom();
            var factoryCalls = 0;
            using var factoryEntered = new ManualResetEventSlim();
            using var releaseFactory = new ManualResetEventSlim();
            var dialogue = DialogueService.CreateDeferred(_ =>
            {
                Interlocked.Increment(ref factoryCalls);
                factoryEntered.Set();
                releaseFactory.Wait();
                throw new InvalidDataException("permanent hidden warmup failure");
            }, time);
            var coordinator = new DialogueWarmupCoordinator(dialogue, time);
            var window = new MainWindow(new MainWindowOptions(
                PetSettings.Default,
                new SettingsService(settingsDirectory))
            {
                SuppressApplicationShutdownOnClose = true,
                AmbientScheduler = new AmbientActionScheduler(() => 0.5),
                AutoStartService = DisabledAutoStartService.Instance,
                DialogueService = dialogue,
                TimeProvider = time,
                WarmupCoordinator = coordinator,
                ForegroundFullscreenDetector = detector,
                DialogueScheduler = new DialogueScheduler(schedulerRandom)
            });
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(factoryEntered.Wait(TimeSpan.FromSeconds(2)));
                var say = Assert.IsType<MenuItem>(window.FindName("SayMenuItem"));
                var headerBeforeHide = say.Header;
                var replyBeforeHide = GetLastReply(window);
                var warmup = coordinator.StartAsync(CancellationToken.None);
                window.SetTrayAvailability(true);
                window.HideToTray();

                releaseFactory.Set();
                Assert.Equal(DialogueWarmupOutcome.PermanentFailure, await warmup);

                Assert.Equal(1, detector.ObserveCount);
                Assert.Equal(1, factoryCalls);
                Assert.Same(replyBeforeHide, GetLastReply(window));
                Assert.Equal(headerBeforeHide, say.Header);
                Assert.False(window.CaptureAutomaticDialogueRuntime().IsScheduled);
                Assert.Equal(1, schedulerRandom.NextCount);

                window.ToggleVisibilityFromTray();

                Assert.True(window.IsVisible);
                Assert.Equal(2, detector.ObserveCount);
                Assert.Equal(1, factoryCalls);
                Assert.Same(replyBeforeHide, GetLastReply(window));
                Assert.NotEqual(headerBeforeHide, say.Header);
                Assert.True(say.IsEnabled);
                Assert.True(coordinator.CanRetryAfterFailure);
                Assert.True(window.CaptureAutomaticDialogueRuntime().IsScheduled);
                Assert.Equal(2, schedulerRandom.NextCount);
            }
            finally
            {
                releaseFactory.Set();
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_ClickBetweenLoadedAndContentRenderedIsNotOverwrittenByWarmStartup()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            using var factory = new ControlledDialogueFactory("不该覆盖点击");
            var dialogue = DialogueService.CreateDeferred(factory.Create);
            var window = CreateWindowWithDialogue(settingsDirectory, dialogue, TimeProvider.System);
            try
            {
                window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                var startupRevision = window.CaptureRuntimeState().DialogueReplyRevision;
                Assert.StartsWith("fallback:", GetLastReply(window).SceneId, StringComparison.Ordinal);

                window.SaySomething();
                var click = GetLastReply(window);
                Assert.Equal(CompanionEvent.Click, click.Trigger);
                Assert.True(window.CaptureRuntimeState().DialogueReplyRevision > startupRevision);
                window.ProcessPresentationRendered();
                Assert.True(factory.Entered.Wait(TimeSpan.FromSeconds(2)));

                factory.Release();
                WaitForCondition(
                    () => dialogue.IsReady,
                    TimeSpan.FromSeconds(2),
                    () => "Dialogue warmup did not complete.");
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                Assert.Same(click, GetLastReply(window));
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_TransientWarmupFailureRetriesAndReplaysStartupOnce()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var factoryCalls = 0;
            var delays = new List<TimeSpan>();
            var fixedAgent = new FixedDialogueAgent("重试后真实启动");
            var dialogue = DialogueService.CreateDeferred(_ =>
            {
                if (Interlocked.Increment(ref factoryCalls) == 1)
                {
                    throw new TimeoutException("temporary");
                }

                return fixedAgent;
            });
            var coordinator = new DialogueWarmupCoordinator(
                dialogue,
                delayAsync: (delay, _) =>
                {
                    delays.Add(delay);
                    return Task.CompletedTask;
                });
            var window = CreateWindowWithDialogue(
                settingsDirectory,
                dialogue,
                TimeProvider.System,
                warmupCoordinator: coordinator);
            try
            {
                window.Show();
                WaitForCondition(
                    () => GetLastReply(window).SceneId == "full:test",
                    TimeSpan.FromSeconds(2),
                    () => "Recovered warmup did not replay the real startup reply.");

                Assert.Equal(2, factoryCalls);
                Assert.Equal([TimeSpan.FromSeconds(1)], delays);
                Assert.Equal(CompanionEvent.Startup, GetLastReply(window).Trigger);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_ClickAfterRetriesExhaustedStartsNewRunAndRendersRealReply()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var factoryCalls = 0;
            var delays = new List<TimeSpan>();
            var fixedAgent = new FixedDialogueAgent("重试后文库真的醒了");
            var dialogue = DialogueService.CreateDeferred(_ =>
            {
                if (Interlocked.Increment(ref factoryCalls) <= 4)
                {
                    throw new IOException("temporary corpus access");
                }

                return fixedAgent;
            });
            var coordinator = new DialogueWarmupCoordinator(
                dialogue,
                delayAsync: (delay, _) =>
                {
                    delays.Add(delay);
                    return Task.CompletedTask;
                });
            var window = CreateWindowWithDialogue(
                settingsDirectory,
                dialogue,
                TimeProvider.System,
                warmupCoordinator: coordinator);
            try
            {
                window.Show();
                WaitForCondition(
                    () => Assert.IsType<TextBlock>(window.FindName("SpeechText")).Text
                        == "文库没醒，点我重试",
                    TimeSpan.FromSeconds(2),
                    () => "Retries-exhausted state was not visible.");
                Assert.Equal(4, factoryCalls);
                Assert.Equal(
                    [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)],
                    delays);

                window.SaySomething();
                WaitForCondition(
                    () => GetLastReply(window).SceneId == "full:test",
                    TimeSpan.FromSeconds(2),
                    () => "The explicit retry did not render a real corpus reply.");

                Assert.Equal(5, factoryCalls);
                Assert.Equal(CompanionEvent.Click, GetLastReply(window).Trigger);
                Assert.Equal("重试后文库真的醒了", GetLastReply(window).Text);
                Assert.NotEqual("builtin_fallback", GetLastReply(window).SourceLine!.SourceKind);
                Assert.Equal(
                    "说句话 ♡",
                    Assert.IsType<MenuItem>(window.FindName("SayMenuItem")).Header);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public async Task MainWindow_CloseInvalidatesPendingWarmupContinuation()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            using var factory = new ControlledDialogueFactory("关闭后不许播");
            var dialogue = DialogueService.CreateDeferred(factory.Create);
            var window = CreateWindowWithDialogue(settingsDirectory, dialogue, TimeProvider.System);
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.True(factory.Entered.Wait(TimeSpan.FromSeconds(2)));
            var replyBeforeClose = GetLastReply(window);
            var speechBeforeClose = Assert.IsType<TextBlock>(window.FindName("SpeechText")).Text;

            window.Close();
            factory.Release();
            Assert.True(await dialogue.WarmupAsync());
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

            Assert.Same(replyBeforeClose, GetLastReply(window));
            Assert.Equal(
                speechBeforeClose,
                Assert.IsType<TextBlock>(window.FindName("SpeechText")).Text);
            Assert.False(window.CaptureRuntimeState().IsAutomaticTimerEnabled);
            Assert.False(window.CaptureRuntimeState().IsEventTimerEnabled);
            DeleteSettingsDirectory(settingsDirectory);
        });
    }

    [Fact]
    public async Task MainWindow_CloseCancelsPendingWarmupBackoffBeforeAnotherAttempt()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var delayEntered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var factoryCalls = 0;
            var dialogue = DialogueService.CreateDeferred(_ =>
            {
                Interlocked.Increment(ref factoryCalls);
                throw new TimeoutException("temporary");
            });
            var coordinator = new DialogueWarmupCoordinator(
                dialogue,
                delayAsync: async (_, cancellationToken) =>
                {
                    delayEntered.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                });
            var window = CreateWindowWithDialogue(
                settingsDirectory,
                dialogue,
                TimeProvider.System,
                warmupCoordinator: coordinator);
            window.Show();
            await delayEntered.Task;
            var run = coordinator.StartAsync(CancellationToken.None);

            window.Close();

            Assert.Equal(DialogueWarmupOutcome.Cancelled, await run);
            Assert.Equal(1, factoryCalls);
            DeleteSettingsDirectory(settingsDirectory);
        });
    }

    [Fact]
    public void MainWindow_StartupReply_DoesNotDriveActionAnimationsOrHearts()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var window = CreateWindow(settingsDirectory);
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
    public void MainWindow_DependencyBoundaryAcceptsTestOverridesWithoutSignatureReflection()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var idleTimeProvider = new FixedIdleTimeProvider(TimeSpan.FromMinutes(7));
            var autoStartService = new FakeAutoStartService { Enabled = true };
            var dependencies = new MainWindowOptions(
                PetSettings.Default,
                new SettingsService(settingsDirectory))
            {
                IdleTimeProvider = idleTimeProvider,
                AmbientScheduler = new AmbientActionScheduler(() => 0.5),
                AutoStartService = autoStartService,
                SuppressApplicationShutdownOnClose = true
            };
            MainWindow? window = null;
            ContextMenu? menu = null;
            try
            {
                window = new MainWindow(dependencies);
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                menu = Assert.IsType<ContextMenu>(window.FindName("ControlMenu"));
                menu.IsOpen = true;

                Assert.True(Assert.IsType<MenuItem>(window.FindName("AutoStartMenuItem")).IsChecked);
                Assert.Equal(TimeSpan.FromMinutes(7), idleTimeProvider.LastReturnedValue);
            }
            finally
            {
                try
                {
                    if (menu is not null)
                    {
                        menu.IsOpen = false;
                    }
                }
                finally
                {
                    try
                    {
                        window?.Close();
                    }
                    finally
                    {
                        DeleteSettingsDirectory(settingsDirectory);
                    }
                }
            }
        });
    }

    [Fact]
    public void MainWindow_OptionsAcceptANullAgentMemoryService()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            MainWindow? window = null;
            try
            {
                window = new MainWindow(new MainWindowOptions(
                    PetSettings.Default,
                    new SettingsService(settingsDirectory))
                {
                    AgentMemoryService = null,
                    SuppressApplicationShutdownOnClose = true
                });

                Assert.NotNull(window);
            }
            finally
            {
                try
                {
                    window?.Close();
                }
                finally
                {
                    DeleteSettingsDirectory(settingsDirectory);
                }
            }
        });
    }

    [Fact]
    public void MainWindow_SystemCommands_RefreshAndApplyAutoStartFromTheControlMenu()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var autoStart = new FakeAutoStartService { Enabled = true };
            var window = CreateWindowWithAutoStart(settingsDirectory, autoStart);
            try
            {
                var menu = Assert.IsType<ContextMenu>(window.FindName("ControlMenu"));
                var autoStartItem = Assert.IsType<MenuItem>(window.FindName("AutoStartMenuItem"));
                Assert.IsType<MenuItem>(window.FindName("HideToTrayMenuItem"));

                menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));

                Assert.Equal(1, autoStart.ReadCount);
                Assert.True(autoStartItem.IsEnabled);
                Assert.True(autoStartItem.IsChecked);
                Assert.Null(autoStartItem.ToolTip);

                autoStartItem.IsChecked = false;
                autoStartItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                Assert.Equal([false], autoStart.WriteRequests);
                Assert.False(autoStartItem.IsChecked);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_SystemCommands_AutoStartFailureRollsBackWithoutChangingConversation()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var autoStart = new FakeAutoStartService
            {
                Enabled = true,
                SetSucceeds = false
            };
            var window = CreateWindowWithAutoStart(settingsDirectory, autoStart);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var menu = Assert.IsType<ContextMenu>(window.FindName("ControlMenu"));
                var autoStartItem = Assert.IsType<MenuItem>(window.FindName("AutoStartMenuItem"));
                var speech = Assert.IsType<TextBlock>(window.FindName("SpeechText"));
                var replyBeforeFailure = GetLastReply(window);
                menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));

                autoStartItem.IsChecked = false;
                autoStartItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                Assert.Equal([false], autoStart.WriteRequests);
                Assert.True(autoStartItem.IsChecked);
                Assert.True(window.IsVisible);
                Assert.Same(replyBeforeFailure, GetLastReply(window));
                Assert.Equal("开机启动没设置上，Windows 不让改。", speech.Text);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_SystemCommands_NearDeadlineAutoStartWriteFailureRearmsFromVisibleFeedback()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0));
            var detector = new SequenceFullscreenDetector(false, false);
            var agent = new RecordingDialogueAgent();
            var schedulerRandom = new EndpointRandom();
            var autoStart = new FakeAutoStartService
            {
                Enabled = true,
                SetSucceeds = false
            };
            var window = CreateWindowWithCadence(
                settingsDirectory,
                time,
                detector,
                agent,
                schedulerRandom,
                autoStart);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var menu = Assert.IsType<ContextMenu>(window.FindName("ControlMenu"));
                var autoStartItem = Assert.IsType<MenuItem>(window.FindName("AutoStartMenuItem"));
                var bubble = Assert.IsType<StackPanel>(window.FindName("SpeechBubble"));
                var replyBeforeFailure = GetLastReply(window);
                menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
                time.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
                time.SetLocalNow(new DateTime(2026, 7, 26, 10, 4, 59));
                var timeReadsBeforeFailure = time.UtcNowReadCount;

                autoStartItem.IsChecked = false;
                autoStartItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                var runtime = window.CaptureAutomaticDialogueRuntime();
                Assert.Equal([false], autoStart.WriteRequests);
                Assert.True(autoStartItem.IsChecked);
                Assert.Equal(Visibility.Visible, bubble.Visibility);
                Assert.Equal(timeReadsBeforeFailure + 1, time.UtcNowReadCount);
                Assert.Equal(2, detector.ObserveCount);
                Assert.Equal(2, schedulerRandom.NextCount);
                Assert.Single(agent.Calls);
                Assert.Same(replyBeforeFailure, GetLastReply(window));
                Assert.True(runtime.IsScheduled);
                Assert.Equal(
                    (TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59)).Ticks,
                    runtime.ArmedAtTimestamp);
                Assert.Equal(TimeSpan.FromMinutes(5), runtime.ScheduledDelay);
                Assert.Equal(new FullscreenSnapshot(false, false), runtime.Fullscreen);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindowOptionsBuilder_CreatesTheSingleCompositionRootValue()
    {
        var settings = PetSettings.Default;
        var settingsService = new SettingsService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var autoStartService = new FakeAutoStartService { Enabled = true };

        var options = new MainWindowOptionsBuilder(settings, settingsService)
        {
            AutoStartService = autoStartService,
            SuppressApplicationShutdownOnClose = true
        }.Build();

        Assert.Same(settings, options.Settings);
        Assert.Same(settingsService, options.SettingsService);
        Assert.Same(autoStartService, options.AutoStartService);
        Assert.True(options.SuppressApplicationShutdownOnClose);
    }

    [Fact]
    public void MainWindow_SystemCommands_TrayAutoStartUsesTheSuccessfulReadAsRollbackState()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var autoStart = new FakeAutoStartService
            {
                Enabled = true,
                SetSucceeds = false
            };
            var window = CreateWindowWithAutoStart(settingsDirectory, autoStart);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var autoStartItem = Assert.IsType<MenuItem>(window.FindName("AutoStartMenuItem"));
                var replyBeforeFailure = GetLastReply(window);

                window.ToggleAutoStartFromTray();

                Assert.Equal(1, autoStart.ReadCount);
                Assert.Equal([false], autoStart.WriteRequests);
                Assert.True(autoStartItem.IsChecked);
                Assert.Same(replyBeforeFailure, GetLastReply(window));
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_SystemCommands_TrayAutoStartReadFailureDoesNotGuessOrWrite()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var autoStart = new FakeAutoStartService { ReadSucceeds = false };
            var window = CreateWindowWithAutoStart(settingsDirectory, autoStart);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var speech = Assert.IsType<TextBlock>(window.FindName("SpeechText"));
                var replyBeforeFailure = GetLastReply(window);

                window.ToggleAutoStartFromTray();

                Assert.Equal(1, autoStart.ReadCount);
                Assert.Empty(autoStart.WriteRequests);
                Assert.Same(replyBeforeFailure, GetLastReply(window));
                Assert.Equal("Windows 暂时不允许读取开机启动设置。", speech.Text);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_SystemCommands_NearDeadlineTrayAutoStartReadFailureRearmsFromVisibleFeedback()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0));
            var detector = new SequenceFullscreenDetector(false, false);
            var agent = new RecordingDialogueAgent();
            var schedulerRandom = new EndpointRandom();
            var autoStart = new FakeAutoStartService { ReadSucceeds = false };
            var window = CreateWindowWithCadence(
                settingsDirectory,
                time,
                detector,
                agent,
                schedulerRandom,
                autoStart);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var bubble = Assert.IsType<StackPanel>(window.FindName("SpeechBubble"));
                var replyBeforeFailure = GetLastReply(window);
                time.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
                time.SetLocalNow(new DateTime(2026, 7, 26, 10, 4, 59));
                var timeReadsBeforeFailure = time.UtcNowReadCount;

                window.ToggleAutoStartFromTray();

                var runtime = window.CaptureAutomaticDialogueRuntime();
                Assert.Equal(1, autoStart.ReadCount);
                Assert.Empty(autoStart.WriteRequests);
                Assert.Equal(Visibility.Visible, bubble.Visibility);
                Assert.Equal(timeReadsBeforeFailure + 1, time.UtcNowReadCount);
                Assert.Equal(2, detector.ObserveCount);
                Assert.Equal(2, schedulerRandom.NextCount);
                Assert.Single(agent.Calls);
                Assert.Same(replyBeforeFailure, GetLastReply(window));
                Assert.True(runtime.IsScheduled);
                Assert.Equal(
                    (TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59)).Ticks,
                    runtime.ArmedAtTimestamp);
                Assert.Equal(TimeSpan.FromMinutes(5), runtime.ScheduledDelay);
                Assert.Equal(new FullscreenSnapshot(false, false), runtime.Fullscreen);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_SystemCommands_DisabledAutoStartStillShowsItsTooltip()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var autoStart = new FakeAutoStartService { ReadSucceeds = false };
            var window = CreateWindowWithAutoStart(settingsDirectory, autoStart);
            try
            {
                var menu = Assert.IsType<ContextMenu>(window.FindName("ControlMenu"));
                var autoStartItem = Assert.IsType<MenuItem>(window.FindName("AutoStartMenuItem"));

                menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));

                Assert.False(autoStartItem.IsEnabled);
                Assert.NotNull(autoStartItem.ToolTip);
                Assert.True(ToolTipService.GetShowOnDisabled(autoStartItem));
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_SystemCommands_HideIsGuardedUntilTrayIsAvailable()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var window = CreateWindowWithAutoStart(
                settingsDirectory,
                new FakeAutoStartService());
            try
            {
                var hideItem = Assert.IsType<MenuItem>(window.FindName("HideToTrayMenuItem"));
                Assert.False(hideItem.IsEnabled);
                Assert.NotNull(hideItem.ToolTip);
                Assert.True(ToolTipService.GetShowOnDisabled(hideItem));

                window.Show();
                window.HideToTray();

                Assert.True(window.IsVisible);

                window.SetTrayAvailability(true);
                Assert.True(hideItem.IsEnabled);
                Assert.Null(hideItem.ToolTip);
                window.HideToTray();
                Assert.False(window.IsVisible);

                window.WindowState = WindowState.Minimized;
                window.SetTrayAvailability(false);
                Assert.True(window.IsVisible);
                Assert.Equal(WindowState.Normal, window.WindowState);
                Assert.True(window.IsActive);
                Assert.False(hideItem.IsEnabled);
                Assert.NotNull(hideItem.ToolTip);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_SystemCommands_TrayStatePreservesLastCheckWhenReadIsUnavailable()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var autoStart = new FakeAutoStartService { Enabled = true };
            var window = CreateWindowWithAutoStart(settingsDirectory, autoStart);
            try
            {
                var available = window.GetTrayMenuState();
                Assert.True(available.IsAutoStartAvailable);
                Assert.True(available.IsAutoStartEnabled);

                autoStart.ReadSucceeds = false;
                autoStart.Enabled = false;
                var unavailable = window.GetTrayMenuState();

                Assert.False(unavailable.IsAutoStartAvailable);
                Assert.True(unavailable.IsAutoStartEnabled);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_SystemCommands_HideAndShowPreserveWindowLifetime()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var shutdownRequests = 0;
            var window = CreateWindowWithAutoStart(
                settingsDirectory,
                new FakeAutoStartService(),
                suppressApplicationShutdownOnClose: false,
                shutdownApplication: () => shutdownRequests++);
            var closedCount = 0;
            window.Closed += (_, _) => closedCount++;
            try
            {
                window.Show();
                window.SetTrayAvailability(true);
                window.HideToTray();

                Assert.False(window.IsVisible);
                Assert.Equal(0, closedCount);
                Assert.Equal(0, shutdownRequests);

                window.WindowState = WindowState.Minimized;
                window.ToggleVisibilityFromTray();

                Assert.True(window.IsVisible);
                Assert.Equal(WindowState.Normal, window.WindowState);
                Assert.True(window.IsActive);

                window.ToggleVisibilityFromTray();
                Assert.False(window.IsVisible);
                Assert.Equal(0, closedCount);
                Assert.Equal(0, shutdownRequests);
            }
            finally
            {
                window.Close();
                Assert.Equal(1, closedCount);
                Assert.Equal(1, shutdownRequests);
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public async Task MainWindow_SystemCommands_WpfAndInternalCommandsShareOutcomesAndExitIsIdempotent()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var directSettingsDirectory = CreateSettingsDirectory();
            var directShutdownRequests = 0;
            var directClosedCount = 0;
            var directWindow = CreateWindowWithAutoStart(
                directSettingsDirectory,
                new FakeAutoStartService(),
                suppressApplicationShutdownOnClose: false,
                shutdownApplication: () => directShutdownRequests++);
            directWindow.Closed += (_, _) => directClosedCount++;
            directWindow.Show();
            directWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var say = Assert.IsType<MenuItem>(directWindow.FindName("SayMenuItem"));
            say.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(CompanionEvent.Click, GetLastReply(directWindow).Trigger);
            directWindow.SaySomething();
            Assert.Equal(CompanionEvent.Click, GetLastReply(directWindow).Trigger);

            var pause = Assert.IsType<MenuItem>(directWindow.FindName("PauseMenuItem"));
            pause.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(directWindow.CaptureRuntimeState().IsPaused);
            await directWindow.ToggleAnimationAsync();
            Assert.False(directWindow.CaptureRuntimeState().IsPaused);

            await Task.WhenAll(
                directWindow.RequestExitAsync(),
                directWindow.RequestExitAsync());

            Assert.Equal(1, directClosedCount);
            Assert.Equal(1, directShutdownRequests);

            var wpfSettingsDirectory = CreateSettingsDirectory();
            var wpfShutdownRequests = 0;
            var wpfClosedCount = 0;
            var wpfWindow = CreateWindowWithAutoStart(
                wpfSettingsDirectory,
                new FakeAutoStartService(),
                suppressApplicationShutdownOnClose: false,
                shutdownApplication: () => wpfShutdownRequests++);
            wpfWindow.Closed += (_, _) => wpfClosedCount++;
            wpfWindow.Show();

            Assert.IsType<MenuItem>(wpfWindow.FindName("ExitMenuItem"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            WaitForCondition(
                () => wpfClosedCount == 1,
                TimeSpan.FromSeconds(5),
                () => "The WPF exit command did not close the window.");

            Assert.Equal(1, wpfShutdownRequests);
            DeleteSettingsDirectory(directSettingsDirectory);
            DeleteSettingsDirectory(wpfSettingsDirectory);
        });
    }

    [Fact]
    public async Task MainWindow_SystemCommands_ExitSerializesMemoryAndLeavesNoTrailingSave()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var memoryWriter = new ControlledMemoryWriter();
            var closedCount = 0;
            var dialogue = new DialogueService();
            var window = CreateWindowWithMemoryWriter(
                settingsDirectory,
                memoryWriter.SaveAsync,
                dialogue);
            window.Closed += (_, _) => closedCount++;
            Task? exit = null;
            Task? duplicateExit = null;
            Task? postExitToggle = null;
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var character = Assert.IsType<Grid>(window.FindName("CharacterStage"));
                var bubble = Assert.IsType<StackPanel>(window.FindName("SpeechBubble"));
                var speech = Assert.IsType<TextBlock>(window.FindName("SpeechText"));
                var controlMenu = Assert.IsType<ContextMenu>(window.FindName("ControlMenu"));
                var autoStartItem = Assert.IsType<MenuItem>(window.FindName("AutoStartMenuItem"));
                Assert.True(window.CaptureRuntimeState().IsMemoryTimerEnabled);

                window.MemoryTimer_Tick(null, EventArgs.Empty);
                await memoryWriter.FirstSaveStarted;
                Assert.False(window.CaptureRuntimeState().IsMemoryTimerEnabled);
                Assert.Equal(1, memoryWriter.CallCount);

                window.SaySomething();
                Assert.True(window.CaptureRuntimeState().IsMemoryTimerEnabled);
                var frozenReply = GetLastReply(window);
                var frozenTurnCount = dialogue.CreateSnapshot().TurnCount;
                var frozenPaused = window.CaptureRuntimeState().IsPaused;
                var frozenBubbleVisibility = bubble.Visibility;
                var frozenSpeech = speech.Text;
                autoStartItem.IsChecked = true;
                autoStartItem.IsEnabled = false;
                autoStartItem.ToolTip = "frozen";

                exit = window.RequestExitAsync();
                duplicateExit = window.RequestExitAsync();

                var frozenRuntime = window.CaptureRuntimeState();
                Assert.False(frozenRuntime.IsMemoryTimerEnabled);
                Assert.False(frozenRuntime.IsAutomaticTimerEnabled);
                Assert.False(frozenRuntime.IsEventTimerEnabled);
                Assert.False(frozenRuntime.IsAmbientTimerEnabled);
                Assert.False(frozenRuntime.IsBubbleTimerEnabled);
                Assert.Equal(BubbleCountdownState.Hidden, frozenRuntime.BubbleCountdownState);
                window.ShowBubble("blocked after exit");
                Assert.Equal(
                    BubbleCountdownState.Hidden,
                    window.CaptureRuntimeState().BubbleCountdownState);
                Assert.False(exit.IsCompleted);
                Assert.True(duplicateExit.IsCompletedSuccessfully);

                window.SaySomething();
                postExitToggle = window.ToggleAnimationAsync();
                window.ProcessAutomaticTimerTick();
                window.ProcessEventTimerTick();
                window.ProcessAmbientSchedule();
                window.BubbleHover_MouseEnter(character, null);
                window.BubbleHover_MouseLeave(character, null);
                window.BubbleHover_MouseEnter(bubble, null);
                window.BubbleHover_MouseLeave(bubble, null);
                window.BubbleTimer_Tick(null, EventArgs.Empty);
                window.SynchronizeBubbleTimer();
                controlMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));

                frozenRuntime = window.CaptureRuntimeState();
                Assert.False(frozenRuntime.IsMemoryTimerEnabled);
                Assert.False(frozenRuntime.IsAutomaticTimerEnabled);
                Assert.False(frozenRuntime.IsEventTimerEnabled);
                Assert.False(frozenRuntime.IsAmbientTimerEnabled);
                Assert.False(frozenRuntime.IsBubbleTimerEnabled);
                Assert.Equal(BubbleCountdownState.Hidden, frozenRuntime.BubbleCountdownState);
                Assert.Equal(frozenBubbleVisibility, bubble.Visibility);
                Assert.Equal(frozenSpeech, speech.Text);
                Assert.True(autoStartItem.IsChecked);
                Assert.False(autoStartItem.IsEnabled);
                Assert.Equal("frozen", autoStartItem.ToolTip);
                Assert.Equal(frozenPaused, window.CaptureRuntimeState().IsPaused);
                Assert.Same(frozenReply, GetLastReply(window));
                Assert.Equal(frozenTurnCount, dialogue.CreateSnapshot().TurnCount);

                window.MemoryTimer_Tick(null, EventArgs.Empty);
                Assert.Equal(1, memoryWriter.CallCount);

                memoryWriter.ReleaseFirstSave();
                await Task.WhenAll(exit, duplicateExit);

                Assert.Equal(1, closedCount);
                Assert.Equal(2, memoryWriter.CallCount);
                Assert.Equal(2, memoryWriter.CompletedCount);
                Assert.Equal(1, memoryWriter.MaximumConcurrentWrites);
                Assert.Equal(0, memoryWriter.ActiveWrites);
                Assert.True(
                    memoryWriter.Snapshots[1].TurnCount
                    > memoryWriter.Snapshots[0].TurnCount);
                Assert.Equal(frozenTurnCount, memoryWriter.Snapshots[1].TurnCount);

                window.MemoryTimer_Tick(null, EventArgs.Empty);
                Assert.Equal(2, memoryWriter.CallCount);
                Assert.Equal(0, memoryWriter.ActiveWrites);
            }
            finally
            {
                memoryWriter.ReleaseFirstSave();
                if (exit is not null && duplicateExit is not null)
                {
                    await Task.WhenAll(exit, duplicateExit);
                }

                if (postExitToggle is not null)
                {
                    await postExitToggle;
                }

                if (closedCount == 0)
                {
                    window.Close();
                }

                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public async Task MainWindow_TrayExit_ClosesHiddenWindowWhenSettingsSaveThrows()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var shutdownRequests = 0;
            var closedCount = 0;
            var memorySaveCalls = 0;
            var window = CreateWindowWithPersistenceWriters(
                settingsDirectory,
                _ => Task.FromException(new InvalidOperationException("settings failed")),
                _ =>
                {
                    memorySaveCalls++;
                    return Task.CompletedTask;
                },
                () => shutdownRequests++);
            window.Closed += (_, _) => closedCount++;
            using var sourceIcon = new DrawingIcon(
                Path.Combine(AppContext.BaseDirectory, "Assets", "pet.ico"));
            var tray = new TrayIconService(
                window.Dispatcher,
                sourceIcon,
                window.GetTrayMenuState,
                window.ToggleVisibilityFromTray,
                window.SaySomething,
                window.ToggleAnimationAsync,
                window.ToggleAutoStartFromTray,
                window.RequestExitAsync,
                publishIcon: false);
            try
            {
                window.Show();
                window.SetTrayAvailability(true);
                window.HideToTray();
                Assert.False(window.IsVisible);

                tray.ExitMenuItem.PerformClick();
                WaitForCondition(
                    () => closedCount == 1,
                    TimeSpan.FromSeconds(5),
                    () => "Tray exit did not close after the settings save failed.");
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                Assert.Equal(1, closedCount);
                Assert.Equal(1, shutdownRequests);
                Assert.Equal(1, memorySaveCalls);
            }
            finally
            {
                tray.Dispose();
                tray.Dispose();
                if (closedCount == 0)
                {
                    window.Close();
                }

                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_WpfExit_HandlesUnexpectedMemorySaveFailureAndClosesOnce()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var shutdownRequests = 0;
            var closedCount = 0;
            Exception? dispatcherException = null;
            var window = CreateWindowWithPersistenceWriters(
                settingsDirectory,
                _ => Task.CompletedTask,
                _ => Task.FromException(new InvalidOperationException("memory failed")),
                () => shutdownRequests++);
            window.Closed += (_, _) => closedCount++;
            DispatcherUnhandledExceptionEventHandler handler = (_, e) =>
            {
                dispatcherException = e.Exception;
                e.Handled = true;
            };
            Application.Current.DispatcherUnhandledException += handler;
            try
            {
                window.Show();
                Assert.IsType<MenuItem>(window.FindName("ExitMenuItem"))
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                WaitForCondition(
                    () => closedCount == 1 || dispatcherException is not null,
                    TimeSpan.FromSeconds(5),
                    () => "WPF exit neither closed nor reported its save failure.");

                Assert.Null(dispatcherException);
                Assert.Equal(1, closedCount);
                Assert.Equal(1, shutdownRequests);
            }
            finally
            {
                Application.Current.DispatcherUnhandledException -= handler;
                if (closedCount == 0)
                {
                    window.Close();
                }

                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public async Task MainWindow_FatalExitSaveFailure_StillClosesInFinally()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var closedCount = 0;
            var window = CreateWindowWithPersistenceWriters(
                settingsDirectory,
                _ => Task.FromException(new OutOfMemoryException("fatal save")),
                _ => Task.CompletedTask,
                shutdownApplication: () => { });
            window.Closed += (_, _) => closedCount++;
            window.Show();

            await Assert.ThrowsAsync<OutOfMemoryException>(window.RequestExitAsync);

            Assert.Equal(1, closedCount);
            DeleteSettingsDirectory(settingsDirectory);
        });
    }

    [Fact]
    public void MainWindow_SecondInstanceActivation_AlwaysRestoresAndNeverTogglesAway()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var window = CreateWindow(settingsDirectory);
            try
            {
                window.Show();
                window.SetTrayAvailability(true);
                window.HideToTray();
                Assert.False(window.IsVisible);

                window.RestoreFromSecondInstance();

                Assert.True(window.IsVisible);
                Assert.Equal(WindowState.Normal, window.WindowState);
                Assert.True(window.IsActive);

                window.RestoreFromSecondInstance();
                Assert.True(window.IsVisible);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_EventTimerRunsOnlyWhileTheWindowIsLoaded()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var window = CreateWindow(settingsDirectory);
            Assert.Equal(
                TimeSpan.FromSeconds(30),
                window.CaptureRuntimeState().EventTimerInterval);
            Assert.False(window.CaptureRuntimeState().IsEventTimerEnabled);

            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.True(window.CaptureRuntimeState().IsEventTimerEnabled);

            window.Close();
            Assert.False(window.CaptureRuntimeState().IsEventTimerEnabled);
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, true);
            }
        });
    }

    [Fact]
    public async Task MainWindow_SystemCommands_TrayHiddenPauseAndResumeAvoidDialogueCadenceSideEffects()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0));
            var detector = new SequenceFullscreenDetector(false, false, false);
            var dialogue = new DialogueService();
            var animations = new ControlledAnimationController();
            var savedSettings = new List<PetSettings>();
            var window = new MainWindow(new MainWindowOptions(
                PetSettings.Default,
                new SettingsService(settingsDirectory))
            {
                SuppressApplicationShutdownOnClose = true,
                AmbientScheduler = new AmbientActionScheduler(() => 0.5),
                AutoStartService = DisabledAutoStartService.Instance,
                DialogueService = dialogue,
                TimeProvider = time,
                ForegroundFullscreenDetector = detector,
                DialogueScheduler = new DialogueScheduler(new EndpointRandom()),
                AnimationController = animations,
                SaveSettingsAsync = settings =>
                {
                    savedSettings.Add(settings);
                    return Task.CompletedTask;
                }
            });
            using var sourceIcon = new DrawingIcon(
                Path.Combine(AppContext.BaseDirectory, "Assets", "pet.ico"));
            var tray = new TrayIconService(
                window.Dispatcher,
                sourceIcon,
                window.GetTrayMenuState,
                window.ToggleVisibilityFromTray,
                window.SaySomething,
                window.ToggleAnimationAsync,
                window.ToggleAutoStartFromTray,
                window.RequestExitAsync,
                publishIcon: false);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var replyBeforeHide = GetLastReply(window);
                var memoryBeforeHide = dialogue.CreateSnapshot();
                window.SetTrayAvailability(true);
                window.HideToTray();

                tray.PauseMenuItem.PerformClick();
                WaitForCondition(
                    () => savedSettings.Count == 1,
                    TimeSpan.FromSeconds(5),
                    () => "The hidden tray pause command did not save its setting.");

                Assert.True(window.GetTrayMenuState().IsPaused);
                Assert.True(savedSettings[0].AnimationPaused);
                Assert.Equal(1, animations.PauseIdleCount);

                tray.PauseMenuItem.PerformClick();
                WaitForCondition(
                    () => savedSettings.Count == 2,
                    TimeSpan.FromSeconds(5),
                    () => "The hidden tray resume command did not save its setting.");
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                var memoryAfterResume = dialogue.CreateSnapshot();
                Assert.False(window.GetTrayMenuState().IsPaused);
                Assert.False(savedSettings[1].AnimationPaused);
                Assert.Equal(1, animations.ResumeIdleCount);
                Assert.Equal(1, detector.ObserveCount);
                Assert.Equal(memoryBeforeHide.TurnCount, memoryAfterResume.TurnCount);
                Assert.Equal(memoryBeforeHide.History, memoryAfterResume.History);
                Assert.Equal(memoryBeforeHide.RecentLines, memoryAfterResume.RecentLines);
                Assert.Same(replyBeforeHide, GetLastReply(window));
                Assert.False(window.CaptureAutomaticDialogueRuntime().IsScheduled);
            }
            finally
            {
                tray.Dispose();
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_LoadedObservesOnceAndArmsFromTheStartupSnapshot()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0));
            var detector = new SequenceFullscreenDetector(true);
            var agent = new RecordingDialogueAgent();
            var schedulerRandom = new EndpointRandom();
            var window = CreateWindowWithCadence(
                settingsDirectory,
                time,
                detector,
                agent,
                schedulerRandom);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                var runtime = window.CaptureAutomaticDialogueRuntime();
                var startup = Assert.Single(agent.Calls);
                Assert.Equal(1, detector.ObserveCount);
                Assert.Equal(1, schedulerRandom.NextCount);
                Assert.NotEqual(nint.Zero, Assert.Single(detector.ExcludedWindows));
                Assert.Equal(CompanionEvent.Startup, startup.Trigger);
                Assert.Equal(new DateTime(2026, 7, 26, 10, 0, 0), startup.LocalTime);
                Assert.Equal(new FullscreenSnapshot(true, true), startup.Fullscreen);
                Assert.True(runtime.IsScheduled);
                Assert.Equal(TimeSpan.FromMinutes(60), runtime.ScheduledDelay);
                Assert.Equal(AutomaticCadenceMode.Fullscreen, runtime.ArmedMode);
                Assert.Equal(0, runtime.ArmedAtTimestamp);
                Assert.Equal(startup.Fullscreen, runtime.Fullscreen);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_EnteringAndExitingFullscreenSilentlyRearmsWithoutChangingReply()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0));
            var detector = new SequenceFullscreenDetector(false, true, false);
            var agent = new RecordingDialogueAgent();
            var window = CreateWindowWithCadence(settingsDirectory, time, detector, agent);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var startupReply = GetLastReply(window);

                time.Advance(TimeSpan.FromMinutes(1));
                time.SetLocalNow(new DateTime(2026, 7, 26, 10, 1, 0));
                window.ProcessEventTimerTick();
                var entered = window.CaptureAutomaticDialogueRuntime();

                Assert.Same(startupReply, GetLastReply(window));
                Assert.Single(agent.Calls);
                Assert.Equal(AutomaticCadenceMode.Fullscreen, entered.ArmedMode);
                Assert.Equal(TimeSpan.FromMinutes(60), entered.ScheduledDelay);
                Assert.Equal(TimeSpan.FromMinutes(1).Ticks, entered.ArmedAtTimestamp);

                time.Advance(TimeSpan.FromMinutes(1));
                time.SetLocalNow(new DateTime(2026, 7, 26, 10, 2, 0));
                window.ProcessEventTimerTick();
                var exited = window.CaptureAutomaticDialogueRuntime();

                Assert.Same(startupReply, GetLastReply(window));
                Assert.Single(agent.Calls);
                Assert.Equal(3, detector.ObserveCount);
                Assert.Equal(AutomaticCadenceMode.Daytime, exited.ArmedMode);
                Assert.Equal(TimeSpan.FromMinutes(5), exited.ScheduledDelay);
                Assert.Equal(TimeSpan.FromMinutes(2).Ticks, exited.ArmedAtTimestamp);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_NonFullscreenBandChangeSilentlyRearmsBeforePollingEvents()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 17, 59, 0));
            var detector = new SequenceFullscreenDetector(false, false);
            var agent = new RecordingDialogueAgent();
            var schedulerRandom = new EndpointRandom();
            var window = CreateWindowWithCadence(
                settingsDirectory,
                time,
                detector,
                agent,
                schedulerRandom);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var startupReply = GetLastReply(window);
                time.Advance(TimeSpan.FromMinutes(1));
                time.SetLocalNow(new DateTime(2026, 7, 26, 18, 0, 0));

                window.ProcessEventTimerTick();

                var runtime = window.CaptureAutomaticDialogueRuntime();
                Assert.Same(startupReply, GetLastReply(window));
                Assert.Single(agent.Calls);
                Assert.Equal(2, detector.ObserveCount);
                Assert.Equal(2, schedulerRandom.NextCount);
                Assert.Equal(AutomaticCadenceMode.Evening, runtime.ArmedMode);
                Assert.Equal(TimeSpan.FromMinutes(10), runtime.ScheduledDelay);
                Assert.Equal(TimeSpan.FromMinutes(1).Ticks, runtime.ArmedAtTimestamp);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_FullscreenBandChangeKeepsTheExistingFullscreenArm()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 17, 59, 0));
            var detector = new SequenceFullscreenDetector(true, true);
            var agent = new RecordingDialogueAgent(CompanionEvent.ClockTick);
            var window = CreateWindowWithCadence(settingsDirectory, time, detector, agent);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var before = window.CaptureAutomaticDialogueRuntime();
                time.Advance(TimeSpan.FromMinutes(1));
                time.SetLocalNow(new DateTime(2026, 7, 26, 18, 0, 0));

                window.ProcessEventTimerTick();

                var after = window.CaptureAutomaticDialogueRuntime();
                Assert.Equal(2, detector.ObserveCount);
                Assert.Equal(2, agent.Calls.Count);
                Assert.Equal(CompanionEvent.ClockTick, agent.Calls[^1].Trigger);
                Assert.Equal(AutomaticCadenceMode.Fullscreen, after.ArmedMode);
                Assert.Equal(before.ScheduledDelay, after.ScheduledDelay);
                Assert.Equal(before.ArmedAtTimestamp, after.ArmedAtTimestamp);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_NullFullscreenObservationPreservesEffectiveQuietAndReachesAgentRaw()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0));
            var detector = new SequenceFullscreenDetector(true, null);
            var agent = new RecordingDialogueAgent();
            var window = CreateWindowWithCadence(settingsDirectory, time, detector, agent);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                time.Advance(TimeSpan.FromMinutes(1));
                time.SetLocalNow(new DateTime(2026, 7, 26, 11, 0, 0));

                window.ProcessEventTimerTick();

                Assert.Equal(2, agent.Calls.Count);
                var clockTick = agent.Calls[^1];
                Assert.Equal(CompanionEvent.ClockTick, clockTick.Trigger);
                Assert.Null(clockTick.Fullscreen.Observed);
                Assert.True(clockTick.Fullscreen.EffectiveQuietMode);
                Assert.Equal(clockTick.Fullscreen, window.CaptureAutomaticDialogueRuntime().Fullscreen);
                Assert.Equal(2, detector.ObserveCount);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_NonfatalFullscreenProbeFailureBecomesUnknownObservation()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0));
            var detector = new SequenceFullscreenDetector(
                true,
                new InvalidOperationException("probe failed"));
            var agent = new RecordingDialogueAgent();
            var window = CreateWindowWithCadence(settingsDirectory, time, detector, agent);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                time.SetLocalNow(new DateTime(2026, 7, 26, 11, 0, 0));

                var exception = Record.Exception(window.ProcessEventTimerTick);

                Assert.Null(exception);
                Assert.Equal(2, detector.ObserveCount);
                Assert.Equal(2, agent.Calls.Count);
                Assert.Equal(new FullscreenSnapshot(null, true), agent.Calls[^1].Fullscreen);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_OnTimeAutomaticTickObservesDisplaysAndFullyRearms()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0));
            var detector = new SequenceFullscreenDetector(false, false);
            var agent = new RecordingDialogueAgent();
            var schedulerRandom = new EndpointRandom();
            var window = CreateWindowWithCadence(
                settingsDirectory,
                time,
                detector,
                agent,
                schedulerRandom);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                time.Advance(TimeSpan.FromMinutes(5));
                time.SetLocalNow(new DateTime(2026, 7, 26, 10, 5, 0));

                window.ProcessAutomaticTimerTick();

                var runtime = window.CaptureAutomaticDialogueRuntime();
                Assert.Equal(2, detector.ObserveCount);
                Assert.Equal(2, schedulerRandom.NextCount);
                Assert.Equal(2, agent.Calls.Count);
                Assert.Equal(CompanionEvent.Automatic, agent.Calls[^1].Trigger);
                Assert.Equal(new DateTime(2026, 7, 26, 10, 5, 0), agent.Calls[^1].LocalTime);
                Assert.Equal(new FullscreenSnapshot(false, false), agent.Calls[^1].Fullscreen);
                Assert.Equal(CompanionEvent.Automatic, GetLastReply(window).Trigger);
                Assert.True(runtime.IsScheduled);
                Assert.Equal(TimeSpan.FromMinutes(5), runtime.ScheduledDelay);
                Assert.Equal(TimeSpan.FromMinutes(5).Ticks, runtime.ArmedAtTimestamp);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_AutomaticTickStopsExistingTimerBeforeFullscreenObservation()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0));
            MainWindow? window = null;
            bool? scheduledDuringTickObservation = null;
            var detector = new SequenceFullscreenDetector(false, false)
            {
                OnObserve = callCount =>
                {
                    if (callCount == 2)
                    {
                        scheduledDuringTickObservation = window!
                            .CaptureAutomaticDialogueRuntime()
                            .IsScheduled;
                    }
                }
            };
            var agent = new RecordingDialogueAgent();
            window = CreateWindowWithCadence(settingsDirectory, time, detector, agent);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(window.CaptureAutomaticDialogueRuntime().IsScheduled);

                window.ProcessAutomaticTimerTick();

                Assert.False(scheduledDuringTickObservation);
                Assert.True(window.CaptureAutomaticDialogueRuntime().IsScheduled);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_LateAutomaticTickSilentlyRearms()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0));
            var detector = new SequenceFullscreenDetector(false, false);
            var agent = new RecordingDialogueAgent();
            var window = CreateWindowWithCadence(settingsDirectory, time, detector, agent);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var startupReply = GetLastReply(window);
                time.Advance(TimeSpan.FromMinutes(6) + TimeSpan.FromTicks(1));
                time.SetLocalNow(new DateTime(2026, 7, 26, 10, 6, 0));

                window.ProcessAutomaticTimerTick();

                var runtime = window.CaptureAutomaticDialogueRuntime();
                Assert.Same(startupReply, GetLastReply(window));
                Assert.Single(agent.Calls);
                Assert.Equal(2, detector.ObserveCount);
                Assert.Equal(TimeSpan.FromMinutes(6).Ticks + 1, runtime.ArmedAtTimestamp);
                Assert.Equal(TimeSpan.FromMinutes(5), runtime.ScheduledDelay);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_ModeChangedAutomaticTickSilentlyRearms()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0));
            var detector = new SequenceFullscreenDetector(false, true);
            var agent = new RecordingDialogueAgent();
            var window = CreateWindowWithCadence(settingsDirectory, time, detector, agent);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var startupReply = GetLastReply(window);
                time.Advance(TimeSpan.FromMinutes(1));
                time.SetLocalNow(new DateTime(2026, 7, 26, 10, 1, 0));

                window.ProcessAutomaticTimerTick();

                var runtime = window.CaptureAutomaticDialogueRuntime();
                Assert.Same(startupReply, GetLastReply(window));
                Assert.Single(agent.Calls);
                Assert.Equal(2, detector.ObserveCount);
                Assert.Equal(AutomaticCadenceMode.Fullscreen, runtime.ArmedMode);
                Assert.Equal(TimeSpan.FromMinutes(60), runtime.ScheduledDelay);
                Assert.Equal(TimeSpan.FromMinutes(1).Ticks, runtime.ArmedAtTimestamp);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_WallClockRollbackDoesNotDefeatMonotonicAutomaticDueTime()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0));
            var detector = new SequenceFullscreenDetector(false, false);
            var agent = new RecordingDialogueAgent();
            var window = CreateWindowWithCadence(settingsDirectory, time, detector, agent);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                time.Advance(TimeSpan.FromMinutes(5));
                time.SetLocalNow(new DateTime(2026, 7, 26, 9, 0, 0));

                window.ProcessAutomaticTimerTick();

                Assert.Equal(2, detector.ObserveCount);
                Assert.Equal(2, agent.Calls.Count);
                Assert.Equal(CompanionEvent.Automatic, agent.Calls[^1].Trigger);
                Assert.Equal(new DateTime(2026, 7, 26, 9, 0, 0), agent.Calls[^1].LocalTime);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_VisibleEventReplyResetsTheAutomaticCountdown()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0));
            var detector = new SequenceFullscreenDetector(false, false);
            var agent = new RecordingDialogueAgent();
            var window = CreateWindowWithCadence(settingsDirectory, time, detector, agent);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                time.Advance(TimeSpan.FromMinutes(1));
                time.SetLocalNow(new DateTime(2026, 7, 26, 11, 0, 0));

                window.ProcessEventTimerTick();

                var runtime = window.CaptureAutomaticDialogueRuntime();
                Assert.Equal(2, detector.ObserveCount);
                Assert.Equal(CompanionEvent.ClockTick, agent.Calls[^1].Trigger);
                Assert.Equal(TimeSpan.FromMinutes(1).Ticks, runtime.ArmedAtTimestamp);
                Assert.Equal(TimeSpan.FromMinutes(5), runtime.ScheduledDelay);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_VisibleDirectReplyResetsTheAutomaticCountdown()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0));
            var detector = new SequenceFullscreenDetector(false, false);
            var agent = new RecordingDialogueAgent();
            var window = CreateWindowWithCadence(settingsDirectory, time, detector, agent);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                time.Advance(TimeSpan.FromMinutes(1));
                time.SetLocalNow(new DateTime(2026, 7, 26, 10, 1, 0));

                window.SaySomething();

                var runtime = window.CaptureAutomaticDialogueRuntime();
                Assert.Equal(2, detector.ObserveCount);
                Assert.Equal(CompanionEvent.Click, agent.Calls[^1].Trigger);
                Assert.Equal(new DateTime(2026, 7, 26, 10, 1, 0), agent.Calls[^1].LocalTime);
                Assert.Equal(agent.Calls[^1].Fullscreen, runtime.Fullscreen);
                Assert.Equal(TimeSpan.FromMinutes(1).Ticks, runtime.ArmedAtTimestamp);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_SilentBudgetedEventPreservesTheAutomaticDeadline()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0));
            var detector = new SequenceFullscreenDetector(false, false);
            var agent = new RecordingDialogueAgent(CompanionEvent.ClockTick);
            var window = CreateWindowWithCadence(settingsDirectory, time, detector, agent);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var before = window.CaptureAutomaticDialogueRuntime();
                time.Advance(TimeSpan.FromMinutes(1));
                time.SetLocalNow(new DateTime(2026, 7, 26, 11, 0, 0));

                window.ProcessEventTimerTick();

                var after = window.CaptureAutomaticDialogueRuntime();
                Assert.Equal(2, detector.ObserveCount);
                Assert.Equal(CompanionEvent.ClockTick, GetLastReply(window).Trigger);
                Assert.False(GetLastReply(window).ShouldDisplayText);
                Assert.Equal(before.ArmedAtTimestamp, after.ArmedAtTimestamp);
                Assert.Equal(before.ScheduledDelay, after.ScheduledDelay);
                Assert.Equal(before.ArmedMode, after.ArmedMode);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_HiddenAndClosedQueuedTicksDoNotObserveOrRearm()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0));
            var detector = new SequenceFullscreenDetector(false, true);
            var agent = new RecordingDialogueAgent();
            var window = CreateWindowWithCadence(settingsDirectory, time, detector, agent);
            var closed = false;
            window.Closed += (_, _) => closed = true;
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.SetTrayAvailability(true);
                window.HideToTray();

                window.ProcessAutomaticTimerTick();
                window.ProcessEventTimerTick();

                Assert.Equal(1, detector.ObserveCount);
                Assert.False(window.CaptureAutomaticDialogueRuntime().IsScheduled);

                window.Close();
                window.ProcessAutomaticTimerTick();
                window.ProcessEventTimerTick();

                Assert.Equal(1, detector.ObserveCount);
                Assert.False(window.CaptureAutomaticDialogueRuntime().IsScheduled);
            }
            finally
            {
                if (!closed)
                {
                    window.Close();
                }
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_TrayRestoreResamplesAfterPositionCorrectionAndBeforeArming()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0));
            MainWindow? window = null;
            var observedVisible = false;
            var observedClamped = false;
            var observedDisarmed = false;
            var detector = new SequenceFullscreenDetector(false, true)
            {
                OnObserve = callCount =>
                {
                    if (callCount != 2)
                    {
                        return;
                    }

                    observedVisible = window!.IsVisible;
                    observedClamped = window.Left < 100_000 && window.Top > -100_000;
                    observedDisarmed = !window.CaptureAutomaticDialogueRuntime().IsScheduled;
                }
            };
            var agent = new RecordingDialogueAgent();
            window = CreateWindowWithCadence(settingsDirectory, time, detector, agent);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.SetTrayAvailability(true);
                window.Left = 100_000;
                window.Top = -100_000;
                window.HideToTray();

                window.ToggleVisibilityFromTray();

                var runtime = window.CaptureAutomaticDialogueRuntime();
                Assert.Equal(2, detector.ObserveCount);
                Assert.True(observedVisible);
                Assert.True(observedClamped);
                Assert.True(observedDisarmed);
                Assert.True(runtime.IsScheduled);
                Assert.Equal(new FullscreenSnapshot(true, true), runtime.Fullscreen);
                Assert.Equal(AutomaticCadenceMode.Fullscreen, runtime.ArmedMode);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_StartupThenSaySomething_ReplacesTheStartupReplyWithNewV2Text()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var window = CreateWindow(settingsDirectory);
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
            var settingsDirectory = CreateSettingsDirectory();
            var time = new ManualTimeProvider();
            time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0));
            using var factory = new ControlledDialogueFactory("unused full reply");
            var dialogue = DialogueService.CreateDeferred(factory.Create, time);
            var window = new MainWindow(new MainWindowOptions(
                PetSettings.Default,
                new SettingsService(settingsDirectory))
            {
                SuppressApplicationShutdownOnClose = true,
                DialogueService = dialogue,
                TimeProvider = time,
                ForegroundFullscreenDetector = new SequenceFullscreenDetector(false, false),
                DialogueScheduler = new DialogueScheduler(new EndpointRandom())
            });
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(factory.Entered.Wait(TimeSpan.FromSeconds(2)));
                var bubble = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("SpeechBubble"));
                var speech = Assert.IsType<TextBlock>(window.FindName("SpeechText"));
                Assert.Equal(Visibility.Visible, bubble.Visibility);
                var startupText = speech.Text;
                time.Advance(TimeSpan.FromMinutes(5));
                time.SetLocalNow(new DateTime(2026, 7, 26, 10, 5, 0));

                window.ProcessAutomaticTimerTick();

                var reply = GetLastReply(window);
                var runtime = window.CaptureAutomaticDialogueRuntime();
                Assert.False(reply.ShouldDisplayText);
                Assert.Equal(Visibility.Visible, bubble.Visibility);
                Assert.Equal(startupText, speech.Text);
                Assert.True(runtime.IsScheduled);
                Assert.Equal(TimeSpan.FromMinutes(5).Ticks, runtime.ArmedAtTimestamp);
            }
            finally
            {
                window.Close();
                factory.Release();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_ExplicitUserSilence_ClearsThePreviousBubble()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var window = CreateWindow(settingsDirectory);
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var bubble = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("SpeechBubble"));
            var speech = Assert.IsType<TextBlock>(window.FindName("SpeechText"));
            Assert.Equal(Visibility.Visible, bubble.Visibility);

            window.PresentReply(
                new AgentReply(
                    string.Empty,
                    DialogueCategory.DailyCare,
                    DialogueTreeKind.Companion,
                    CompanionEvent.Click,
                    ShouldDisplayText: false));

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
            foreach (var (trigger, enterThroughRealHandler) in
                     new (CompanionEvent, Action<MainWindow, ManualTimeProvider>)[]
                     {
                         (CompanionEvent.Startup, (window, _) =>
                         {
                             window.Show();
                             window.Dispatcher.Invoke(
                                 () => { },
                                 System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                         }),
                         (CompanionEvent.Click, (window, _) =>
                         {
                             window.Show();
                             window.Dispatcher.Invoke(
                                 () => { },
                                 DispatcherPriority.ApplicationIdle);
                             var say = Assert.IsType<MenuItem>(window.FindName("SayMenuItem"));
                             say.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                         }),
                         (CompanionEvent.Automatic, (window, time) =>
                         {
                             window.Show();
                             window.Dispatcher.Invoke(
                                 () => { },
                                 DispatcherPriority.ApplicationIdle);
                             var delay = window.CaptureAutomaticDialogueRuntime().ScheduledDelay;
                             time.Advance(delay);
                             time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0) + delay);
                             window.ProcessAutomaticTimerTick();
                         })
                     })
            {
                var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                var time = new ManualTimeProvider();
                time.SetLocalNow(new DateTime(2026, 7, 26, 10, 0, 0));
                var window = new MainWindow(new MainWindowOptions(
                    PetSettings.Default,
                    new SettingsService(settingsDirectory))
                {
                    SuppressApplicationShutdownOnClose = true,
                    DialogueService = new DialogueService(),
                    TimeProvider = time,
                    ForegroundFullscreenDetector = new SequenceFullscreenDetector(false, false),
                    DialogueScheduler = new DialogueScheduler(new EndpointRandom())
                });

                enterThroughRealHandler(window, time);

                var reply = GetLastReply(window);
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
            window.ShowBubble("hover countdown");
            var stage = Assert.IsType<Grid>(window.FindName("CharacterStage"));
            var bubble = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("SpeechBubble"));

            window.BubbleHover_MouseEnter(stage, null);
            Assert.False(window.CaptureRuntimeState().IsBubbleTimerEnabled);
            window.BubbleHover_MouseEnter(bubble, null);
            window.BubbleHover_MouseLeave(stage, null);
            Assert.False(window.CaptureRuntimeState().IsBubbleTimerEnabled);

            window.BubbleTimer_Tick(null, EventArgs.Empty);

            Assert.Equal(Visibility.Visible, bubble.Visibility);
            window.BubbleHover_MouseLeave(bubble, null);
            Assert.True(window.CaptureRuntimeState().IsBubbleTimerEnabled);
            window.Close();
        });
        DeleteSettingsDirectory(settingsDirectory);
    }

    [Fact]
    public void MainWindow_TrayRestore_ReclampsTheCharacterAfterDisplayTopologyChanges()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var window = CreateWindow(settingsDirectory);
            window.Show();
            window.SetTrayAvailability(true);
            window.Left = 100_000;
            window.Top = -100_000;
            window.HideToTray();

            window.ToggleVisibilityFromTray();

            var stage = Assert.IsType<Grid>(window.FindName("CharacterStage"));
            var character = new Rect(
                window.Left + ((window.ActualWidth - stage.ActualWidth) / 2),
                window.Top + window.ActualHeight - stage.ActualHeight,
                stage.ActualWidth,
                stage.ActualHeight);
            var work = SystemParameters.WorkArea;
            Assert.True(character.Left >= work.Left);
            Assert.True(character.Top >= work.Top);
            Assert.True(character.Right <= work.Right);
            Assert.True(character.Bottom <= work.Bottom);
            window.Close();
            DeleteSettingsDirectory(settingsDirectory);
        });
    }

    [Fact]
    public void MainWindow_TrayHideAndRestore_HidesAndRestoresTheIndependentBubble()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var window = CreateWindow(settingsDirectory);
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            window.SetTrayAvailability(true);
            var popup = Assert.IsType<Popup>(window.FindName("BubblePopup"));
            Assert.True(popup.IsOpen);
            Assert.False(window.CaptureRuntimeState().IsAnimationSuspended);
            Assert.Equal(
                BubbleCountdownState.CountingDown,
                window.CaptureRuntimeState().BubbleCountdownState);
            Assert.True(window.CaptureRuntimeState().IsBubbleTimerEnabled);

            window.HideToTray();
            Assert.False(popup.IsOpen);
            Assert.True(window.CaptureRuntimeState().IsAnimationSuspended);
            Assert.Equal(
                BubbleCountdownState.Suspended,
                window.CaptureRuntimeState().BubbleCountdownState);
            Assert.False(window.CaptureRuntimeState().IsBubbleTimerEnabled);

            window.ToggleVisibilityFromTray();
            Assert.True(popup.IsOpen);
            Assert.False(window.CaptureRuntimeState().IsAnimationSuspended);
            Assert.Equal(
                BubbleCountdownState.CountingDown,
                window.CaptureRuntimeState().BubbleCountdownState);
            Assert.True(window.CaptureRuntimeState().IsBubbleTimerEnabled);
            window.Close();
            DeleteSettingsDirectory(settingsDirectory);
        });
    }

    [Fact]
    public void MainWindow_TrayHideSuspendsPresentationSchedulersAndQueuesHiddenSpeech()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            using var factory = new ControlledDialogueFactory("blocked warmup");
            var dialogue = DialogueService.CreateDeferred(factory.Create);
            var window = CreateWindowWithDialogue(
                settingsDirectory,
                dialogue,
                TimeProvider.System);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(factory.Entered.Wait(TimeSpan.FromSeconds(2)));
                window.SetTrayAvailability(true);
                var popup = Assert.IsType<Popup>(window.FindName("BubblePopup"));
                var speech = Assert.IsType<TextBlock>(window.FindName("SpeechText"));
                Assert.True(window.CaptureRuntimeState().IsAutomaticTimerEnabled);
                Assert.True(window.CaptureRuntimeState().IsEventTimerEnabled);
                Assert.True(window.CaptureRuntimeState().IsAmbientTimerEnabled);

                window.HideToTray();

                Assert.False(window.CaptureRuntimeState().IsAutomaticTimerEnabled);
                Assert.False(window.CaptureRuntimeState().IsEventTimerEnabled);
                Assert.False(window.CaptureRuntimeState().IsAmbientTimerEnabled);
                Assert.False(popup.IsOpen);

                window.ShowBubble("藏起来也别穿帮");

                Assert.False(popup.IsOpen);
                Assert.Equal("藏起来也别穿帮", speech.Text);
                Assert.Equal(
                    BubbleCountdownState.Suspended,
                    window.CaptureRuntimeState().BubbleCountdownState);
                Assert.False(window.CaptureRuntimeState().IsBubbleTimerEnabled);

                var hiddenReply = GetLastReply(window);
                window.ProcessAutomaticTimerTick();
                window.ProcessEventTimerTick();
                window.ProcessAmbientSchedule();

                Assert.Same(hiddenReply, GetLastReply(window));
                Assert.False(popup.IsOpen);
                Assert.False(window.CaptureRuntimeState().IsAutomaticTimerEnabled);
                Assert.False(window.CaptureRuntimeState().IsEventTimerEnabled);
                Assert.False(window.CaptureRuntimeState().IsAmbientTimerEnabled);

                window.ToggleVisibilityFromTray();

                Assert.True(window.IsVisible);
                Assert.True(popup.IsOpen);
                Assert.Equal("藏起来也别穿帮", speech.Text);
                Assert.Equal(
                    BubbleCountdownState.CountingDown,
                    window.CaptureRuntimeState().BubbleCountdownState);
                Assert.True(window.CaptureRuntimeState().IsAutomaticTimerEnabled);
                Assert.True(window.CaptureRuntimeState().IsEventTimerEnabled);
                Assert.True(window.CaptureRuntimeState().IsAmbientTimerEnabled);
            }
            finally
            {
                window.Close();
                factory.Release();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_TraySayRestoresTheWindowBeforeShowingSpeech()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var window = CreateWindow(settingsDirectory);
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.SetTrayAvailability(true);
                window.HideToTray();

                window.SaySomething();

                Assert.True(window.IsVisible);
                Assert.True(Assert.IsType<Popup>(window.FindName("BubblePopup")).IsOpen);
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_LostCaptureAndMouseUpCompleteOneDragAndOneSettingsSave()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var settingsSaveCount = 0;
            var window = CreateWindowWithPersistenceWriters(
                settingsDirectory,
                _ =>
                {
                    Interlocked.Increment(ref settingsSaveCount);
                    return Task.CompletedTask;
                },
                _ => Task.CompletedTask,
                () => { });
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            window.BeginDragGesture();

            window.FinishDragOnce();
            window.FinishDragOnce();

            WaitForCondition(
                () => Volatile.Read(ref settingsSaveCount) == 1,
                TimeSpan.FromSeconds(5),
                () => $"Expected one post-drag save, got {settingsSaveCount}.");
            Assert.Equal(1, settingsSaveCount);
            window.Close();
            DeleteSettingsDirectory(settingsDirectory);
        });
    }

    [Fact]
    public void MainWindow_BubbleUsesIndependentPopupWithProtectedShadowAndDirectionalArrows()
    {
        var settingsDirectory = CreateSettingsDirectory();
        RunOnStaThread(() =>
        {
            var window = CreateWindow(settingsDirectory);
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var popup = Assert.IsType<Popup>(window.FindName("BubblePopup"));
            var surface = Assert.IsType<Border>(window.FindName("BubblePopupSurface"));
            var arrowUp = Assert.IsType<System.Windows.Shapes.Path>(window.FindName("BubbleArrowUp"));
            var arrowDown = Assert.IsType<System.Windows.Shapes.Path>(window.FindName("BubbleArrowDown"));
            var stage = Assert.IsType<Grid>(window.FindName("CharacterStage"));
            var localTop = window.ActualHeight - stage.ActualHeight;
            window.Left = SystemParameters.WorkArea.Left + 300;
            window.Top = SystemParameters.WorkArea.Top - localTop;

            window.ShowBubble("popup placement");
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            Assert.True(popup.IsOpen);
            Assert.Null(popup.PlacementTarget);
            Assert.Equal(PlacementMode.AbsolutePoint, popup.Placement);
            Assert.Equal(new Thickness(10), surface.Padding);
            Assert.Equal(Visibility.Visible, arrowUp.Visibility);
            Assert.Equal(Visibility.Collapsed, arrowDown.Visibility);
            Assert.Equal(
                window.Top + localTop + stage.ActualHeight + 30,
                popup.VerticalOffset + surface.Padding.Top,
                3);
            window.Close();
        });
        DeleteSettingsDirectory(settingsDirectory);
    }

    [Fact]
    public void MainWindow_OpenBubbleTracksWindowMovementInScreenCoordinates()
    {
        var settingsDirectory = CreateSettingsDirectory();
        RunOnStaThread(() =>
        {
            var window = CreateWindow(settingsDirectory);
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            window.Left = SystemParameters.WorkArea.Left + 360;
            window.Top = SystemParameters.WorkArea.Top + 180;
            window.ShowBubble("follow the character");
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var popupSurface = Assert.IsType<Border>(window.FindName("BubblePopupSurface"));
            var windowBefore = window.PointToScreen(new Point());
            var bubbleBefore = popupSurface.PointToScreen(new Point());

            window.Left += 140;
            window.Top += 70;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var windowAfter = window.PointToScreen(new Point());
            var bubbleAfter = popupSurface.PointToScreen(new Point());
            Assert.Equal(windowAfter.X - windowBefore.X, bubbleAfter.X - bubbleBefore.X, 1);
            Assert.Equal(windowAfter.Y - windowBefore.Y, bubbleAfter.Y - bubbleBefore.Y, 1);
            window.Close();
        });
        DeleteSettingsDirectory(settingsDirectory);
    }

    [Theory]
    [InlineData(PetScale.Small)]
    [InlineData(PetScale.Normal)]
    [InlineData(PetScale.Large)]
    public void MainWindow_PersistedTopPosition_ClampsTheCharacterExactlyToWorkAreaTop(PetScale scale)
    {
        var settingsDirectory = CreateSettingsDirectory();
        RunOnStaThread(() =>
        {
            var settings = new PetSettings(500, -10_000, scale, false, true);
            var window = CreateWindowWithScheduler(
                settingsDirectory,
                new AmbientActionScheduler(() => 0.5),
                settings);
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var stage = Assert.IsType<Grid>(window.FindName("CharacterStage"));

            var characterTop = window.Top + window.ActualHeight - stage.ActualHeight;

            Assert.Equal(SystemParameters.WorkArea.Top, characterTop, 3);
            window.Close();
        });
        DeleteSettingsDirectory(settingsDirectory);
    }

    [Fact]
    public void MainWindow_SizeChange_PreservesCharacterBottomCenterBeforeClamping()
    {
        var settingsDirectory = CreateSettingsDirectory();
        RunOnStaThread(() =>
        {
            var window = CreateWindow(settingsDirectory);
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var stage = Assert.IsType<Grid>(window.FindName("CharacterStage"));
            window.Left = SystemParameters.WorkArea.Left + 500;
            window.Top = SystemParameters.WorkArea.Top + 200;
            var oldCenter = window.Left + ((window.ActualWidth - stage.ActualWidth) / 2)
                + (stage.ActualWidth / 2);
            var oldBottom = window.Top + window.ActualHeight;

            Assert.IsType<MenuItem>(window.FindName("LargeSizeMenuItem"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var newCenter = window.Left + ((window.ActualWidth - stage.ActualWidth) / 2)
                + (stage.ActualWidth / 2);
            var newBottom = window.Top + window.ActualHeight;
            Assert.Equal(oldCenter, newCenter, 3);
            Assert.Equal(oldBottom, newBottom, 3);
            window.Close();
        });
        Assert.True(SpinWait.SpinUntil(
            () => File.Exists(Path.Combine(settingsDirectory, "settings.json"))
                  && !HasPendingSettingsWrite(settingsDirectory),
            TimeSpan.FromSeconds(5)));
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
            window.ApplyScale(scale);
            window.ShowBubble("layout measurement");
            var stage = Assert.IsType<Grid>(window.FindName("CharacterStage"));
            var bubble = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("SpeechBubble"));
            var popup = Assert.IsType<Popup>(window.FindName("BubblePopup"));
            var popupSurface = Assert.IsType<Border>(window.FindName("BubblePopupSurface"));
            stage.RenderTransform = Transform.Identity;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();

            var characterTop = window.Top + window.ActualHeight - stage.ActualHeight;
            var bubbleBottom =
                popup.VerticalOffset + popupSurface.Padding.Top + bubble.ActualHeight;

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
            window.ApplyScale(PetScale.Large);
            window.ShowBubble(longestLine.Text);
            var stage = Assert.IsType<Grid>(window.FindName("CharacterStage"));
            var bubble = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("SpeechBubble"));
            var popup = Assert.IsType<Popup>(window.FindName("BubblePopup"));
            var popupSurface = Assert.IsType<Border>(window.FindName("BubblePopupSurface"));
            stage.RenderTransform = Transform.Identity;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();

            var characterTop = window.Top + window.ActualHeight - stage.ActualHeight;
            var bubbleBottom =
                popup.VerticalOffset + popupSurface.Padding.Top + bubble.ActualHeight;
            var gap = characterTop - bubbleBottom;

            Assert.True(
                popup.IsOpen && bubble.ActualHeight > 0 && bubble.ActualWidth > 0,
                $"Bubble did not lay out line {longestLine.Id} ({longestLine.Text.Length} chars); "
                + $"bubble size={bubble.ActualWidth:F3}x{bubble.ActualHeight:F3}, gap={gap:F3}.");
            Assert.InRange(gap, 29.5, 30.5);
            window.Close();
        });
        DeleteSettingsDirectory(settingsDirectory);
    }

    [Fact]
    public void MainWindow_ExposesAccessibleNamesAndLiveSpeechStatus()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var window = CreateWindow(settingsDirectory);
            try
            {
                var stage = Assert.IsType<Grid>(window.FindName("CharacterStage"));
                var speech = Assert.IsType<TextBlock>(window.FindName("SpeechText"));
                var menu = Assert.IsType<ContextMenu>(window.FindName("ControlMenu"));
                var say = Assert.IsType<MenuItem>(window.FindName("SayMenuItem"));
                var hideToTray = Assert.IsType<MenuItem>(window.FindName("HideToTrayMenuItem"));

                Assert.Equal("佳怡桌宠", AutomationProperties.GetName(window));
                Assert.Equal("佳怡", AutomationProperties.GetName(stage));
                Assert.True(stage.Focusable);
                Assert.Contains("右键", AutomationProperties.GetHelpText(stage));
                window.ShowBubble("读屏也要听见这句话");
                Assert.Equal("佳怡说：读屏也要听见这句话", AutomationProperties.GetName(speech));
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(speech));
                Assert.Equal("佳怡控制面板", AutomationProperties.GetName(menu));
                Assert.Contains("佳怡", AutomationProperties.GetHelpText(say));
                Assert.False(hideToTray.IsEnabled);
                Assert.Contains("托盘暂时不可用", AutomationProperties.GetHelpText(hideToTray));
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_NewVisibleSpeechRaisesTheLiveRegionAnnouncementHook()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var announcements = new List<FrameworkElement>();
            var window = new MainWindow(new MainWindowOptions(
                PetSettings.Default,
                new SettingsService(settingsDirectory))
            {
                SuppressApplicationShutdownOnClose = true,
                AmbientScheduler = new AmbientActionScheduler(() => 0.5),
                AutoStartService = DisabledAutoStartService.Instance,
                DialogueService = new DialogueService(),
                AnnounceLiveRegionChanged = element => announcements.Add(element)
            });
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                announcements.Clear();
                var speech = Assert.IsType<TextBlock>(window.FindName("SpeechText"));

                window.ShowBubble("这句不是摆设");

                Assert.Single(announcements, speech);
                Assert.Equal("佳怡说：这句不是摆设", AutomationProperties.GetName(speech));
            }
            finally
            {
                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void MainWindow_KeyboardControlMenuRestoresFocusAfterPopupCloses()
    {
        RunOnStaThread(() =>
        {
            var settingsDirectory = CreateSettingsDirectory();
            var window = CreateWindow(settingsDirectory);
            ContextMenu? menu = null;
            try
            {
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var stage = Assert.IsType<Grid>(window.FindName("CharacterStage"));
                menu = Assert.IsType<ContextMenu>(window.FindName("ControlMenu"));
                var say = Assert.IsType<MenuItem>(window.FindName("SayMenuItem"));
                var source = PresentationSource.FromVisual(window);
                Assert.NotNull(source);
                window.Activate();
                Assert.True(stage.Focus());

                window.RaiseEvent(new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    source!,
                    Environment.TickCount,
                    Key.Apps)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                });
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                Assert.True(menu.IsOpen);
                Assert.True(say.IsKeyboardFocused);

                menu.RaiseEvent(new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(menu)!,
                    Environment.TickCount,
                    Key.Escape)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                });
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.False(menu.IsOpen);
                Assert.True(stage.IsKeyboardFocused);
            }
            finally
            {
                if (menu is not null)
                {
                    menu.IsOpen = false;
                }

                window.Close();
                DeleteSettingsDirectory(settingsDirectory);
            }
        });
    }

    [Fact]
    public void PetTheme_KeepsControlStylesKeyedUntilTheWindowScopesThem()
    {
        RunOnStaThread(() =>
        {
            var theme = new ResourceDictionary
            {
                Source = new Uri(
                    "/CompanionDesktopPet;component/Themes/PetTheme.xaml",
                    UriKind.Relative)
            };

            Assert.False(theme.Contains(typeof(ContextMenu)));
            Assert.False(theme.Contains(typeof(MenuItem)));
            Assert.False(theme.Contains(typeof(Separator)));
            Assert.False(theme.Contains(MenuItem.SeparatorStyleKey));
            Assert.True(theme.Contains("KawaiiContextMenuStyle"));
            Assert.True(theme.Contains("KawaiiMenuItemStyle"));
            Assert.True(theme.Contains("KawaiiSeparatorStyle"));
            Assert.True(theme.Contains("Pet.Brush.BubbleSurface"));
            Assert.True(theme.Contains("Pet.Brush.TextPrimary"));
        });
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
                    window.FindResource("Pet.Brush.MenuSurface"));
                Assert.Equal(2, surface.GradientStops.Count);
                Assert.Equal(Color.FromArgb(0xFA, 0xFF, 0xFD, 0xF7), surface.GradientStops[0].Color);
                Assert.Equal(0, surface.GradientStops[0].Offset);
                Assert.Equal(Color.FromArgb(0xE8, 0xFF, 0xE0, 0xEA), surface.GradientStops[1].Color);
                Assert.Equal(1, surface.GradientStops[1].Offset);

                var separatorBrush = Assert.IsType<LinearGradientBrush>(
                    window.FindResource("Pet.Brush.MenuSeparator"));
                Assert.Equal(3, separatorBrush.GradientStops.Count);
                Assert.Equal(
                    Color.FromArgb(0x00, 0xE9, 0x8F, 0xA4),
                    separatorBrush.GradientStops[0].Color);
                Assert.Equal(0, separatorBrush.GradientStops[0].Offset);
                Assert.Equal(
                    Color.FromArgb(0x99, 0xE9, 0x8F, 0xA4),
                    separatorBrush.GradientStops[1].Color);
                Assert.Equal(0.5, separatorBrush.GradientStops[1].Offset);
                Assert.Equal(
                    Color.FromArgb(0x00, 0xE9, 0x8F, 0xA4),
                    separatorBrush.GradientStops[2].Color);
                Assert.Equal(1, separatorBrush.GradientStops[2].Offset);

                menu.IsOpen = true;
                menu.ApplyTemplate();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.Equal(270, menu.MinWidth);
                var shell = Assert.IsType<Border>(menu.Template.FindName("MenuShell", menu));
                Assert.Equal(new CornerRadius(24), shell.CornerRadius);
                Assert.Equal(new Thickness(2), shell.BorderThickness);
                Assert.IsType<DropShadowEffect>(shell.Effect);
                var presenter = Assert.IsType<ItemsPresenter>(shell.Child);
                Assert.Equal(
                    KeyboardNavigationMode.Cycle,
                    KeyboardNavigation.GetDirectionalNavigation(presenter));

                var separator = Assert.IsType<Separator>(menu.Items[3]);
                separator.ApplyTemplate();
                var separatorChrome = Assert.IsType<Border>(
                    VisualTreeHelper.GetChild(separator, 0));
                Assert.Same(separatorBrush, separatorChrome.Background);

                var say = Assert.IsType<MenuItem>(window.FindName("SayMenuItem"));
                say.ApplyTemplate();
                var chrome = Assert.IsType<Border>(
                    say.Template.FindName("MenuItemChrome", say));
                Assert.Equal(new CornerRadius(14), chrome.CornerRadius);
                Assert.Equal(35, say.MinHeight);
                Assert.Equal("✦", say.Tag);
                Assert.Equal("♡", Assert.IsType<MenuItem>(window.FindName("GreetingMenuItem")).Tag);
                Assert.Equal("☾", Assert.IsType<MenuItem>(window.FindName("PauseMenuItem")).Tag);
                Assert.Equal("⌁", Assert.IsType<MenuItem>(window.FindName("TopmostMenuItem")).Tag);
                Assert.Equal("⌂", Assert.IsType<MenuItem>(window.FindName("RestorePositionMenuItem")).Tag);
                Assert.Equal("☁", Assert.IsType<MenuItem>(window.FindName("ExitMenuItem")).Tag);

                Assert.IsType<SolidColorBrush>(window.FindResource("Pet.Brush.MenuItemHover"));
                Assert.IsType<SolidColorBrush>(window.FindResource("Pet.Brush.MenuHighlight"));
                var hoverTrigger = say.Template.Triggers
                    .OfType<Trigger>()
                    .Single(trigger =>
                        trigger.Property == MenuItem.IsHighlightedProperty
                        && Equals(trigger.Value, true));
                var hoverSetters = hoverTrigger.Setters.OfType<Setter>().ToArray();
                Assert.Equal(
                    "Pet.Brush.MenuItemHover",
                    Assert.IsType<DynamicResourceExtension>(hoverSetters.Single(setter =>
                        setter.TargetName == "MenuItemChrome"
                        && setter.Property == Border.BackgroundProperty).Value).ResourceKey);
                Assert.Equal(
                    "Pet.Brush.MenuHighlight",
                    Assert.IsType<DynamicResourceExtension>(hoverSetters.Single(setter =>
                        setter.TargetName == "MenuItemChrome"
                        && setter.Property == Border.BorderBrushProperty).Value).ResourceKey);

                var checkedHighlightTrigger = say.Template.Triggers
                    .OfType<MultiTrigger>()
                    .Single(trigger =>
                        trigger.Conditions.Count == 2
                        && trigger.Conditions.Cast<System.Windows.Condition>().Any(condition =>
                            condition.Property == MenuItem.IsHighlightedProperty
                            && Equals(condition.Value, true))
                        && trigger.Conditions.Cast<System.Windows.Condition>().Any(condition =>
                            condition.Property == MenuItem.IsCheckedProperty
                            && Equals(condition.Value, true)));
                var checkedHighlightForeground = checkedHighlightTrigger.Setters
                    .OfType<Setter>()
                    .Single(setter =>
                        setter.TargetName == "IconGlyph"
                        && setter.Property == TextBlock.ForegroundProperty);
                Assert.Equal(
                    "Pet.Brush.MenuItemHoverText",
                    Assert.IsType<DynamicResourceExtension>(
                        checkedHighlightForeground.Value).ResourceKey);

                var disabledTrigger = say.Template.Triggers
                    .OfType<Trigger>()
                    .Single(trigger =>
                        trigger.Property == UIElement.IsEnabledProperty
                        && Equals(trigger.Value, false));
                var disabledSetters = disabledTrigger.Setters.OfType<Setter>().ToArray();
                var disabledOpacity = disabledSetters.Single(setter =>
                    setter.TargetName == "MenuItemChrome"
                    && setter.Property == UIElement.OpacityProperty);
                Assert.Equal(
                    "Pet.Opacity.Disabled",
                    Assert.IsType<DynamicResourceExtension>(disabledOpacity.Value).ResourceKey);
                var disabledForeground = disabledSetters.Single(setter =>
                    string.IsNullOrEmpty(setter.TargetName)
                    && setter.Property == Control.ForegroundProperty);
                Assert.Equal(
                    "Pet.Brush.TextDisabled",
                    Assert.IsType<DynamicResourceExtension>(disabledForeground.Value).ResourceKey);

                size = menu.Items
                    .OfType<MenuItem>()
                    .Single(item => Equals(item.Header, "大小"));
                Assert.Equal("◌", size.Tag);
                size.ApplyTemplate();
                Assert.Equal(MenuItemRole.SubmenuHeader, size.Role);
                var submenuArrow = Assert.IsType<TextBlock>(
                    size.Template.FindName("SubmenuArrow", size));
                Assert.Equal("›", submenuArrow.Text);
                Assert.Equal(Visibility.Visible, submenuArrow.Visibility);

                Assert.True(size.Focus());
                var presentationSource = PresentationSource.FromVisual(size);
                Assert.NotNull(presentationSource);
                size.RaiseEvent(new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    presentationSource!,
                    Environment.TickCount,
                    Key.Right)
                {
                    RoutedEvent = Keyboard.KeyDownEvent
                });
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var popup = Assert.IsType<Popup>(
                    size.Template.FindName("PART_Popup", size));
                Assert.True(size.IsSubmenuOpen);
                Assert.True(popup.IsOpen);

                var popupGrid = Assert.IsType<Grid>(popup.Child);
                var submenuShell = Assert.IsType<Border>(popupGrid.Children[0]);
                var submenuItemsHost = Assert.IsType<StackPanel>(submenuShell.Child);
                Assert.True(submenuItemsHost.IsItemsHost);
                Assert.Equal(
                    KeyboardNavigationMode.Cycle,
                    KeyboardNavigation.GetDirectionalNavigation(submenuItemsHost));

                var normal = Assert.IsType<MenuItem>(window.FindName("NormalSizeMenuItem"));
                normal.ApplyTemplate();
                var checkedGlyph = Assert.IsType<TextBlock>(
                    normal.Template.FindName("IconGlyph", normal));
                Assert.Equal("✓", checkedGlyph.Text);

                var small = Assert.IsType<MenuItem>(window.FindName("SmallSizeMenuItem"));
                small.ApplyTemplate();
                var uncheckedGlyph = Assert.IsType<TextBlock>(
                    small.Template.FindName("IconGlyph", small));
                Assert.Equal(string.Empty, uncheckedGlyph.Text);
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
            var window = CreateWindow(settingsDirectory);

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
            () => !HasPendingSettingsWrite(settingsDirectory),
            TimeSpan.FromSeconds(5)));
        if (Directory.Exists(settingsDirectory))
        {
            Directory.Delete(settingsDirectory, true);
        }
    }

    private static void RunOnStaThread(Action action) => StaHost.Value.Invoke(action);

    private static Task RunOnStaThreadAsync(Func<Task> action) =>
        StaHost.Value.InvokeAsync(action);

    private static MainWindow CreateWindow(
        string settingsDirectory,
        bool suppressApplicationShutdownOnClose = true,
        Action? shutdownApplication = null) =>
        new(new MainWindowOptions(
            PetSettings.Default,
            new SettingsService(settingsDirectory))
        {
            SuppressApplicationShutdownOnClose = suppressApplicationShutdownOnClose,
            ShutdownApplication = shutdownApplication
        });

    private static MainWindow CreateWindowWithScheduler(
        string settingsDirectory,
        AmbientActionScheduler ambientScheduler,
        PetSettings? settings = null,
        IPetAnimationController? animationController = null) =>
        new(new MainWindowOptions(
            settings ?? PetSettings.Default,
            new SettingsService(settingsDirectory))
        {
            SuppressApplicationShutdownOnClose = true,
            AmbientScheduler = ambientScheduler,
            AnimationController = animationController
        });

    private static MainWindow CreateWindowWithAutoStart(
        string settingsDirectory,
        IAutoStartService autoStartService,
        bool suppressApplicationShutdownOnClose = true,
        Action? shutdownApplication = null) =>
        new(new MainWindowOptions(
            PetSettings.Default,
            new SettingsService(settingsDirectory))
        {
            SuppressApplicationShutdownOnClose = suppressApplicationShutdownOnClose,
            ShutdownApplication = shutdownApplication,
            AmbientScheduler = new AmbientActionScheduler(() => 0.5),
            AutoStartService = autoStartService
        });

    private static MainWindow CreateWindowWithMemoryWriter(
        string settingsDirectory,
        Func<AgentMemorySnapshot, Task> saveAgentMemoryAsync,
        DialogueService dialogue) =>
        new(new MainWindowOptions(
            PetSettings.Default,
            new SettingsService(settingsDirectory))
        {
            SuppressApplicationShutdownOnClose = true,
            AmbientScheduler = new AmbientActionScheduler(() => 0.5),
            AutoStartService = DisabledAutoStartService.Instance,
            SaveAgentMemoryAsync = saveAgentMemoryAsync,
            DialogueService = dialogue,
            TimeProvider = TimeProvider.System
        });

    private static MainWindow CreateWindowWithPersistenceWriters(
        string settingsDirectory,
        Func<PetSettings, Task> saveSettingsAsync,
        Func<AgentMemorySnapshot, Task> saveAgentMemoryAsync,
        Action shutdownApplication) =>
        new(new MainWindowOptions(
            PetSettings.Default,
            new SettingsService(settingsDirectory))
        {
            SuppressApplicationShutdownOnClose = false,
            ShutdownApplication = shutdownApplication,
            AmbientScheduler = new AmbientActionScheduler(() => 0.5),
            AutoStartService = DisabledAutoStartService.Instance,
            SaveAgentMemoryAsync = saveAgentMemoryAsync,
            SaveSettingsAsync = saveSettingsAsync,
            DialogueService = new DialogueService(),
            TimeProvider = TimeProvider.System
        });

    private static MainWindow CreateWindowWithDialogue(
        string settingsDirectory,
        DialogueService dialogue,
        TimeProvider timeProvider,
        Func<AgentMemorySnapshot, Task>? saveAgentMemoryAsync = null,
        DialogueWarmupCoordinator? warmupCoordinator = null) =>
        new(new MainWindowOptions(
            PetSettings.Default,
            new SettingsService(settingsDirectory))
        {
            SuppressApplicationShutdownOnClose = true,
            AmbientScheduler = new AmbientActionScheduler(() => 0.5),
            AutoStartService = DisabledAutoStartService.Instance,
            SaveAgentMemoryAsync = saveAgentMemoryAsync,
            DialogueService = dialogue,
            TimeProvider = timeProvider,
            WarmupCoordinator = warmupCoordinator
        });

    private static MainWindow CreateWindowWithCadence(
        string settingsDirectory,
        TimeProvider timeProvider,
        IForegroundFullscreenDetector fullscreenDetector,
        RecordingDialogueAgent agent,
        EndpointRandom? schedulerRandom = null,
        IAutoStartService? autoStartService = null)
    {
        var dialogue = DialogueService.CreateDeferred(_ => agent, timeProvider);
        Assert.True(dialogue.WarmupAsync().GetAwaiter().GetResult());
        return new MainWindow(new MainWindowOptions(
            PetSettings.Default,
            new SettingsService(settingsDirectory))
        {
            SuppressApplicationShutdownOnClose = true,
            AmbientScheduler = new AmbientActionScheduler(() => 0.5),
            AutoStartService = autoStartService ?? DisabledAutoStartService.Instance,
            DialogueService = dialogue,
            TimeProvider = timeProvider,
            ForegroundFullscreenDetector = fullscreenDetector,
            DialogueScheduler = new DialogueScheduler(schedulerRandom ?? new EndpointRandom())
        });
    }

    private static AmbientActionScheduler CreateSchedulerWithTimeProvider(
        Func<double> sample,
        TimeProvider timeProvider)
    {
        return new AmbientActionScheduler(sample, timeProvider);
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

    private sealed class ControlledAnimationController : IPetAnimationController
    {
        private Action? _ambientCompletion;

        public int PauseIdleCount { get; private set; }
        public int ResumeIdleCount { get; private set; }

        public void CompleteAmbientAction()
        {
            var completion = _ambientCompletion;
            _ambientCompletion = null;
            if (completion is null)
            {
                throw new InvalidOperationException("No ambient animation is awaiting completion.");
            }

            completion();
        }

        public void StartIdle()
        {
        }

        public void PauseIdle()
        {
            PauseIdleCount++;
        }

        public void ResumeIdle()
        {
            ResumeIdleCount++;
        }

        public void PlayClickReaction()
        {
        }

        public void PlayClickReaction(ClickSide clickSide)
        {
        }

        public void SetDragLean(double horizontalDelta)
        {
        }

        public void PlayLanding(Action? completed)
        {
            _ambientCompletion = completed;
        }

        public void PlayBlink(bool doubleBlink, Action completed)
        {
            _ambientCompletion = completed;
        }

        public void PlayGreeting(Action completed)
        {
            _ambientCompletion = completed;
        }

        public void CancelAmbientAction()
        {
            _ambientCompletion = null;
        }

        public void Suspend()
        {
        }

        public void Resume()
        {
        }

        public void Dispose()
        {
            _ambientCompletion = null;
        }
    }

    private static bool HasPendingSettingsWrite(string settingsDirectory) =>
        Directory.Exists(settingsDirectory)
        && Directory.EnumerateFiles(
                settingsDirectory,
                "settings.json.*.tmp",
                SearchOption.TopDirectoryOnly)
            .Any();

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
            if (Application.Current is { } existingApplication)
            {
                _application = existingApplication;
                _dispatcher = existingApplication.Dispatcher;
                return;
            }

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

        public int UtcNowReadCount { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            UtcNowReadCount++;
            return _utcNow;
        }

        public void Advance(TimeSpan elapsed) => _timestamp += elapsed.Ticks;

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;

        public void SetLocalNow(DateTime value)
        {
            var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
            var offset = TimeZoneInfo.Local.GetUtcOffset(unspecified);
            _utcNow = new DateTimeOffset(unspecified, offset).ToUniversalTime();
        }
    }

    private sealed class EndpointRandom : Random
    {
        public int NextCount { get; private set; }

        public override int Next(int minValue, int maxValue)
        {
            NextCount++;
            return minValue;
        }
    }

    private sealed class SequenceFullscreenDetector(params object?[] observations)
        : IForegroundFullscreenDetector
    {
        private readonly Queue<object?> _observations = new(observations);

        public int ObserveCount { get; private set; }
        public List<nint> ExcludedWindows { get; } = [];
        public Action<int>? OnObserve { get; init; }

        public bool? Observe(nint excludedWindow)
        {
            ObserveCount++;
            ExcludedWindows.Add(excludedWindow);
            OnObserve?.Invoke(ObserveCount);
            if (_observations.Count == 0)
            {
                throw new InvalidOperationException("No fullscreen observation remains.");
            }

            return _observations.Dequeue() switch
            {
                Exception exception => throw exception,
                bool value => value,
                null => null,
                var value => throw new InvalidOperationException(
                    $"Unsupported fullscreen observation {value.GetType().Name}.")
            };
        }
    }

    private readonly record struct RecordedDialogueCall(
        CompanionEvent Trigger,
        DateTime LocalTime,
        FullscreenSnapshot Fullscreen);

    private sealed class RecordingDialogueAgent(params CompanionEvent[] silentEvents)
        : ICompanionDialogueAgent
    {
        private readonly HashSet<CompanionEvent> _silentEvents = [.. silentEvents];
        private readonly AgentMemorySnapshot _snapshot = new(
            CharacterState.Create(new DateTime(2026, 7, 26, 10, 0, 0)),
            [],
            0,
            null,
            []);

        public List<RecordedDialogueCall> Calls { get; } = [];
        public int CreateSnapshotCallCount { get; private set; }
        public DateTime? NextStoryDueAt => null;

        public AgentMemorySnapshot CreateSnapshot()
        {
            CreateSnapshotCallCount++;
            return _snapshot;
        }

        public AgentReply Respond(
            CompanionEvent trigger,
            DateTime localTime,
            Random random,
            FullscreenSnapshot fullscreen)
        {
            Calls.Add(new RecordedDialogueCall(trigger, localTime, fullscreen));
            var displaysText = !_silentEvents.Contains(trigger);
            return new AgentReply(
                displaysText ? $"{trigger} cadence reply" : string.Empty,
                DialogueCategory.CharacterLife,
                DialogueTreeKind.Companion,
                trigger,
                SceneId: $"cadence:{trigger}",
                ShouldDisplayText: displaysText,
                SemanticGroup: "cadence.test");
        }
    }

    private sealed class FakeAutoStartService : IAutoStartService
    {
        public bool Enabled { get; set; }
        public bool ReadSucceeds { get; set; } = true;
        public bool SetSucceeds { get; set; } = true;
        public int ReadCount { get; private set; }
        public List<bool> WriteRequests { get; } = [];

        public bool TryGetEnabled(out bool enabled)
        {
            ReadCount++;
            enabled = Enabled;
            return ReadSucceeds;
        }

        public bool TrySetEnabled(bool enabled)
        {
            WriteRequests.Add(enabled);
            if (SetSucceeds)
            {
                Enabled = enabled;
            }

            return SetSucceeds;
        }
    }

    private sealed class FixedIdleTimeProvider(TimeSpan value) : IIdleTimeProvider
    {
        public TimeSpan LastReturnedValue { get; private set; }

        public TimeSpan? GetIdleTime()
        {
            LastReturnedValue = value;
            return value;
        }
    }

    private sealed class ControlledMemoryWriter
    {
        private readonly TaskCompletionSource _firstSaveStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstSave = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<AgentMemorySnapshot> _snapshots = [];
        private int _activeWrites;

        public Task FirstSaveStarted => _firstSaveStarted.Task;
        public int CallCount { get; private set; }
        public int CompletedCount { get; private set; }
        public int MaximumConcurrentWrites { get; private set; }
        public int ActiveWrites => _activeWrites;
        public IReadOnlyList<AgentMemorySnapshot> Snapshots => _snapshots;

        public async Task SaveAsync(AgentMemorySnapshot snapshot)
        {
            CallCount++;
            _snapshots.Add(snapshot);
            _activeWrites++;
            MaximumConcurrentWrites = Math.Max(MaximumConcurrentWrites, _activeWrites);
            try
            {
                if (CallCount == 1)
                {
                    _firstSaveStarted.TrySetResult();
                    await _releaseFirstSave.Task;
                }

                CompletedCount++;
            }
            finally
            {
                _activeWrites--;
            }
        }

        public void ReleaseFirstSave() => _releaseFirstSave.TrySetResult();
    }

    private sealed class ControlledDialogueFactory : IDisposable
    {
        private readonly ManualResetEventSlim _entered = new();
        private readonly ManualResetEventSlim _release = new();

        public ControlledDialogueFactory(string replyText) =>
            Agent = new FixedDialogueAgent(replyText);

        public FixedDialogueAgent Agent { get; }
        public int CallCount { get; private set; }
        public ManualResetEventSlim Entered => _entered;

        public ICompanionDialogueAgent Create(AgentMemorySnapshot? snapshot)
        {
            CallCount++;
            _entered.Set();
            _release.Wait();
            return Agent;
        }

        public void Release() => _release.Set();

        public void Dispose()
        {
            _release.Set();
            _entered.Dispose();
            _release.Dispose();
        }
    }

    private sealed class FixedDialogueAgent : ICompanionDialogueAgent
    {
        private static readonly DialogueLine FullLine = new(
            "full-test-line",
            DialogueCategory.CharacterLife,
            DialogueCategoryGroup.CharacterLife,
            "full.test",
            "full.test",
            DialogueOutputMode.SelfTalk,
            DialogueTrigger.Any,
            ["none"],
            "gentle",
            0,
            1,
            1,
            1,
            1,
            false,
            true,
            "full test line",
            "new_character_life",
            "catalog:test",
            "test fixture");

        private readonly string _replyText;
        private readonly string _sceneId;
        private readonly string _sourceKind;
        private readonly AgentMemorySnapshot _snapshot = new(
            CharacterState.Create(new DateTime(2026, 7, 24, 9, 0, 0, DateTimeKind.Local)),
            [],
            0,
            null,
            []);

        public FixedDialogueAgent(
            string replyText,
            string sceneId = "full:test",
            string sourceKind = "new_character_life")
        {
            _replyText = replyText;
            _sceneId = sceneId;
            _sourceKind = sourceKind;
        }

        public DateTime? LastRespondedAt { get; private set; }
        public DateTime? NextStoryDueAt => null;

        public AgentMemorySnapshot CreateSnapshot() => _snapshot;

        public AgentReply Respond(
            CompanionEvent trigger,
            DateTime localTime,
            Random random,
            FullscreenSnapshot fullscreen)
        {
            LastRespondedAt = localTime;
            return new AgentReply(
                _replyText,
                DialogueCategory.CharacterLife,
                DialogueTreeKind.Companion,
                trigger,
                SceneId: _sceneId,
                SourceLine: FullLine with { Text = _replyText, SourceKind = _sourceKind },
                SemanticGroup: "full.test");
        }
    }

    private static AgentReply GetLastReply(MainWindow window)
    {
        return Assert.IsType<AgentReply>(window.LastReply);
    }
}
