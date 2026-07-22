using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Media;
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
            var controller = new AnimationController(
                breathing, sway, floating, reactionScale, reactionRotation);

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
