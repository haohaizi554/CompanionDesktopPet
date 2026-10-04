using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Runtime;
using Android.Views.Accessibility;

namespace CompanionDesktopPet.Android;

internal static class PetAwareness
{
    public static bool ServiceRunning { get; private set; }

    public static bool? Fullscreen { get; private set; }

    public static bool CoveringHome { get; private set; } = true;

    public static event System.Action? Changed;

    public static void Update(bool running, bool? fullscreen, bool coveringHome)
    {
        ServiceRunning = running;
        Fullscreen = fullscreen;
        CoveringHome = coveringHome;
        Changed?.Invoke();
    }
}

[Service(Exported = false, Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE", Label = "佳怡")]
[IntentFilter(["android.accessibilityservice.AccessibilityService"])]
[MetaData("android.accessibilityservice", Resource = "@xml/pet_awareness")]
public sealed class PetAwarenessService : AccessibilityService
{
    private string? _homePackage;

    public PetAwarenessService()
    {
    }

    public PetAwarenessService(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public override void OnAccessibilityEvent(AccessibilityEvent? e)
    {
        if (e?.EventType != EventTypes.WindowStateChanged)
        {
            return;
        }

        var packageName = e.PackageName?.ToString();
        _homePackage ??= HomePackage();
        var ours = string.Equals(packageName, PackageName, StringComparison.Ordinal);
        var home = string.Equals(packageName, _homePackage, StringComparison.Ordinal);
        var fullscreen = false;
        if (!ours && !home && e.Source is not null)
        {
            var bounds = new global::Android.Graphics.Rect();
            e.Source.GetBoundsInScreen(bounds);
            var metrics = Resources?.DisplayMetrics;
            var width = metrics?.WidthPixels ?? 0;
            var height = metrics?.HeightPixels ?? 0;
            fullscreen = width > 0
                && height > 0
                && bounds.Width() >= width - 2
                && bounds.Height() >= height - 2;
        }

        PetAwareness.Update(true, fullscreen ? true : null, ours || home || string.IsNullOrEmpty(packageName));
    }

    public override void OnInterrupt()
    {
    }

    protected override void OnServiceConnected()
    {
        base.OnServiceConnected();
        PetAwareness.Update(true, null, true);
    }

    public override void OnDestroy()
    {
        PetAwareness.Update(false, null, true);
        base.OnDestroy();
    }

    private string? HomePackage()
    {
        var intent = new Intent(Intent.ActionMain);
        intent.AddCategory(Intent.CategoryHome);
        return PackageManager?.ResolveActivity(intent, PackageInfoFlags.MatchDefaultOnly)?.ActivityInfo?.PackageName;
    }
}
