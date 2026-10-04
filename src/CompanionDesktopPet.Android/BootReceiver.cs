using Android.App;
using Android.Content;
using Android.Runtime;

namespace CompanionDesktopPet.Android;

[BroadcastReceiver(Enabled = true, Exported = true, DirectBootAware = false)]
[IntentFilter([Intent.ActionBootCompleted])]
public sealed class BootReceiver : BroadcastReceiver
{
    public BootReceiver()
    {
    }

    public BootReceiver(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != Intent.ActionBootCompleted || !AutostartStore.IsEnabled(context))
        {
            return;
        }

        var start = new Intent(context, typeof(OverlayService));
        start.SetAction(OverlayService.ActionShow);
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            context.StartForegroundService(start);
        }
        else
        {
            context.StartService(start);
        }
    }
}
