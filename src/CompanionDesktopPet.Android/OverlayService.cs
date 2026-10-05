using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Android;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeSpecialUse)]
public sealed class OverlayService : Service
{
    public OverlayService()
    {
    }

    public OverlayService(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public const string ActionShow = "com.jiayi.companionpet.SHOW";
    public const string ActionSay = "com.jiayi.companionpet.SAY";
    public const string ActionPause = "com.jiayi.companionpet.PAUSE";
    public const string ActionExit = "com.jiayi.companionpet.EXIT";
    public const int NotificationId = 7;

    private WindowManagerLayoutParams? _petParams;
    private WindowManagerLayoutParams? _bubbleParams;
    private WindowManagerLayoutParams? _menuParams;
    private WindowManagerLayoutParams? _composerParams;
    private CharacterView? _character;
    private BubbleView? _bubble;
    private ControlPanel? _menu;
    private ComposerView? _composer;
    private PetSession? _session;
    private IWindowManager? _windows;
    private bool _petAdded;
    private bool _bubbleAdded;
    private bool _menuAdded;
    private bool _composerAdded;
    private string? _voicePack;
    private BubblePlacementSide _bubbleSide = BubblePlacementSide.Above;
    private bool _draggingWindow;
    private bool _dragFrameQueued;
    private int _dragX;
    private int _dragY;
    private double _composerWidthDip = 276;
    private double _composerHeightDip = 110;
    private double _bubbleWidthDip = 296;
    private double _bubbleHeightDip = 80;
    private FrameTick? _dragFrame;

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        Dip.Density = Resources?.DisplayMetrics?.Density ?? 1;
        _windows = GetSystemService(WindowService)?.JavaCast<IWindowManager>();
        CreateNotificationChannel();
        StartInForeground();
        _voicePack = VoicePackFiles.Ensure(this);
        var body = LoadBitmap("pet/character.png");
        var blink = LoadBitmap("pet/blink.png");
        var mouthMid = LoadBitmap("pet/mouth-mid.png");
        var mouthOpen = LoadBitmap("pet/mouth-open.png");
        _character = new CharacterView(this, body, blink, mouthMid, mouthOpen);
        _bubble = new BubbleView(this);
        _session = new PetSession(
            new Handler(Looper.MainLooper!),
            FilesDir?.AbsolutePath,
            () => AutostartStore.IsEnabled(this),
            SetAutostart,
            this,
            VoiceHostStore.Get(this));
        _session.WorkArea = ReadWorkArea();
        _menu = new ControlPanel(this, _session, PlaceMenu, _voicePack);
        _menu.EnsureKeyboard = FocusMenuForTyping;
        _menu.KeyboardIdle += ReleaseMenuTyping;
        Wire(_character, _bubble, _session);
        _petParams = Window(_character, Dip.Px(_session.CharacterSize), Dip.Px(_session.CharacterSize), focusable: false);
        AddPet();
        _ = RunStartAsync();
        PetAwareness.Changed += OnAwarenessChanged;
    }

    private async Task RunStartAsync()
    {
        try
        {
            if (_session is not null)
            {
                await _session.StartAsync();
            }

            _ = Task.Run(WarmCorpus);
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error("jiayi", exception.ToString());
        }
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        StartInForeground();
        switch (intent?.Action)
        {
            case ActionSay:
                _session?.ShowFromTray();
                _session?.Say();
                break;
            case ActionPause:
                _session?.TogglePause();
                RefreshNotification();
                break;
            case ActionExit:
                _session?.Exit();
                break;
            default:
                _session?.ShowFromTray();
                break;
        }

        return StartCommandResult.Sticky;
    }

    public override void OnConfigurationChanged(global::Android.Content.Res.Configuration? newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        RefreshWorkArea();
    }

    public override void OnDestroy()
    {
        PetAwareness.Changed -= OnAwarenessChanged;
        Remove(_menu, ref _menuAdded);
        Remove(_composer, ref _composerAdded);
        Remove(_bubble, ref _bubbleAdded);
        Remove(_character, ref _petAdded);
        _session?.Dispose();
        base.OnDestroy();
    }

    private void Wire(CharacterView character, BubbleView bubble, PetSession session)
    {
        character.FingerDown += session.HoldBubble;
        character.Released += session.ReleaseBubble;
        bubble.FingerDown += session.HoldBubble;
        bubble.Released += session.ReleaseBubble;
        character.Tapped += x =>
        {
            CloseMenu();
            session.Click(Dip.ToDip(x));
        };
        character.LongPressed += OpenMenu;
        character.DragStarted += () =>
        {
            CloseMenu();
            RefreshWorkArea();
            _draggingWindow = true;
            session.BeginDrag();
        };
        character.DraggedTo += (x, y) => QueueDrag(x, y);
        character.DragEnded += () =>
        {
            _draggingWindow = false;
            _dragFrameQueued = false;
            session.DragTo(_dragX / Dip.Density, _dragY / Dip.Density);
            session.EndDrag();
            if (_bubbleAdded)
            {
                PlaceBubble();
            }

            PlaceComposer();
        };
        session.Moved += (left, top) =>
        {
            if (_petParams is null || _character is null)
            {
                return;
            }

            var x = Dip.Px(left);
            var y = Dip.Px(top);
            if (_petParams.X != x || _petParams.Y != y)
            {
                _petParams.X = x;
                _petParams.Y = y;
                Update(_character, _petParams);
            }

            if (_bubbleAdded)
            {
                PlaceBubble(measure: !_draggingWindow);
            }

            if (_composerAdded)
            {
                PlaceComposer(measure: !_draggingWindow);
            }
        };
        session.Scaled += size =>
        {
            if (_petParams is null || _character is null)
            {
                return;
            }

            var px = Dip.Px(session.CharacterSize);
            _petParams.Width = px;
            _petParams.Height = px;
            Update(_character, _petParams);
            PlaceComposer();
        };
        session.BubbleRequested += text =>
        {
            if (_bubble is null)
            {
                return;
            }

            _bubble.SetText(text);
            if (!_bubbleAdded && _windows is not null)
            {
                _bubbleParams = Window(_bubble, ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, focusable: false);
                _windows.AddView(_bubble, _bubbleParams);
                _bubbleAdded = true;
                if (_menuAdded && _menu is not null && _menuParams is not null)
                {
                    Raise(_menu, _menuParams);
                }
            }

            PlaceBubble();
        };
        session.BubbleCleared += () => Remove(_bubble, ref _bubbleAdded);
        session.Clicked += positive => _character?.PlayClick(positive);
        session.DragLean += delta => _character?.SetDragLean(delta);
        session.Landing += () => _character?.PlayLanding(session.CompleteLanding);
        session.Blink += doubleBlink => _character?.PlayBlink(doubleBlink, session.CompleteBlink);
        session.Greeting += () => _character?.PlayGreeting(session.CompleteGreeting);
        session.IdleChanged += running => _character?.SetIdle(running && !session.Hidden);
        session.MenuClosed += CloseMenu;
        session.DialogueVisibilityChanged += visible =>
        {
            if (visible)
            {
                ShowComposer();
            }
            else
            {
                HideComposer();
            }
        };
        session.MouthChanged += level => _character?.SetMouth(level);
        session.VoiceCue += (mode, fraction) => _bubble?.SetVoice(mode, fraction);
        session.VisibilityChanged += visible =>
        {
            if (visible)
            {
                AddPet();
                if (session.DialogueEnabled)
                {
                    ShowComposer();
                }
            }
            else
            {
                CloseMenu();
                HideComposer();
                Remove(_bubble, ref _bubbleAdded);
                Remove(_character, ref _petAdded);
            }
        };
        session.Changed += () =>
        {
            if (_menuAdded)
            {
                _menu?.RefreshRoot();
            }

            OnAwarenessChanged();
        };
        session.Exited += () =>
        {
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
        };
    }

    private void OpenMenu()
    {
        if (_menu is null || _windows is null || _session is null || _session.Hidden)
        {
            return;
        }

        if (!_menuAdded)
        {
            _menuParams = Window(_menu, ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, focusable: false);
            _windows.AddView(_menu, _menuParams);
            _menuAdded = true;
        }
        else if (_menuParams is not null)
        {
            Raise(_menu, _menuParams);
        }

        _menu.Show(PanelPage.Root);
        PlaceMenu();
    }

    private void CloseMenu() => Remove(_menu, ref _menuAdded);

    private void ShowComposer()
    {
        if (_windows is null || _session is null || _session.Hidden)
        {
            return;
        }

        _composer ??= CreateComposer();
        if (!_composerAdded)
        {
            _composerParams = Window(_composer, Dip.Px(276), ViewGroup.LayoutParams.WrapContent, focusable: false, closeOnOutside: false);
            _composerParams.SoftInputMode = SoftInput.AdjustPan;
            _windows.AddView(_composer, _composerParams);
            _composerAdded = true;
        }

        _composerParams!.SoftInputMode = SoftInput.AdjustPan;
        PlaceComposer();
    }

    private void QueueDrag(int x, int y)
    {
        _dragX = x;
        _dragY = y;
        if (_dragFrameQueued)
        {
            return;
        }

        _dragFrameQueued = true;
        _dragFrame ??= new FrameTick(ApplyQueuedDrag);
        Choreographer.Instance?.PostFrameCallback(_dragFrame);
    }

    private void ApplyQueuedDrag()
    {
        _dragFrameQueued = false;
        if (!_draggingWindow || _session is null)
        {
            return;
        }

        _session.DragTo(_dragX / Dip.Density, _dragY / Dip.Density);
    }

    private void PlaceComposer(bool measure = true)
    {
        if (_composer is null || _composerParams is null || _session is null || !_composerAdded)
        {
            return;
        }

        var character = new ScreenRect(CurrentLeft(), CurrentTop(), _session.CharacterSize, _session.CharacterSize);
        ScreenSize panel;
        if (measure)
        {
            var placementGuess = DialoguePlacementService.Place(character, new ScreenSize(320, 110), _session.WorkArea);
            _composer.SetSide(placementGuess.Side);
            _composer.Measure(
                View.MeasureSpec.MakeMeasureSpec(Dip.Px(360), MeasureSpecMode.AtMost),
                View.MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified));
            panel = new ScreenSize(
                Math.Max(276, Dip.ToDip(_composer.MeasuredWidth)),
                Math.Max(72, Dip.ToDip(_composer.MeasuredHeight)));
            _composerWidthDip = panel.Width;
            _composerHeightDip = panel.Height;
        }
        else
        {
            panel = new ScreenSize(_composerWidthDip, _composerHeightDip);
        }

        var placement = DialoguePlacementService.Place(character, panel, _session.WorkArea);
        _composer.SetSide(placement.Side);
        var x = Dip.Px(placement.Origin.X);
        var y = Dip.Px(placement.Origin.Y);
        var width = Dip.Px(panel.Width);
        var height = Dip.Px(panel.Height) + Dip.Px(8);
        if (_composerParams.X == x && _composerParams.Y == y && _composerParams.Width == width && _composerParams.Height == height)
        {
            return;
        }

        _composerParams.X = x;
        _composerParams.Y = y;
        _composerParams.Width = width;
        _composerParams.Height = height;
        Update(_composer, _composerParams);
    }

    private void HideComposer()
    {
        ReleaseComposer();
        Remove(_composer, ref _composerAdded);
    }

    private ComposerView CreateComposer()
    {
        var composer = new ComposerView(this, text => _session?.SubmitDialogue(text));
        composer.Editing += FocusComposer;
        composer.Finished += ReleaseComposer;
        composer.Touch += (_, args) =>
        {
            if (args.Event?.Action != MotionEventActions.Outside)
            {
                return;
            }

            ReleaseComposer();
            args.Handled = true;
        };
        return composer;
    }

    private void FocusComposer()
    {
        if (_composer is null || _composerParams is null || !_composerAdded)
        {
            return;
        }

        _composerParams.Flags &= ~WindowManagerFlags.NotFocusable;
        _composerParams.Flags |= WindowManagerFlags.WatchOutsideTouch;
        _composerParams.SoftInputMode = SoftInput.AdjustPan;
        Update(_composer, _composerParams);
        _composer.Post(() => _composer.TakeInput());
    }

    private void ReleaseComposer()
    {
        if (_composer is null || _composerParams is null || !_composerAdded)
        {
            return;
        }

        _composer.ReleaseInput();
        _composerParams.Flags |= WindowManagerFlags.NotFocusable;
        _composerParams.Flags &= ~WindowManagerFlags.WatchOutsideTouch;
        Update(_composer, _composerParams);
    }

    private void FocusMenuForTyping()
    {
        if (_menu is null || _menuParams is null || !_menuAdded)
        {
            return;
        }

        _menuParams.Flags &= ~WindowManagerFlags.NotFocusable;
        Update(_menu, _menuParams);
    }

    private void ReleaseMenuTyping()
    {
        if (_menu is null || _menuParams is null || !_menuAdded)
        {
            return;
        }

        var manager = (global::Android.Views.InputMethods.InputMethodManager?)GetSystemService(InputMethodService);
        manager?.HideSoftInputFromWindow(_menu.WindowToken, global::Android.Views.InputMethods.HideSoftInputFlags.None);
        _menuParams.Flags |= WindowManagerFlags.NotFocusable;
        Update(_menu, _menuParams);
    }

    private void PlaceMenu()
    {
        if (_menu is null || _menuParams is null || _session is null || !_menuAdded)
        {
            return;
        }

        var width = Dip.Px(318);
        _menu.Measure(
            View.MeasureSpec.MakeMeasureSpec(width, MeasureSpecMode.AtMost),
            View.MeasureSpec.MakeMeasureSpec(Dip.Px(_session.WorkArea.Height - 48), MeasureSpecMode.AtMost));
        var menuWidth = Dip.ToDip(_menu.MeasuredWidth);
        var menuHeight = Dip.ToDip(_menu.MeasuredHeight);
        var placement = ControlMenuPlacementService.Place(
            new ScreenRect(_session.WorkArea.Left, 0, 0, 0),
            new ScreenSize(menuWidth, menuHeight),
            _session.WorkArea);
        var character = new ScreenRect(CurrentLeft(), CurrentTop(), _session.CharacterSize, _session.CharacterSize);
        placement = ControlMenuPlacementService.Place(character, new ScreenSize(menuWidth, menuHeight), _session.WorkArea);
        _menuParams.X = Dip.Px(placement.Origin.X);
        _menuParams.Y = Dip.Px(placement.Origin.Y);
        _menuParams.Width = Dip.Px(menuWidth);
        _menuParams.Height = Dip.Px(Math.Min(menuHeight, _session.WorkArea.Height - 48));
        Update(_menu, _menuParams);
    }

    private void PlaceBubble(bool measure = true)
    {
        if (_bubble is null || _bubbleParams is null || _session is null)
        {
            return;
        }

        ScreenSize bubble;
        if (measure || _bubbleHeightDip <= 0)
        {
            var width = Dip.Px(296);
            _bubble.Measure(
                View.MeasureSpec.MakeMeasureSpec(width, MeasureSpecMode.Exactly),
                View.MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified));
            bubble = new ScreenSize(Dip.ToDip(_bubble.MeasuredWidth), Dip.ToDip(_bubble.MeasuredHeight));
            _bubbleWidthDip = bubble.Width;
            _bubbleHeightDip = bubble.Height;
        }
        else
        {
            bubble = new ScreenSize(_bubbleWidthDip, _bubbleHeightDip);
        }

        var character = new ScreenRect(CurrentLeft(), CurrentTop(), _session.CharacterSize, _session.CharacterSize);
        var placement = BubblePlacementService.Place(character, bubble, _session.WorkArea, _bubbleSide);
        _bubbleSide = placement.Side;
        _bubble.PlaceArrow(placement.Side, placement.ArrowCenterX);
        var x = Dip.Px(placement.Origin.X);
        var y = Dip.Px(placement.Origin.Y);
        var bubbleWidth = Dip.Px(bubble.Width);
        var bubbleHeight = Dip.Px(bubble.Height) + Dip.Px(10);
        if (_bubbleParams.X == x && _bubbleParams.Y == y && _bubbleParams.Width == bubbleWidth && _bubbleParams.Height == bubbleHeight)
        {
            return;
        }

        _bubbleParams.X = x;
        _bubbleParams.Y = y;
        _bubbleParams.Width = bubbleWidth;
        _bubbleParams.Height = bubbleHeight;
        Update(_bubble, _bubbleParams);
    }

    private double CurrentLeft() => _petParams is null ? 0 : Dip.ToDip(_petParams.X);

    private double CurrentTop() => _petParams is null ? 0 : Dip.ToDip(_petParams.Y);

    private void AddPet()
    {
        if (_petAdded || _character is null || _petParams is null || _windows is null)
        {
            return;
        }

        try
        {
            _windows.AddView(_character, _petParams);
            _petAdded = true;
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error("jiayi", exception.ToString());
        }
    }

    private void OnAwarenessChanged()
    {
        _session?.RefreshOcclusion(PetAwareness.CoveringHome || !PetAwareness.ServiceRunning);
    }

    private void SetAutostart(bool enabled)
    {
        AutostartStore.SetEnabled(this, enabled);
        if (!enabled)
        {
            return;
        }

        var power = GetSystemService(PowerService) as PowerManager;
        if (power is not null && !power.IsIgnoringBatteryOptimizations(PackageName))
        {
            var intent = new Intent(global::Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations);
            intent.SetData(global::Android.Net.Uri.Parse("package:" + PackageName));
            intent.AddFlags(ActivityFlags.NewTask);
            StartActivity(intent);
        }
    }

    private void RefreshWorkArea()
    {
        if (_session is null)
        {
            return;
        }

        _session.SetWorkArea(ReadWorkArea());
    }

    private ScreenRect ReadWorkArea()
    {
        var width = 0;
        var height = 0;
        if (_windows?.DefaultDisplay is { } display)
        {
            var real = new global::Android.Util.DisplayMetrics();
            display.GetRealMetrics(real);
            width = real.WidthPixels;
            height = real.HeightPixels;
        }

        if (width <= 0 || height <= 0)
        {
            var metrics = Resources?.DisplayMetrics;
            width = metrics?.WidthPixels ?? 1080;
            height = metrics?.HeightPixels ?? 1920;
        }

        var top = SystemDimen("status_bar_height");
        var bottom = SystemDimen("navigation_bar_height");
        return new ScreenRect(0, Dip.ToDip(top), Dip.ToDip(width), Dip.ToDip(Math.Max(1, height - top - bottom)));
    }

    private int SystemDimen(string name)
    {
        var id = Resources?.GetIdentifier(name, "dimen", "android") ?? 0;
        return id == 0 || Resources is null ? 0 : Resources.GetDimensionPixelSize(id);
    }

    private static void WarmCorpus()
    {
        try
        {
            _ = CorpusBrowserIndex.Outline;
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error("jiayi", exception.ToString());
        }
    }

    private void Raise(View view, WindowManagerLayoutParams parameters)
    {
        if (_windows is null)
        {
            return;
        }

        try
        {
            _windows.RemoveView(view);
            _windows.AddView(view, parameters);
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error("jiayi", exception.ToString());
        }
    }

    private Bitmap LoadBitmap(string asset)
    {
        using var stream = Assets!.Open(asset);
        return BitmapFactory.DecodeStream(stream)
            ?? throw new InvalidOperationException($"Missing pet image {asset}.");
    }

    private WindowManagerLayoutParams Window(View view, int width, int height, bool focusable, bool closeOnOutside = true)
    {
        var flags = WindowManagerFlags.NotTouchModal
            | WindowManagerFlags.LayoutInScreen
            | WindowManagerFlags.HardwareAccelerated;
        if (!focusable)
        {
            flags |= WindowManagerFlags.NotFocusable;
        }

        if (closeOnOutside)
        {
            flags |= WindowManagerFlags.WatchOutsideTouch;
            view.Touch += (_, args) =>
            {
                if (args.Event?.Action != MotionEventActions.Outside)
                {
                    return;
                }

                if (view is ControlPanel panel && panel.HoldsKeyboard)
                {
                    ReleaseMenuTyping();
                    args.Handled = true;
                    return;
                }

                CloseMenu();
                args.Handled = true;
            };
        }

        return new WindowManagerLayoutParams(
            width,
            height,
            WindowManagerTypes.ApplicationOverlay,
            flags,
            Format.Translucent)
        {
            Gravity = GravityFlags.Top | GravityFlags.Left,
            X = Dip.Px(24),
            Y = Dip.Px(24)
        };
    }

    private void Update(View? view, WindowManagerLayoutParams? parameters)
    {
        if (view is null || parameters is null || _windows is null)
        {
            return;
        }

        try
        {
            _windows.UpdateViewLayout(view, parameters);
        }
        catch (Exception exception) when (exception is Java.Lang.IllegalArgumentException)
        {
        }
    }

    private void Remove(View? view, ref bool added)
    {
        if (!added || view is null || _windows is null)
        {
            added = false;
            return;
        }

        try
        {
            _windows.RemoveView(view);
        }
        catch (Exception exception) when (exception is Java.Lang.IllegalArgumentException)
        {
        }

        added = false;
    }

    private void StartInForeground()
    {
        var notification = BuildNotification();
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeSpecialUse);
        }
        else
        {
            StartForeground(NotificationId, notification);
        }
    }

    private void RefreshNotification()
    {
        var manager = GetSystemService(NotificationService) as NotificationManager;
        manager?.Notify(NotificationId, BuildNotification());
    }

    private Notification BuildNotification()
    {
        var text = _session is { Hidden: true } ? "佳怡收起来了，点一下把她叫回来。" : "佳怡在屏幕上。";
        var builder = new Notification.Builder(this, "jiayi")
            .SetContentTitle("佳怡")
            .SetContentText(text)
            .SetSmallIcon(Resource.Drawable.ic_stat_jiayi)
            .SetLargeIcon(BitmapFactory.DecodeResource(Resources, Resource.Mipmap.ic_launcher))
            .SetOngoing(true)
            .SetContentIntent(Pending(ActionShow))
            .AddAction(Resource.Drawable.ic_stat_jiayi, "说句话", Pending(ActionSay))
            .AddAction(Resource.Drawable.ic_stat_jiayi, _session is { Paused: true } ? "继续" : "暂停", Pending(ActionPause))
            .AddAction(Resource.Drawable.ic_stat_jiayi, "退出", Pending(ActionExit));
        return builder.Build();
    }

    private PendingIntent Pending(string action)
    {
        var intent = new Intent(this, typeof(OverlayService));
        intent.SetAction(action);
        var flags = PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable;
        return PendingIntent.GetService(this, action.GetHashCode(), intent, flags)!;
    }

    private void CreateNotificationChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        var channel = new NotificationChannel("jiayi", "佳怡", NotificationImportance.Low)
        {
            Description = "桌宠浮窗还在。"
        };
        var manager = GetSystemService(NotificationService) as NotificationManager;
        manager?.CreateNotificationChannel(channel);
    }

    private sealed class FrameTick : Java.Lang.Object, Choreographer.IFrameCallback
    {
        private readonly Action _action;

        public FrameTick(Action action) => _action = action;

        public FrameTick(IntPtr handle, JniHandleOwnership transfer)
            : base(handle, transfer)
        {
            _action = static () => { };
        }

        public void DoFrame(long frameTimeNanos) => _action();
    }
}

internal static class AutostartStore
{
    private const string FileName = "autostart.flag";

    public static bool IsEnabled(Context context)
    {
        var path = System.IO.Path.Combine(context.FilesDir!.AbsolutePath, FileName);
        return File.Exists(path);
    }

    public static void SetEnabled(Context context, bool enabled)
    {
        var path = System.IO.Path.Combine(context.FilesDir!.AbsolutePath, FileName);
        if (enabled)
        {
            File.WriteAllText(path, "1");
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

internal static class VoicePackFiles
{
    public static string? Ensure(Context context)
    {
        var names = context.Assets?.List("voice/refs");
        if (names is null || names.Length == 0)
        {
            return null;
        }

        var root = System.IO.Path.Combine(context.FilesDir!.AbsolutePath, "voice", "packs", "jiayi");
        var refs = System.IO.Path.Combine(root, "refs");
        Directory.CreateDirectory(refs);
        Copy(context, "voice/manifest.json", System.IO.Path.Combine(root, "manifest.json"));
        foreach (var name in names)
        {
            Copy(context, "voice/refs/" + name, System.IO.Path.Combine(refs, name));
        }

        return root;
    }

    private static void Copy(Context context, string asset, string destination)
    {
        if (File.Exists(destination) && new FileInfo(destination).Length > 0)
        {
            return;
        }

        using var input = context.Assets!.Open(asset);
        using var output = File.Create(destination);
        input.CopyTo(output);
    }
}
