using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CompanionDesktopPet.UI;

namespace CompanionDesktopPet.Tests;

public sealed class AnimationControllerTests
{
    [Fact]
    public void AnimationController_ExposesNoCorpusDrivenAmbientGesture()
    {
        Assert.Null(typeof(AnimationController).GetMethod("PlayAmbientGesture"));
    }

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
