using Android.Animation;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Runtime;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Android;

internal static class Dip
{
    public static float Density { get; set; } = 1;

    public static int Px(double dip) => (int)Math.Round(dip * Density);

    public static double ToDip(float px) => px / Density;
}

internal static class PetColors
{
    public static Color Bubble { get; } = Color.ParseColor("#FFFFF8EE");
    public static Color Arrow { get; } = Color.ParseColor("#FFFFD6DF");
    public static Color Accent { get; } = Color.ParseColor("#FFE98FA4");
    public static Color Text { get; } = Color.ParseColor("#FF543A3F");
    public static Color Disabled { get; } = Color.ParseColor("#FF8A7479");
    public static Color Checked { get; } = Color.ParseColor("#FFBE4B70");
    public static Color MenuStart { get; } = Color.ParseColor("#FAFFFDF7");
    public static Color MenuEnd { get; } = Color.ParseColor("#E8FFE0EA");
    public static Color MenuBorder { get; } = Color.ParseColor("#B8E56F91");
}

internal sealed class CharacterView : FrameLayout
{
    private readonly ImageView _body;
    private readonly ImageView _blink;
    private readonly ImageView _mouthMid;
    private readonly ImageView _mouthOpen;
    private readonly TextView _greeting;
    private readonly TextView[] _hearts;
    private readonly GestureDetector _gestures;
    private ValueAnimator? _breath;
    private ValueAnimator? _sway;
    private ValueAnimator? _float;
    private bool _dragging;
    private bool _dragAnnounced;
    private float _downRawX;
    private float _downRawY;
    private float _grabOffsetX;
    private float _grabOffsetY;
    private float _lean;
    private bool _idle;

    public CharacterView(Context context, Bitmap body, Bitmap? blink, Bitmap? mouthMid, Bitmap? mouthOpen) : base(context)
    {
        SetBackgroundColor(Color.Transparent);
        SetClipChildren(false);
        _body = new ImageView(context);
        _body.SetImageBitmap(body);
        _body.SetScaleType(ImageView.ScaleType.FitCenter);
        AddView(_body, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        _blink = new ImageView(context);
        if (blink is not null)
        {
            _blink.SetImageBitmap(blink);
        }

        _blink.SetScaleType(ImageView.ScaleType.FitCenter);
        _blink.Alpha = 0;
        AddView(_blink, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        _mouthMid = MouthLayer(context, mouthMid);
        _mouthOpen = MouthLayer(context, mouthOpen);
        AddView(_mouthMid, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        AddView(_mouthOpen, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        _greeting = new TextView(context)
        {
            Text = "嗨♡",
            TextSize = 22,
            Gravity = GravityFlags.Center
        };
        _greeting.SetTextColor(PetColors.Checked);
        _greeting.Alpha = 0;
        var greetingParams = new LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Top | GravityFlags.CenterHorizontal);
        greetingParams.TopMargin = Dip.Px(28);
        AddView(_greeting, greetingParams);
        _hearts = new TextView[3];
        for (var index = 0; index < _hearts.Length; index++)
        {
            var heart = new TextView(context) { Text = "♡", TextSize = 18 };
            heart.SetTextColor(index switch
            {
                0 => Color.ParseColor("#FFFF6F91"),
                1 => Color.ParseColor("#FFFF9BB2"),
                _ => Color.ParseColor("#FFFFB3C6")
            });
            heart.Alpha = 0;
            _hearts[index] = heart;
            AddView(heart, new LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
        }

        _gestures = new GestureDetector(context, new Gestures(this));
        Clickable = true;
    }

    public event Action<float>? Tapped;
    public event Action? LongPressed;
    public event Action? DragStarted;
    public event Action<int, int>? DraggedTo;
    public event Action? DragEnded;
    public event Action? FingerDown;
    public event Action? Released;

    public void SetIdle(bool running)
    {
        if (running == _idle)
        {
            return;
        }

        _idle = running;
        if (!running)
        {
            _breath?.Cancel();
            _sway?.Cancel();
            _float?.Cancel();
            _body.ScaleX = 1;
            _body.ScaleY = 1;
            _body.Rotation = 0;
            _body.TranslationY = 0;
            _blink.ScaleX = 1;
            _blink.ScaleY = 1;
            _blink.Rotation = 0;
            _blink.TranslationY = 0;
            PoseMouth(1, 1, 0, 0);
            return;
        }

        _breath = Pulse(0.985f, 1.015f, 4000, value =>
        {
            _body.ScaleX = value;
            _body.ScaleY = value;
            _blink.ScaleX = value;
            _blink.ScaleY = value;
            _mouthMid.ScaleX = value;
            _mouthMid.ScaleY = value;
            _mouthOpen.ScaleX = value;
            _mouthOpen.ScaleY = value;
        });
        _sway = Pulse(-1.2f, 1.2f, 6000, value =>
        {
            _body.Rotation = value;
            _blink.Rotation = value;
            _mouthMid.Rotation = value;
            _mouthOpen.Rotation = value;
        });
        var travel = Dip.Px(3);
        _float = Pulse(-travel, travel, 5000, value =>
        {
            _body.TranslationY = value;
            _blink.TranslationY = value;
            _mouthMid.TranslationY = value;
            _mouthOpen.TranslationY = value;
        });
    }

    public void SetMouth(byte level)
    {
        _mouthMid.Alpha = level == 1 ? 1 : 0;
        _mouthOpen.Alpha = level == 2 ? 1 : 0;
    }

    public void PlayClick(bool tiltPositive)
    {
        AnimateScale(_body, 1.06f, 0.94f);
        AnimateScale(_mouthMid, 1.06f, 0.94f);
        AnimateScale(_mouthOpen, 1.06f, 0.94f);
        var angle = tiltPositive ? 2.2f : -2.2f;
        _body.Animate()?.RotationBy(0)?.SetDuration(0);
        _body.Rotation = 0;
        _body.Animate()?.Rotation(angle)?.SetDuration(180)?.WithEndAction(new Runnable(() =>
            _body.Animate()?.Rotation(0)?.SetDuration(220)?.Start()))?.Start();
        for (var index = 0; index < _hearts.Length; index++)
        {
            var heart = _hearts[index];
            heart.TranslationX = (Width / 2f) - Dip.Px(8);
            heart.TranslationY = Height * 0.42f;
            heart.Alpha = 0;
            var lift = Dip.Px(45 + (index * 7));
            heart.Animate()?.Alpha(1)?.TranslationYBy(-lift)?.SetStartDelay(index * 55)?.SetDuration(620)
                ?.WithEndAction(new Runnable(() => heart.Alpha = 0))?.Start();
        }
    }

    public void SetDragLean(double horizontalDeltaDips)
    {
        if (horizontalDeltaDips == 0)
        {
            _lean = 0;
        }
        else
        {
            var target = (float)Math.Clamp(horizontalDeltaDips * 0.08, -4, 4);
            _lean += (target - _lean) * 0.4f;
        }

        _body.Rotation = _lean;
        _blink.Rotation = _lean;
        _mouthMid.Rotation = _lean;
        _mouthOpen.Rotation = _lean;
    }

    public void PlayLanding(Action completed)
    {
        _body.Animate()?.Rotation(0)?.SetDuration(520)?.Start();
        _body.Animate()?.ScaleY(0.965f)?.SetDuration(150)?.WithEndAction(new Runnable(() =>
        {
            _body.Animate()?.ScaleY(1f)?.SetDuration(370)?.WithEndAction(new Runnable(completed))?.Start();
        }))?.Start();
    }

    public void PlayBlink(bool doubleBlink, Action completed)
    {
        _blink.Alpha = 1;
        PostDelayed(() =>
        {
            _blink.Alpha = 0;
            if (!doubleBlink)
            {
                completed();
                return;
            }

            PostDelayed(() =>
            {
                _blink.Alpha = 1;
                PostDelayed(() =>
                {
                    _blink.Alpha = 0;
                    completed();
                }, 50);
            }, 80);
        }, 55);
    }

    public void PlayGreeting(Action completed)
    {
        _greeting.Alpha = 0;
        _greeting.TranslationY = Dip.Px(8);
        _greeting.Animate()?.Alpha(1)?.TranslationY(0)?.SetDuration(180)?.WithEndAction(new Runnable(() =>
        {
            _greeting.Animate()?.Alpha(0)?.TranslationY(-Dip.Px(20))?.SetStartDelay(400)?.SetDuration(320)
                ?.WithEndAction(new Runnable(completed))?.Start();
        }))?.Start();
        _body.Animate()?.Rotation(-2.2f)?.SetDuration(360)?.WithEndAction(new Runnable(() =>
            _body.Animate()?.Rotation(0)?.SetDuration(740)?.Start()))?.Start();
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null)
        {
            return false;
        }

        var action = e.ActionMasked;
        if (action == MotionEventActions.Down)
        {
            var location = new int[2];
            GetLocationOnScreen(location);
            _downRawX = e.RawX;
            _downRawY = e.RawY;
            _grabOffsetX = e.RawX - location[0];
            _grabOffsetY = e.RawY - location[1];
        }
        else if (action == MotionEventActions.Move)
        {
            var travel = MathF.Abs(e.RawX - _downRawX) + MathF.Abs(e.RawY - _downRawY);
            if (!_dragAnnounced && Dip.ToDip(travel) >= 8)
            {
                _dragging = true;
                _dragAnnounced = true;
                DragStarted?.Invoke();
            }

            if (_dragAnnounced)
            {
                DraggedTo?.Invoke(
                    (int)MathF.Round(e.RawX - _grabOffsetX),
                    (int)MathF.Round(e.RawY - _grabOffsetY));
            }
        }

        _gestures.OnTouchEvent(e);
        if (action is MotionEventActions.Up or MotionEventActions.Cancel)
        {
            if (_dragging)
            {
                _dragging = false;
                _dragAnnounced = false;
                DragEnded?.Invoke();
            }

            Released?.Invoke();
        }

        return true;
    }

    private void OnDown()
    {
        _dragging = false;
        _dragAnnounced = false;
        _lean = 0;
        FingerDown?.Invoke();
    }

    private void OnTap(float x) => Tapped?.Invoke(x);

    private void OnLongPress()
    {
        if (_dragging)
        {
            return;
        }

        LongPressed?.Invoke();
    }

    private static ValueAnimator Pulse(float from, float to, long duration, Action<float> apply)
    {
        var animator = ValueAnimator.OfFloat(from, to)!;
        animator.SetDuration(duration);
        animator.RepeatMode = ValueAnimatorRepeatMode.Reverse;
        animator.RepeatCount = ValueAnimator.Infinite;
        animator.Update += (_, args) => apply((float)(args.Animation?.AnimatedValue ?? from));
        animator.Start();
        return animator;
    }

    private static ImageView MouthLayer(Context context, Bitmap? bitmap)
    {
        var view = new ImageView(context);
        if (bitmap is not null)
        {
            view.SetImageBitmap(bitmap);
        }

        view.SetScaleType(ImageView.ScaleType.FitCenter);
        view.Alpha = 0;
        view.Clickable = false;
        return view;
    }

    private void PoseMouth(float scaleX, float scaleY, float rotation, float translationY)
    {
        foreach (var view in new[] { _mouthMid, _mouthOpen })
        {
            view.ScaleX = scaleX;
            view.ScaleY = scaleY;
            view.Rotation = rotation;
            view.TranslationY = translationY;
        }
    }

    private static void AnimateScale(View view, float x, float y)
    {
        view.Animate()?.ScaleX(x)?.ScaleY(y)?.SetDuration(140)?.WithEndAction(new Runnable(() =>
            view.Animate()?.ScaleX(1)?.ScaleY(1)?.SetDuration(180)?.Start()))?.Start();
    }

    private sealed class Gestures : GestureDetector.SimpleOnGestureListener
    {
        private readonly CharacterView _owner;

        public Gestures(CharacterView owner) => _owner = owner;

        public override bool OnDown(MotionEvent e)
        {
            _owner.OnDown();
            return true;
        }

        public override bool OnScroll(MotionEvent? e1, MotionEvent e2, float distanceX, float distanceY) => true;

        public override bool OnSingleTapUp(MotionEvent e)
        {
            if (!_owner._dragAnnounced)
            {
                _owner.OnTap(e.GetX());
            }

            return true;
        }

        public override void OnLongPress(MotionEvent e) => _owner.OnLongPress();
    }

    private sealed class Runnable : Java.Lang.Object, Java.Lang.IRunnable
    {
        private readonly Action _action;

        public Runnable(Action action) => _action = action;

        public void Run() => _action();
    }
}

internal sealed class BubbleView : LinearLayout
{
    private readonly PetArrowView _arrow;
    private readonly VoiceRingView _ring;
    private readonly TextView _text;

    public BubbleView(Context context) : base(context)
    {
        Orientation = Orientation.Vertical;
        SetBackgroundColor(Color.Transparent);
        SetPadding(Dip.Px(10), Dip.Px(10), Dip.Px(10), Dip.Px(10));
        _arrow = new PetArrowView(context) { Direction = PetArrowDirection.Down };
        _ring = new VoiceRingView(context);
        var card = new TextView(context)
        {
            Gravity = GravityFlags.Center,
            TextSize = 15
        };
        card.SetTextColor(PetColors.Text);
        card.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        card.Gravity = GravityFlags.CenterVertical;
        card.SetIncludeFontPadding(true);
        card.SetPadding(0, Dip.Px(2), 0, Dip.Px(8));
        card.SetLineSpacing(Dip.Px(2), 1f);
        var row = new LinearLayout(context) { Orientation = global::Android.Widget.Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(Dip.Px(18), Dip.Px(13), Dip.Px(18), Dip.Px(13));
        var background = new GradientDrawable();
        background.SetColor(PetColors.Bubble);
        background.SetCornerRadius(Dip.Px(25));
        background.SetStroke(Dip.Px(2), PetColors.Accent);
        row.Background = background;
        row.Elevation = Dip.Px(3);
        var ringParams = new LayoutParams(Dip.Px(34), Dip.Px(34));
        ringParams.RightMargin = Dip.Px(12);
        row.AddView(_ring, ringParams);
        row.AddView(card, new LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        _text = card;
        AddView(_arrow, new LayoutParams(Dip.Px(20), Dip.Px(12)) { Gravity = GravityFlags.CenterHorizontal });
        AddView(row, new LayoutParams(Dip.Px(276), ViewGroup.LayoutParams.WrapContent));
    }

    public void SetText(string text) => _text.Text = text;

    public void SetVoice(int mode, float fraction)
    {
        if (mode == 1)
        {
            _ring.ShowWaiting();
        }
        else if (mode == 2)
        {
            _ring.ShowProgress(fraction);
        }
        else
        {
            _ring.HideRing();
        }
    }

    private BubblePlacementSide? _arrowSide;
    private int _arrowMargin = int.MinValue;

    public void PlaceArrow(BubblePlacementSide side, double centerX)
    {
        var margin = (int)Math.Clamp(Dip.Px(centerX) - Dip.Px(10), 0, Dip.Px(256));
        if (_arrowSide == side && _arrowMargin == margin)
        {
            return;
        }

        _arrowSide = side;
        _arrowMargin = margin;
        _arrow.Direction = side == BubblePlacementSide.Below ? PetArrowDirection.Up : PetArrowDirection.Down;
        RemoveView(_arrow);
        var parameters = new LayoutParams(Dip.Px(20), Dip.Px(12));
        parameters.LeftMargin = margin;
        if (side == BubblePlacementSide.Below)
        {
            AddView(_arrow, 0, parameters);
        }
        else
        {
            AddView(_arrow, parameters);
        }
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null)
        {
            return false;
        }

        if (e.ActionMasked == MotionEventActions.Down)
        {
            FingerDown?.Invoke();
        }
        else if (e.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel)
        {
            Released?.Invoke();
        }

        return true;
    }

    public event Action? FingerDown;
    public event Action? Released;
}

internal sealed class ComposerView : LinearLayout
{
    private readonly PetArrowView _arrow;
    private readonly LinearLayout _card;
    private readonly EditText _input;
    private readonly Action<string> _submit;

    public event Action? Editing;

    public event Action? Finished;

    public ComposerView(Context context, Action<string> submit) : base(context)
    {
        _submit = submit;
        SetBackgroundColor(Color.Transparent);
        SetPadding(Dip.Px(10), Dip.Px(10), Dip.Px(10), Dip.Px(10));
        _arrow = new PetArrowView(context);
        _card = new LinearLayout(context) { Orientation = global::Android.Widget.Orientation.Horizontal };
        _card.SetGravity(GravityFlags.CenterVertical);
        _card.SetPadding(Dip.Px(10), Dip.Px(8), Dip.Px(8), Dip.Px(8));
        var shell = new GradientDrawable();
        shell.SetColor(PetColors.Bubble);
        shell.SetCornerRadius(Dip.Px(25));
        shell.SetStroke(Dip.Px(2), PetColors.Accent);
        _card.Background = shell;
        _card.Elevation = Dip.Px(3);
        _input = new EditText(context) { Hint = "跟佳怡说一句" };
        _input.SetSingleLine(true);
        _input.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14);
        _input.SetMinHeight(Dip.Px(34));
        _input.SetTextColor(PetColors.Text);
        _input.SetHintTextColor(PetColors.Disabled);
        _input.Background = null;
        _input.Focusable = true;
        _input.FocusableInTouchMode = true;
        _input.ImeOptions = ImeAction.Send;
        _input.SetPadding(0, Dip.Px(8), 0, Dip.Px(8));
        _input.SetOnEditorActionListener(new SendAction(Submit));
        _input.KeyPress += (_, args) =>
        {
            if (args.Event?.Action == KeyEventActions.Down && args.KeyCode == Keycode.Enter)
            {
                Submit();
                args.Handled = true;
            }
        };
        _input.Touch += (_, args) =>
        {
            if (args.Event?.ActionMasked == MotionEventActions.Down)
            {
                Editing?.Invoke();
            }
        };
        var button = new Button(context) { Text = "发送" };
        button.SetAllCaps(false);
        button.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14);
        button.SetTextColor(Color.White);
        button.SetMinWidth(Dip.Px(64));
        button.SetMinimumWidth(Dip.Px(64));
        button.SetMinHeight(Dip.Px(38));
        button.SetMinimumHeight(Dip.Px(38));
        button.SetPadding(Dip.Px(12), Dip.Px(6), Dip.Px(12), Dip.Px(6));
        button.StateListAnimator = null;
        var face = new GradientDrawable(GradientDrawable.Orientation.TlBr, [Color.ParseColor("#FFFF8AA6"), Color.ParseColor("#FFE85A86")]);
        face.SetCornerRadius(Dip.Px(16));
        button.Background = face;
        button.Click += (_, _) => Submit();
        var buttonParams = new LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
        buttonParams.LeftMargin = Dip.Px(8);
        _card.AddView(_input, new LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        _card.AddView(button, buttonParams);
        SetSide(DialoguePlacementSide.Below);
    }

    public void TakeInput()
    {
        _input.RequestFocus();
        var manager = (InputMethodManager?)Context?.GetSystemService(Context.InputMethodService);
        manager?.ShowSoftInput(_input, ShowFlags.Implicit);
    }

    public void ReleaseInput()
    {
        var manager = (InputMethodManager?)Context?.GetSystemService(Context.InputMethodService);
        manager?.HideSoftInputFromWindow(_input.WindowToken, HideSoftInputFlags.None);
        _input.ClearFocus();
    }

    private void Submit()
    {
        var text = _input.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _input.Text = "";
        _submit(text);
        Finished?.Invoke();
    }

    private sealed class SendAction : Java.Lang.Object, TextView.IOnEditorActionListener
    {
        private readonly Action _send;

        public SendAction(Action send) => _send = send;

        public SendAction(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer)
        {
            _send = static () => { };
        }

        public bool OnEditorAction(TextView? view, ImeAction actionId, KeyEvent? keyEvent)
        {
            if (actionId is ImeAction.Send or ImeAction.Done || keyEvent?.KeyCode == Keycode.Enter)
            {
                _send();
                return true;
            }

            return false;
        }
    }

    private DialoguePlacementSide? _side;

    public void SetSide(DialoguePlacementSide side)
    {
        if (_side == side)
        {
            return;
        }

        _side = side;
        RemoveAllViews();
        var beside = side is DialoguePlacementSide.Left or DialoguePlacementSide.Right;
        Orientation = beside
            ? global::Android.Widget.Orientation.Horizontal
            : global::Android.Widget.Orientation.Vertical;
        _arrow.Direction = side switch
        {
            DialoguePlacementSide.Below => PetArrowDirection.Up,
            DialoguePlacementSide.Left => PetArrowDirection.Right,
            DialoguePlacementSide.Right => PetArrowDirection.Left,
            _ => PetArrowDirection.Down
        };
        var arrow = beside
            ? new LayoutParams(Dip.Px(12), Dip.Px(18)) { Gravity = GravityFlags.CenterVertical }
            : new LayoutParams(Dip.Px(20), Dip.Px(12)) { Gravity = GravityFlags.CenterHorizontal };
        var card = new LayoutParams(Dip.Px(276), ViewGroup.LayoutParams.WrapContent);
        if (side is DialoguePlacementSide.Below or DialoguePlacementSide.Right)
        {
            AddView(_arrow, arrow);
            AddView(_card, card);
        }
        else
        {
            AddView(_card, card);
            AddView(_arrow, arrow);
        }
    }
}

internal enum PetArrowDirection
{
    Up,
    Down,
    Left,
    Right
}

internal sealed class PetArrowView : View
{
    private readonly Paint _fill = new(PaintFlags.AntiAlias) { Color = PetColors.Arrow };
    private readonly Paint _stroke = new(PaintFlags.AntiAlias)
    {
        Color = PetColors.Accent,
        StrokeWidth = 2
    };

    public PetArrowView(Context context) : base(context) => _stroke.SetStyle(Paint.Style.Stroke);

    public PetArrowDirection Direction { get; set; } = PetArrowDirection.Down;

    protected override void OnDraw(Canvas canvas)
    {
        var path = new global::Android.Graphics.Path();
        switch (Direction)
        {
            case PetArrowDirection.Up:
                path.MoveTo(0, Height);
                path.LineTo(Width, Height);
                path.LineTo(Width / 2f, 0);
                break;
            case PetArrowDirection.Left:
                path.MoveTo(Width, 0);
                path.LineTo(Width, Height);
                path.LineTo(0, Height / 2f);
                break;
            case PetArrowDirection.Right:
                path.MoveTo(0, 0);
                path.LineTo(0, Height);
                path.LineTo(Width, Height / 2f);
                break;
            default:
                path.MoveTo(0, 0);
                path.LineTo(Width, 0);
                path.LineTo(Width / 2f, Height);
                break;
        }

        path.Close();
        canvas.DrawPath(path, _fill);
        canvas.DrawPath(path, _stroke);
    }
}

internal sealed class VoiceRingView : View
{
    private readonly Paint _track = new(PaintFlags.AntiAlias);
    private readonly Paint _arc = new(PaintFlags.AntiAlias);
    private ValueAnimator? _spin;
    private float _fraction;

    public VoiceRingView(Context context) : base(context)
    {
        Visibility = ViewStates.Gone;
        _track.Color = PetColors.Accent;
        _track.Alpha = 71;
        _track.StrokeWidth = Dip.Px(3);
        _track.SetStyle(Paint.Style.Stroke);
        _arc.Color = PetColors.Accent;
        _arc.StrokeWidth = Dip.Px(3);
        _arc.StrokeCap = Paint.Cap.Round;
        _arc.SetStyle(Paint.Style.Stroke);
    }

    public void ShowWaiting()
    {
        Visibility = ViewStates.Visible;
        _fraction = -1;
        if (_spin is null)
        {
            _spin = ValueAnimator.OfFloat(0, 360)!;
            _spin.SetDuration(2880);
            _spin.RepeatCount = ValueAnimator.Infinite;
            _spin.Update += (_, args) =>
            {
                Rotation = (float)(args.Animation?.AnimatedValue ?? 0f);
                Invalidate();
            };
        }

        if (!_spin.IsRunning)
        {
            _spin.Start();
        }

        Invalidate();
    }

    public void ShowProgress(float fraction)
    {
        _spin?.Cancel();
        Rotation = 0;
        Visibility = ViewStates.Visible;
        _fraction = Math.Clamp(fraction, 0, 1);
        Invalidate();
    }

    public void HideRing()
    {
        _spin?.Cancel();
        Rotation = 0;
        Visibility = ViewStates.Gone;
    }

    protected override void OnDraw(Canvas canvas)
    {
        var inset = Dip.Px(1.5);
        var oval = new RectF(inset, inset, Width - inset, Height - inset);
        canvas.DrawOval(oval, _track);
        var sweep = _fraction < 0 ? 80f : 360f * _fraction;
        var start = _fraction < 0 ? -90f : -90f;
        canvas.DrawArc(oval, start, sweep, false, _arc);
    }
}
