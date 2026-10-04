using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Views;
using Android.Widget;

namespace CompanionDesktopPet.Android;

[Activity(Label = "佳怡", MainLauncher = true, Exported = true, Theme = "@android:style/Theme.Material.Light.NoActionBar", ScreenOrientation = ScreenOrientation.Portrait)]
public sealed class MainActivity : Activity
{
    private const int NotificationRequest = 21;

    public MainActivity()
    {
    }

    public MainActivity(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            RequestPermissions([global::Android.Manifest.Permission.PostNotifications], NotificationRequest);
        }

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(Dip.Px(28), Dip.Px(48), Dip.Px(28), Dip.Px(28));
        root.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#FFFFF8EE"));
        var icon = new ImageView(this);
        icon.SetImageResource(Resource.Mipmap.ic_launcher);
        icon.LayoutParameters = new LinearLayout.LayoutParams(Dip.Px(96), Dip.Px(96))
        {
            BottomMargin = Dip.Px(16)
        };
        var title = new TextView(this) { Text = "佳怡", TextSize = 28 };
        title.SetTextColor(global::Android.Graphics.Color.ParseColor("#FF543A3F"));
        var body = new TextView(this)
        {
            Text = "她会浮在别的应用上面。先允许「显示在其他应用上层」。通知用来把收起来的她叫回来。\n\n全屏时少说话、关掉置顶后让开，需要再打开「佳怡」无障碍服务。那个服务只看窗口有没有铺满，以及你在不在桌面。",
            TextSize = 15
        };
        body.SetTextColor(global::Android.Graphics.Color.ParseColor("#FF543A3F"));
        body.SetPadding(0, Dip.Px(16), 0, Dip.Px(12));
        var hostLabel = new TextView(this)
        {
            Text = "语音和对话都连这台电脑，模型和密钥已经填好。平板和电脑要在同一个 Wi-Fi。\n语音 " + LinkDefaults.VoiceHost + "\n对话 " + LinkDefaults.DialogueHost + "\n模型 " + LinkDefaults.ModelName,
            TextSize = 14
        };
        hostLabel.SetTextColor(global::Android.Graphics.Color.ParseColor("#FF543A3F"));
        var host = new EditText(this)
        {
            Text = VoiceHostStore.Get(this),
            Hint = VoiceHostStore.DefaultHost
        };
        host.SetSingleLine(true);
        var appear = new Button(this) { Text = "让佳怡出来" };
        appear.Click += (_, _) =>
        {
            VoiceHostStore.Set(this, host.Text ?? "");
            EnsureOverlay();
        };
        var awareness = new Button(this) { Text = "打开全屏和让开" };
        awareness.Click += (_, _) =>
        {
            StartActivity(new Intent(Settings.ActionAccessibilitySettings));
        };
        root.AddView(icon);
        root.AddView(title);
        root.AddView(body);
        root.AddView(hostLabel);
        root.AddView(host);
        root.AddView(appear);
        root.AddView(awareness);
        SetContentView(root);
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (Settings.CanDrawOverlays(this))
        {
            StartPet();
        }
    }

    private void EnsureOverlay()
    {
        if (Settings.CanDrawOverlays(this))
        {
            StartPet();
            return;
        }

        var intent = new Intent(Settings.ActionManageOverlayPermission);
        intent.SetData(global::Android.Net.Uri.Parse("package:" + PackageName));
        StartActivity(intent);
    }

    private void StartPet()
    {
        var intent = new Intent(this, typeof(OverlayService));
        intent.SetAction(global::CompanionDesktopPet.Android.OverlayService.ActionShow);
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            StartForegroundService(intent);
        }
        else
        {
            StartService(intent);
        }

        Finish();
    }
}

[Application(Label = "佳怡", AllowBackup = false, Icon = "@mipmap/ic_launcher", RoundIcon = "@mipmap/ic_launcher", UsesCleartextTraffic = true)]
public sealed class PetApplication : Application
{
    static PetApplication()
    {
        AppContext.SetSwitch("System.Net.Http.UseSocketsHttpHandler", true);
    }

    public PetApplication(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }
}
