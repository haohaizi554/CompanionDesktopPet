using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
