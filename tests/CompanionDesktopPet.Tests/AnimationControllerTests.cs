using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CompanionDesktopPet.UI;

namespace CompanionDesktopPet.Tests;

public sealed class AnimationControllerTests
{
    [Fact]
    public void IdlePauseResumeAndClick_ManageAnimationState()
    {
        RunOnStaThread(() =>
        {
            var breathing = new ScaleTransform();
            var sway = new RotateTransform();
            var floating = new TranslateTransform();
            var reactionScale = new ScaleTransform();
            var reactionRotation = new RotateTransform();
            var actionScale = new ScaleTransform();
            var actionRotation = new RotateTransform();
            var actionOffset = new TranslateTransform();
            var hearts = new FrameworkElement[] { new TextBlock(), new TextBlock(), new TextBlock() };
            var controller = new AnimationController(
                breathing,
                sway,
                floating,
                reactionScale,
                reactionRotation,
                actionScale,
                actionRotation,
                actionOffset,
                hearts);

            controller.StartIdle();
            Assert.True(breathing.HasAnimatedProperties);
            Assert.True(sway.HasAnimatedProperties);
            Assert.True(floating.HasAnimatedProperties);

            controller.PauseIdle();
            Assert.True(controller.IsPaused);
            controller.ResumeIdle();
            Assert.False(controller.IsPaused);

            controller.PlayClickReaction();
            Assert.True(reactionScale.HasAnimatedProperties);
            Assert.True(reactionRotation.HasAnimatedProperties);
            Assert.All(hearts, heart => Assert.True(heart.HasAnimatedProperties));

            controller.SetDragLean(1_000);
            Assert.Equal(8, actionRotation.Angle);
            controller.SetDragLean(-1_000);
            Assert.Equal(-8, actionRotation.Angle);

            controller.PlayLanding();
            Assert.True(actionRotation.HasAnimatedProperties);
            Assert.True(actionOffset.HasAnimatedProperties);
        });
    }

    [Fact]
    public void ClickReaction_TiltsAwayFromClickedSideWhenTheSameSideRepeatsQuickly()
    {
        RunClickReactionScenario((controller, reactionRotation) =>
        {
            controller.PlayClickReaction(ClickSide.Left);
            var firstLeftClick = SampleFor(
                () => reactionRotation.Angle,
                TimeSpan.FromMilliseconds(80));
            controller.PlayClickReaction(ClickSide.Left);
            var repeatedLeftClick = SampleFor(
                () => reactionRotation.Angle,
                TimeSpan.FromMilliseconds(150));
            controller.PlayClickReaction(ClickSide.Right);
            var rightClick = SampleFor(
                () => reactionRotation.Angle,
                TimeSpan.FromMilliseconds(150));

            Assert.True(firstLeftClick.Max() > 0.3);
            Assert.True(firstLeftClick.Min() >= -0.001);
            Assert.True(repeatedLeftClick.Max() > 0.3);
            Assert.True(repeatedLeftClick.Min() >= -0.001);
            Assert.True(rightClick.Min() < -0.3);
            Assert.True(rightClick.Max() <= 0.001);
            WaitFor(
                () => Math.Abs(reactionRotation.Angle) < 0.001,
                TimeSpan.FromMilliseconds(500));
            Assert.Equal(0, reactionRotation.Angle);
        });
    }

    [Fact]
    public void ClickReaction_WithoutClickSideUsesAStableFallback()
    {
        RunClickReactionScenario((controller, reactionRotation) =>
        {
            controller.PlayClickReaction();
            var first = SampleFor(
                () => reactionRotation.Angle,
                TimeSpan.FromMilliseconds(80));
            controller.PlayClickReaction();
            var second = SampleFor(
                () => reactionRotation.Angle,
                TimeSpan.FromMilliseconds(150));

            Assert.True(first.Max() > 0.3);
            Assert.True(first.Min() >= -0.001);
            Assert.True(second.Max() > 0.3);
            Assert.True(second.Min() >= -0.001);
        });
    }

    [Fact]
    public void AmbientAnimations_CompleteOnceAndRestoreNeutralBaseValues()
    {
        RunOnStaThread(() =>
        {
            var actionScale = new ScaleTransform();
            var actionRotation = new RotateTransform();
            var actionOffset = new TranslateTransform();
            var blinkOverlay = new Border();
            var greetingBadge = new Border();
            var greetingBadgeOffset = new TranslateTransform();
            var controller = new AnimationController(
                new ScaleTransform(),
                new RotateTransform(),
                new TranslateTransform(),
                new ScaleTransform(),
                new RotateTransform(),
                actionScale,
                actionRotation,
                actionOffset,
                [],
                blinkOverlay,
                greetingBadge,
                greetingBadgeOffset);
            var host = new Window
            {
                Content = new Grid
                {
                    RenderTransform = new TransformGroup
                    {
                        Children = { actionScale, actionRotation, actionOffset }
                    },
                    Children =
                    {
                        blinkOverlay,
                        greetingBadge
                    }
                }
            };
            greetingBadge.RenderTransform = greetingBadgeOffset;
            host.Show();
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            try
            {
                var blinkCompletions = 0;
                controller.PlayBlink(doubleBlink: false, completed: () => blinkCompletions++);
                Assert.True(blinkOverlay.HasAnimatedProperties);
                WaitFor(() => blinkCompletions == 1, TimeSpan.FromSeconds(2));
                Assert.Equal(1, blinkCompletions);
                AssertNeutralAmbientBaseValues(blinkOverlay, greetingBadge, greetingBadgeOffset, actionScale, actionRotation, actionOffset);

                var cancelledCompletions = 0;
                controller.PlayBlink(doubleBlink: true, completed: () => cancelledCompletions++);
                controller.CancelAmbientAction();
                AssertNeutralAmbientBaseValues(blinkOverlay, greetingBadge, greetingBadgeOffset, actionScale, actionRotation, actionOffset);
                WaitFor(() => false, TimeSpan.FromMilliseconds(850), expectCompletion: false);
                Assert.Equal(0, cancelledCompletions);

                var greetingCompletions = 0;
                controller.PlayGreeting(() => greetingCompletions++);
                Assert.True(actionRotation.HasAnimatedProperties);
                Assert.True(actionOffset.HasAnimatedProperties);
                Assert.True(greetingBadge.HasAnimatedProperties);
                WaitFor(() => greetingCompletions == 1, TimeSpan.FromSeconds(3));
                Assert.Equal(1, greetingCompletions);
                AssertNeutralAmbientBaseValues(blinkOverlay, greetingBadge, greetingBadgeOffset, actionScale, actionRotation, actionOffset);

                var landingCompletions = 0;
                controller.PlayLanding(() => landingCompletions++);
                WaitFor(() => landingCompletions == 1, TimeSpan.FromSeconds(2));
                Assert.Equal(1, landingCompletions);
                AssertNeutralAmbientBaseValues(blinkOverlay, greetingBadge, greetingBadgeOffset, actionScale, actionRotation, actionOffset);
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Fact]
    public void ConstrainedAmbientProperties_NeverOvershootTheirVisualBounds()
    {
        RunOnStaThread(() =>
        {
            var actionScale = new ScaleTransform();
            var actionRotation = new RotateTransform();
            var actionOffset = new TranslateTransform();
            var blinkOverlay = new Border();
            var greetingBadge = new Border();
            var greetingBadgeOffset = new TranslateTransform();
            var controller = new AnimationController(
                new ScaleTransform(),
                new RotateTransform(),
                new TranslateTransform(),
                new ScaleTransform(),
                new RotateTransform(),
                actionScale,
                actionRotation,
                actionOffset,
                [],
                blinkOverlay,
                greetingBadge,
                greetingBadgeOffset);
            var host = new Window
            {
                Content = new Grid
                {
                    RenderTransform = new TransformGroup
                    {
                        Children = { actionScale, actionRotation, actionOffset }
                    },
                    Children = { blinkOverlay, greetingBadge }
                }
            };
            greetingBadge.RenderTransform = greetingBadgeOffset;
            host.Show();
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            try
            {
                controller.PlayBlink(doubleBlink: false, completed: () => { });
                var blinkOpacitySamples = SampleFor(
                    () => blinkOverlay.Opacity,
                    TimeSpan.FromMilliseconds(340));
                Assert.All(blinkOpacitySamples, value => Assert.InRange(value, -0.000_001, 1.000_001));

                controller.PlayGreeting(() => { });
                var greetingSamples = SampleFor(
                    () => (greetingBadge.Opacity, actionScale.ScaleY),
                    TimeSpan.FromMilliseconds(1_140));
                Assert.All(
                    greetingSamples,
                    sample =>
                    {
                        Assert.InRange(sample.Opacity, -0.000_001, 1.000_001);
                        Assert.InRange(sample.ScaleY, 0.987_999, 1.006_001);
                    });
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Fact]
    public void SuspendFreezesAmbientProgressUntilResumeAndCompletesOnce()
    {
        RunOnStaThread(() =>
        {
            var blinkOverlay = new Border();
            var controller = new AnimationController(
                new ScaleTransform(),
                new RotateTransform(),
                new TranslateTransform(),
                new ScaleTransform(),
                new RotateTransform(),
                new ScaleTransform(),
                new RotateTransform(),
                new TranslateTransform(),
                [],
                blinkOverlay,
                new Border(),
                new TranslateTransform());
            var host = new Window { Content = blinkOverlay };
            host.Show();
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            try
            {
                var completions = 0;
                controller.PlayBlink(doubleBlink: false, () => completions++);
                var blinkClock = Assert.Single(controller.ActiveClocks);
                blinkClock.Controller!.SeekAlignedToLastTick(
                    TimeSpan.FromMilliseconds(100),
                    TimeSeekOrigin.BeginTime);

                controller.Suspend();
                WaitFor(() => blinkClock.IsPaused, TimeSpan.FromSeconds(1));
                Assert.True(controller.IsSuspended);
                Assert.True(blinkClock.IsPaused);
                Assert.Equal(0, completions);

                controller.Resume();
                WaitFor(() => !blinkClock.IsPaused, TimeSpan.FromSeconds(1));
                Assert.False(controller.IsSuspended);
                Assert.False(blinkClock.IsPaused);

                blinkClock.Controller!.SkipToFill();
                WaitFor(() => completions == 1, TimeSpan.FromSeconds(1));
                Assert.Equal(1, completions);
                Assert.Empty(controller.ActiveClocks);
            }
            finally
            {
                controller.Dispose();
                host.Close();
            }
        });
    }

    [Fact]
    public void PauseIdleThenSuspendAndResume_PreservesTheUserPause()
    {
        RunOnStaThread(() =>
        {
            var breathing = new ScaleTransform();
            var sway = new RotateTransform();
            var floating = new TranslateTransform();
            var controller = new AnimationController(
                breathing,
                sway,
                floating,
                new ScaleTransform(),
                new RotateTransform(),
                new ScaleTransform(),
                new RotateTransform(),
                new TranslateTransform(),
                []);
            var host = new Window
            {
                Content = new Grid
                {
                    RenderTransform = new TransformGroup
                    {
                        Children = { breathing, sway, floating }
                    }
                }
            };
            host.Show();
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            try
            {
                controller.StartIdle();
                var idleClocks = controller.ActiveClocks;
                Assert.Equal(4, idleClocks.Count);
                foreach (var clock in idleClocks)
                {
                    clock.Controller!.SeekAlignedToLastTick(
                        TimeSpan.FromMilliseconds(100),
                        TimeSeekOrigin.BeginTime);
                }

                controller.PauseIdle();
                WaitFor(
                    () => idleClocks.All(clock => clock.IsPaused),
                    TimeSpan.FromSeconds(1));
                Assert.True(controller.IsPaused);
                Assert.All(idleClocks, clock => Assert.True(clock.IsPaused));

                controller.Suspend();
                controller.Resume();
                host.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

                Assert.False(controller.IsSuspended);
                Assert.True(controller.IsPaused);
                Assert.All(idleClocks, clock => Assert.True(clock.IsPaused));
            }
            finally
            {
                controller.Dispose();
                host.Close();
            }
        });
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void RestartAndDisposeKeepAConstantClockBudgetAndDetachAnimations()
    {
        RunOnStaThread(() =>
        {
            var breathing = new ScaleTransform();
            var sway = new RotateTransform();
            var floating = new TranslateTransform();
            var blinkOverlay = new Border();
            var controller = new AnimationController(
                breathing,
                sway,
                floating,
                new ScaleTransform(),
                new RotateTransform(),
                new ScaleTransform(),
                new RotateTransform(),
                new TranslateTransform(),
                [],
                blinkOverlay,
                new Border(),
                new TranslateTransform());
            var host = new Window { Content = blinkOverlay };
            host.Show();
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            try
            {
                for (var iteration = 0; iteration < 50; iteration++)
                {
                    controller.StartIdle();
                }

                Assert.Equal(4, controller.ActiveClockCount);
                var completions = 0;
                controller.PlayBlink(doubleBlink: false, () => completions++);
                Assert.Equal(5, controller.ActiveClockCount);

                controller.Dispose();
                controller.Dispose();

                WaitFor(() => false, TimeSpan.FromMilliseconds(400), expectCompletion: false);
                Assert.Equal(0, completions);
                Assert.Equal(0, controller.ActiveClockCount);
                Assert.False(breathing.HasAnimatedProperties);
                Assert.False(sway.HasAnimatedProperties);
                Assert.False(floating.HasAnimatedProperties);
                Assert.False(blinkOverlay.HasAnimatedProperties);
                controller.StartIdle();
                Assert.Equal(0, controller.ActiveClockCount);
            }
            finally
            {
                controller.Dispose();
                host.Close();
            }
        });
    }

    private static void RunClickReactionScenario(
        Action<AnimationController, RotateTransform> scenario)
    {
        RunOnStaThread(() =>
        {
            var breathing = new ScaleTransform();
            var sway = new RotateTransform();
            var floating = new TranslateTransform();
            var reactionScale = new ScaleTransform();
            var reactionRotation = new RotateTransform();
            var actionScale = new ScaleTransform();
            var actionRotation = new RotateTransform();
            var actionOffset = new TranslateTransform();
            var root = new Grid
            {
                RenderTransform = new TransformGroup
                {
                    Children =
                    {
                        breathing,
                        sway,
                        floating,
                        reactionScale,
                        reactionRotation,
                        actionScale,
                        actionRotation,
                        actionOffset
                    }
                }
            };
            var host = new Window { Content = root };
            var controller = new AnimationController(
                breathing,
                sway,
                floating,
                reactionScale,
                reactionRotation,
                actionScale,
                actionRotation,
                actionOffset,
                []);
            host.Show();
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            try
            {
                scenario(controller, reactionRotation);
            }
            finally
            {
                host.Close();
            }
        });
    }

    private static void AssertNeutralAmbientBaseValues(
        FrameworkElement blinkOverlay,
        FrameworkElement greetingBadge,
        TranslateTransform greetingBadgeOffset,
        ScaleTransform actionScale,
        RotateTransform actionRotation,
        TranslateTransform actionOffset)
    {
        Assert.Equal(0, blinkOverlay.Opacity);
        Assert.Equal(0, greetingBadge.Opacity);
        Assert.Equal(0, greetingBadgeOffset.X);
        Assert.Equal(8, greetingBadgeOffset.Y);
        Assert.Equal(1, actionScale.ScaleX);
        Assert.Equal(1, actionScale.ScaleY);
        Assert.Equal(0, actionRotation.Angle);
        Assert.Equal(0, actionOffset.X);
        Assert.Equal(0, actionOffset.Y);
    }

    private static void WaitFor(Func<bool> completed, TimeSpan timeout, bool expectCompletion = true)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        var poll = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        var limit = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = timeout
        };

        poll.Tick += (_, _) =>
        {
            if (completed())
            {
                frame.Continue = false;
            }
        };
        limit.Tick += (_, _) => frame.Continue = false;
        poll.Start();
        limit.Start();
        Dispatcher.PushFrame(frame);
        poll.Stop();
        limit.Stop();

        Assert.Equal(expectCompletion, completed());
    }

    private static IReadOnlyList<T> SampleFor<T>(Func<T> sample, TimeSpan duration)
    {
        var samples = new List<T> { sample() };
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Render, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(5)
        };
        timer.Tick += (_, _) =>
        {
            samples.Add(sample());
            if (stopwatch.Elapsed >= duration)
            {
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        return samples;
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }
}
