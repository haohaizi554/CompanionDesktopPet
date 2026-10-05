using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Media;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Android;

internal enum PanelPage
{
    Root,
    Size,
    Developer,
    Corpus,
    Voice,
    Parameters,
    Endpoint
}

internal sealed class ControlPanel : FrameLayout
{
    private readonly PetSession _session;
    private readonly Action _relayout;
    private readonly string? _voicePackDirectory;
    private readonly LinearLayout _body;
    private PanelPage _page = PanelPage.Root;
    private readonly Stack<CorpusFolder> _corpusStack = new();
    private readonly Stack<VoiceLibraryFolder> _voiceStack = new();
    private CorpusFolder? _corpus;
    private CorpusFolder? _corpusRoot;
    private bool _corpusLoading;
    private bool _corpusFailed;
    private VoiceLibraryFolder? _voice;
    private DeveloperTestParameters _draft = DeveloperTestParameters.CreateDefault();

    public ControlPanel(Context context, PetSession session, Action relayout, string? voicePackDirectory) : base(context)
    {
        _session = session;
        _relayout = relayout;
        _voicePackDirectory = voicePackDirectory;
        var shell = new GradientDrawable();
        shell.SetColors([PetColors.MenuStart.ToArgb(), PetColors.MenuEnd.ToArgb()]);
        shell.SetOrientation(GradientDrawable.Orientation.TlBr);
        shell.SetCornerRadius(Dip.Px(24));
        shell.SetStroke(Dip.Px(2), PetColors.MenuBorder);
        Background = shell;
        Elevation = Dip.Px(8);
        SetPadding(Dip.Px(11), Dip.Px(11), Dip.Px(11), Dip.Px(11));
        _body = new LinearLayout(context) { Orientation = global::Android.Widget.Orientation.Vertical };
        var scroll = new ScrollView(context);
        scroll.AddView(_body);
        AddView(scroll, new LayoutParams(Dip.Px(294), ViewGroup.LayoutParams.WrapContent));
        Show(PanelPage.Root);
    }

    public bool HoldsKeyboard => _page == PanelPage.Endpoint;

    public Action? EnsureKeyboard { get; set; }

    public event Action? KeyboardIdle;

    public void RefreshRoot()
    {
        if (_page == PanelPage.Root)
        {
            Show(PanelPage.Root);
        }
    }

    public void Show(PanelPage page)
    {
        _page = page;
        _body.RemoveAllViews();
        switch (page)
        {
            case PanelPage.Size:
                Title("大小");
                ScaleRow("小巧", PetScale.Small);
                ScaleRow("刚刚好", PetScale.Normal);
                ScaleRow("大一点", PetScale.Large);
                break;
            case PanelPage.Developer:
                Title("开发者模式");
                ActionRow("语料库", "☰", () => OpenCorpus(null));
                ActionRow(_voicePackDirectory is null ? "旁边还没有语音库" : "语音库", "♪", OpenVoice, _voicePackDirectory is not null);
                ActionRow("测试参数", "⌁", () =>
                {
                    _draft = _session.Parameters.Clone();
                    Show(PanelPage.Parameters);
                });
                break;
            case PanelPage.Corpus:
                ShowCorpus();
                break;
            case PanelPage.Voice:
                ShowVoice();
                break;
            case PanelPage.Parameters:
                ShowParameters();
                break;
            case PanelPage.Endpoint:
                ShowEndpoint();
                break;
            default:
                ShowRoot();
                break;
        }

        _relayout();
        if (page != PanelPage.Endpoint)
        {
            KeyboardIdle?.Invoke();
        }
    }

    private void ShowRoot()
    {
        ActionRow(_session.SayLabel, "✦", _session.Say);
        CheckRow("语音开关", "♪", _session.VoiceEnabled, _session.VoiceAvailable, _session.ToggleVoice);
        CheckRow("对话开关", "✎", _session.DialogueEnabled, true, _session.ToggleDialogue);
        ActionRow("填写对话接口", "✎", () => Show(PanelPage.Endpoint));
        ActionRow("打个招呼♡", "♡", _session.Greet);
        ActionRow(_session.Paused ? "继续动画" : "暂停动画", "☾", _session.TogglePause);
        Separator();
        ActionRow("大小", "◌", () => Show(PanelPage.Size));
        CheckRow("保持置顶", "⌁", _session.Topmost, true, _session.ToggleTopmost);
        CheckRow("开机自启动", "⌁", _session.AutostartEnabled(), true, _session.ToggleAutostart);
        ActionRow("回到右下角", "⌂", _session.RestorePosition);
        Separator();
        ActionRow("开发者模式", "⚙", () => Show(PanelPage.Developer));
        Separator();
        ActionRow("藏到托盘里 ♡", "☾", _session.HideToTray);
        ActionRow("先休息啦（退出）", "☁", _session.Exit);
    }

    private void ShowCorpus()
    {
        Title(_corpus?.Title ?? "语料库");
        if (_corpus is null && _corpusRoot is not null)
        {
            _corpus = _corpusRoot;
            _corpusStack.Clear();
            _corpusStack.Push(_corpusRoot);
        }

        if (_corpus is null)
        {
            if (_corpusFailed)
            {
                Note("文库没醒。");
                ActionRow("再试一次", "☰", () =>
                {
                    _corpusFailed = false;
                    Show(PanelPage.Corpus);
                });
                return;
            }

            Note("文库正在醒…");
            if (_corpusLoading)
            {
                return;
            }

            _corpusLoading = true;
            _ = Task.Run(() => CorpusBrowserIndex.Outline).ContinueWith(task =>
                Post(() =>
                {
                    _corpusLoading = false;
                    if (!task.IsCompletedSuccessfully)
                    {
                        _corpusFailed = true;
                        if (_page == PanelPage.Corpus)
                        {
                            Show(PanelPage.Corpus);
                        }

                        return;
                    }

                    _corpusFailed = false;
                    _corpusRoot = task.Result;
                    _corpusStack.Clear();
                    _corpusStack.Push(task.Result);
                    _corpus = task.Result;
                    if (_page == PanelPage.Corpus)
                    {
                        Show(PanelPage.Corpus);
                    }
                }));
            return;
        }

        foreach (var child in _corpus.Children)
        {
            var folder = child;
            ActionRow(folder.Header, "☰", () =>
            {
                _corpusStack.Push(folder);
                _corpus = folder;
                Show(PanelPage.Corpus);
            });
        }

        if (_corpus.Children.Length > 0 || _corpus.Lines.Length == 0)
        {
            return;
        }

        var lines = _corpus.Lines;
        var list = new ListView(Context);
        list.Adapter = new CorpusLineAdapter(lines);
        list.ItemClick += (_, args) =>
        {
            if (args.Position >= 0 && args.Position < lines.Length)
            {
                _session.PreviewLine(lines[args.Position].Text);
            }
        };
        _body.AddView(list, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dip.Px(360)));
    }

    private void ShowVoice()
    {
        Title(_voice?.Title ?? "语音库");
        if (_voicePackDirectory is null)
        {
            Note("旁边还没有语音库");
            return;
        }

        _voice ??= VoiceLibraryIndex.Load(_voicePackDirectory);
        if (_voiceStack.Count == 0)
        {
            _voiceStack.Push(_voice);
        }

        foreach (var child in _voice.Children)
        {
            var folder = child;
            ActionRow(folder.Header, "♪", () =>
            {
                _voiceStack.Push(folder);
                _voice = folder;
                Show(PanelPage.Voice);
            });
        }

        foreach (var clip in _voice.Clips)
        {
            var item = clip;
            ActionRow(item.Prompt, "♪", () =>
            {
                VoicePreview.Play(Context!, item.AudioPath);
                _session.PreviewLine(item.Prompt);
            });
        }
    }

    private void ShowParameters()
    {
        Title("测试参数");
        Note("只改这一次，不会动语料本。");
        Band("白天", _draft.DayMinimumMinutes, _draft.DayMaximumMinutes, DeveloperTestParameters.MinimumDayMinutes, DeveloperTestParameters.MaximumDayMinutes,
            (min, max) => (_draft.DayMinimumMinutes, _draft.DayMaximumMinutes) = (min, max));
        Band("傍晚", _draft.EveningMinimumMinutes, _draft.EveningMaximumMinutes, DeveloperTestParameters.MinimumEveningMinutes, DeveloperTestParameters.MaximumEveningMinutes,
            (min, max) => (_draft.EveningMinimumMinutes, _draft.EveningMaximumMinutes) = (min, max));
        Band("深夜", _draft.LateNightMinimumMinutes, _draft.LateNightMaximumMinutes, DeveloperTestParameters.MinimumLateNightMinutes, DeveloperTestParameters.MaximumLateNightMinutes,
            (min, max) => (_draft.LateNightMinimumMinutes, _draft.LateNightMaximumMinutes) = (min, max));
        Band("全屏", _draft.FullscreenMinimumMinutes, _draft.FullscreenMaximumMinutes, DeveloperTestParameters.MinimumFullscreenMinutes, DeveloperTestParameters.MaximumFullscreenMinutes,
            (min, max) => (_draft.FullscreenMinimumMinutes, _draft.FullscreenMaximumMinutes) = (min, max));
        Single("气泡", _draft.BubbleSeconds, DeveloperTestParameters.MinimumBubbleSeconds, DeveloperTestParameters.MaximumBubbleSeconds, value => _draft.BubbleSeconds = value, "秒");
        Single("输出", _draft.ReplyMaxChars, DeveloperTestParameters.MinimumReplyChars, DeveloperTestParameters.MaximumReplyChars, value => _draft.ReplyMaxChars = value, "字");
        if (_session.VoiceEnabled)
        {
            Note("语音开着。语速 1 是原速。下一句生效。");
            FloatRow("语速", _draft.SpeechSpeed, DeveloperTestParameters.MinimumSpeechSpeed, DeveloperTestParameters.MaximumSpeechSpeed, value => _draft.SpeechSpeed = value);
            FloatRow("语气", _draft.SpeechTemperature, DeveloperTestParameters.MinimumSpeechTemperature, DeveloperTestParameters.MaximumSpeechTemperature, value => _draft.SpeechTemperature = value);
            FloatRow("重复", _draft.SpeechRepetition, DeveloperTestParameters.MinimumSpeechRepetition, DeveloperTestParameters.MaximumSpeechRepetition, value => _draft.SpeechRepetition = value);
        }

        var row = new LinearLayout(Context) { Orientation = global::Android.Widget.Orientation.Horizontal };
        row.AddView(Button("就用这组", () => _session.ApplyParameters(_draft.Clone())), new LinearLayout.LayoutParams(0, Dip.Px(40), 1));
        row.AddView(Button("恢复默认", () =>
        {
            _session.ResetParameters();
            _draft = _session.Parameters.Clone();
            Show(PanelPage.Parameters);
        }), new LinearLayout.LayoutParams(0, Dip.Px(40), 1));
        _body.AddView(row);
    }

    private void ShowEndpoint()
    {
        Title("填写对话接口");
        Note("文字先到这台电脑，再由电脑去问下面这个模型。语音也在这台电脑上合成。");
        var url = Field("地址", LinkDefaults.ModelUrl);
        var model = Field("模型名", LinkDefaults.ModelName);
        var key = Field("密钥", LinkDefaults.ApiKey);
        var auth = LinkDefaults.Auth;
        var hint = new TextView(Context) { Text = "模型和密钥跟电脑上的佳怡是同一套。", TextSize = 11 };
        hint.SetTextColor(PetColors.Disabled);
        hint.SetPadding(Dip.Px(8), Dip.Px(4), Dip.Px(8), Dip.Px(4));
        ActionRow("这台电脑正在用的", "✎", () =>
        {
            url.Text = LinkDefaults.ModelUrl;
            model.Text = LinkDefaults.ModelName;
            key.Text = LinkDefaults.ApiKey;
            auth = LinkDefaults.Auth;
            hint.Text = "模型和密钥跟电脑上的佳怡是同一套。";
        });
        foreach (var preset in DialogueEndpointSetup.Presets)
        {
            var item = preset;
            ActionRow(item.Label, "✎", () =>
            {
                url.Text = item.BaseUrl;
                model.Text = item.Model;
                key.Text = item.ApiKey;
                auth = item.Auth;
                hint.Text = item.Hint;
            });
        }

        _body.AddView(hint);
        _body.AddView(Button("保存并打开", () => _session.SaveEndpoint(url.Text ?? "", model.Text ?? "", key.Text ?? "", auth)));
    }

    private EditText Field(string label, string value)
    {
        Note(label);
        var input = new EditText(Context) { Text = value };
        input.SetSingleLine(true);
        input.SetTextColor(PetColors.Text);
        input.Focusable = true;
        input.FocusableInTouchMode = true;
        input.Touch += (_, args) =>
        {
            if (args.Event?.ActionMasked != MotionEventActions.Down)
            {
                return;
            }

            EnsureKeyboard?.Invoke();
            input.Post(() =>
            {
                input.RequestFocus();
                var manager = (InputMethodManager?)Context?.GetSystemService(Context.InputMethodService);
                manager?.ShowSoftInput(input, ShowFlags.Implicit);
            });
        };
        _body.AddView(input, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        return input;
    }

    private void OpenCorpus(CorpusFolder? folder)
    {
        _corpusStack.Clear();
        if (folder is null)
        {
            _corpus = _corpusRoot;
            if (_corpusRoot is not null)
            {
                _corpusStack.Push(_corpusRoot);
            }
        }
        else
        {
            _corpusStack.Push(folder);
            _corpus = folder;
        }

        Show(PanelPage.Corpus);
    }

    private void OpenVoice()
    {
        _voiceStack.Clear();
        _voice = null;
        Show(PanelPage.Voice);
    }

    private void Title(string text)
    {
        ActionRow("返回", "←", () =>
        {
            if (_page is PanelPage.Corpus)
            {
                if (_corpusStack.Count > 1)
                {
                    _corpusStack.Pop();
                    _corpus = _corpusStack.Peek();
                    Show(PanelPage.Corpus);
                    return;
                }

                _corpusStack.Clear();
                _corpus = null;
                Show(PanelPage.Developer);
                return;
            }

            if (_page is PanelPage.Voice)
            {
                if (_voiceStack.Count > 1)
                {
                    _voiceStack.Pop();
                    _voice = _voiceStack.Peek();
                    Show(PanelPage.Voice);
                    return;
                }

                _voiceStack.Clear();
                _voice = null;
                Show(PanelPage.Developer);
                return;
            }

            Show(_page is PanelPage.Size or PanelPage.Developer or PanelPage.Parameters or PanelPage.Endpoint
                ? PanelPage.Root
                : PanelPage.Developer);
        });
        Note(text);
    }

    private void ActionRow(string label, string mark, Action action, bool enabled = true)
    {
        var row = Row($"{mark}   {label}", enabled);
        if (enabled)
        {
            row.Click += (_, _) => action();
        }

        _body.AddView(row);
    }

    private void CheckRow(string label, string mark, bool check, bool enabled, Action action)
    {
        var prefix = check ? "✓" : mark;
        ActionRow($"{label}", prefix, action, enabled);
    }

    private void ScaleRow(string label, PetScale scale)
    {
        var mark = _session.Scale == scale ? "✓" : "◌";
        ActionRow(label, mark, () => _session.SetScale(scale));
    }

    private void Separator()
    {
        var line = new View(Context);
        line.SetBackgroundColor(PetColors.Accent);
        line.Alpha = 0.45f;
        var parameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dip.Px(1));
        parameters.SetMargins(Dip.Px(8), Dip.Px(6), Dip.Px(8), Dip.Px(6));
        _body.AddView(line, parameters);
    }

    private void Note(string text)
    {
        var view = new TextView(Context) { Text = text, TextSize = 11 };
        view.SetTextColor(PetColors.Disabled);
        view.SetPadding(Dip.Px(8), Dip.Px(4), Dip.Px(8), Dip.Px(4));
        _body.AddView(view);
    }

    private TextView Row(string text, bool enabled)
    {
        var view = new TextView(Context) { Text = text, TextSize = 14 };
        view.SetTextColor(enabled ? PetColors.Text : PetColors.Disabled);
        view.Alpha = enabled ? 1 : 0.46f;
        view.SetPadding(Dip.Px(10), Dip.Px(10), Dip.Px(10), Dip.Px(10));
        view.Clickable = enabled;
        return view;
    }

    private Button Button(string text, Action action)
    {
        var button = new Button(Context) { Text = text };
        button.SetTextColor(PetColors.Text);
        button.Click += (_, _) => action();
        return button;
    }

    private void Band(string name, int min, int max, int floor, int ceiling, Action<int, int> apply)
    {
        Single($"{name}起", min, floor, ceiling, value => apply(Math.Min(value, max), Math.Max(value, max)), "分");
        Single($"{name}止", max, floor, ceiling, value => apply(Math.Min(min, value), Math.Max(min, value)), "分");
    }

    private void Single(string name, int value, int floor, int ceiling, Action<int> apply, string unit)
    {
        var label = new TextView(Context) { TextSize = 12 };
        label.SetTextColor(PetColors.Text);
        void Paint(int current) => label.Text = $"{name}  {current} {unit}";
        Paint(value);
        var slider = new SeekBar(Context) { Max = Math.Max(1, ceiling - floor), Progress = value - floor };
        slider.ProgressChanged += (_, args) =>
        {
            if (!args.FromUser)
            {
                return;
            }

            var current = floor + args.Progress;
            Paint(current);
            apply(current);
        };
        _body.AddView(label);
        _body.AddView(slider);
    }

    private void FloatRow(string name, double value, double floor, double ceiling, Action<double> apply)
    {
        var label = new TextView(Context) { TextSize = 12 };
        label.SetTextColor(PetColors.Text);
        void Paint(double current) => label.Text = $"{name}  {current:0.00}";
        Paint(value);
        var slider = new SeekBar(Context) { Max = 100 };
        slider.Progress = (int)Math.Round((value - floor) / (ceiling - floor) * 100);
        slider.ProgressChanged += (_, args) =>
        {
            if (!args.FromUser)
            {
                return;
            }

            var current = floor + ((ceiling - floor) * args.Progress / 100);
            Paint(current);
            apply(current);
        };
        _body.AddView(label);
        _body.AddView(slider);
    }
}

internal sealed class CorpusLineAdapter : BaseAdapter
{
    private readonly DialogueLine[] _lines;

    public CorpusLineAdapter(DialogueLine[] lines) => _lines = lines;

    public override int Count => _lines.Length;

    public override Java.Lang.Object? GetItem(int position) => null;

    public override long GetItemId(int position) => position;

    public override View GetView(int position, View? convertView, ViewGroup? parent)
    {
        var view = convertView as TextView ?? new TextView(parent?.Context);
        view.Text = _lines[position].Text;
        view.TextSize = 14;
        view.SetTextColor(PetColors.Text);
        view.SetPadding(Dip.Px(10), Dip.Px(8), Dip.Px(10), Dip.Px(8));
        return view;
    }
}

internal static class VoicePreview
{
    private static MediaPlayer? _player;

    public static void Play(Context context, string path)
    {
        _player?.Release();
        _player = new MediaPlayer();
        _player.SetDataSource(path);
        _player.Prepare();
        _player.Start();
    }
}
