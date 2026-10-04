using Android.Content;
using Android.OS;
using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;
using CompanionDesktopPet.UI;

namespace CompanionDesktopPet.Android;

internal sealed class PetSession : IDisposable
{
    private const double ClickSlopDips = 16;
    private readonly Handler _handler;
    private readonly SettingsService _settingsService;
    private readonly AgentMemoryService _memoryService;
    private readonly string? _stateDirectory;
    private DialogueService _dialogue = null!;
    private DialogueWarmupCoordinator _warmup = null!;
    private readonly DialogueScheduler _scheduler = new();
    private readonly AutomaticDialogueCadenceController _cadence;
    private readonly AmbientActionScheduler _ambient = new();
    private readonly PetActionCoordinator _actions = new();
    private readonly FullscreenStateTracker _fullscreenState = new();
    private readonly DeveloperTestParameters _parameters = DeveloperTestParameters.CreateDefault();
    private readonly Random _random = new();
    private readonly object _replyGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private PetReminderStore? _reminders;
    private CompanionEventPump? _events;
    private IVoiceSpeaker? _voice;
    private readonly PcDialogueClient _dialogueClient;
    private PetSettings _settings = PetSettings.Default;
    private double _left = double.NaN;
    private double _top = double.NaN;
    private double _pressLeft;
    private double _pressTop;
    private bool _paused;
    private bool _topmost = true;
    private bool _hidden;
    private bool _occluded;
    private bool _dragged;
    private bool _ready;
    private bool _exit;
    private int _warmupGeneration;
    private int _appliedWarmup = -1;
    private long _startupRevision;
    private long _replyRevision;
    private bool _replayStartup = true;
    private StartupGreetingPhase _greeting = StartupGreetingPhase.Pending;
    private PetAmbientAction _pendingAmbient;
    private long _ambientDue;
    private int _ambientGeneration;
    private int _armedAmbient;
    private string? _lastUtterance;
    private int _automaticGeneration;
    private readonly SpokenLineBridge _spoken = new();
    private readonly object _replyQueue = new();
    private readonly Queue<PendingReply> _replyRequests = new();
    private int _replyPumpRunning;
    private int _spokenFlush;
    private bool _voiceLine;
    private bool _awaitingReply;
    private bool _fingerHold;
    private bool _bubbleVisible;
    private bool _bubbleHeld;
    private DateTime _bubbleDeadlineUtc;
    private TimeSpan _bubbleRemaining = TimeSpan.FromSeconds(5);

    public PetSession(
        Handler handler,
        string? stateDirectory,
        Func<bool> autostart,
        Action<bool> setAutostart,
        Context voiceContext,
        string? voiceHost)
    {
        _handler = handler;
        _stateDirectory = stateDirectory;
        _settingsService = new SettingsService(stateDirectory);
        _memoryService = new AgentMemoryService(stateDirectory);
        _cadence = new AutomaticDialogueCadenceController(_scheduler, TimeProvider.System);
        AutostartEnabled = autostart;
        SetAutostart = setAutostart;
        IVoiceSpeaker? voice = PcGpuVoiceSpeaker.TryCreate(voiceContext, voiceHost, stateDirectory);
        if (voice is PcGpuVoiceSpeaker gpu)
        {
            gpu.Mouth += level => MouthChanged?.Invoke(level);
            gpu.PlaybackFraction += fraction => VoiceCue?.Invoke(2, fraction);
        }

        _voice = voice ?? JiayiVoiceSpeaker.TryCreate(stateDirectory ?? AppContext.BaseDirectory);
        if (_voice is not null)
        {
            _voice.VoiceIdle += () => _handler.Post(ReleaseVoiceHold);
        }
        _dialogueClient = new PcDialogueClient(string.IsNullOrWhiteSpace(voiceHost) ? VoiceHostStore.DefaultHost : voiceHost);
    }

    public PetScale Scale { get; private set; } = PetScale.Normal;

    public bool Paused => _paused;

    public bool Topmost => _topmost;

    public bool Hidden => _hidden;

    public bool VoiceAvailable => _voice is not null;

    public bool VoiceEnabled => _voice is { Enabled: true };

    public bool DialogueEnabled { get; private set; }

    public string SayLabel { get; private set; } = "说句话 ♡";

    public DeveloperTestParameters Parameters => _parameters;

    public Func<bool> AutostartEnabled { get; }

    public Action<bool> SetAutostart { get; }

    public event Action? Changed;
    public event Action<string>? BubbleRequested;
    public event Action? BubbleCleared;
    public event Action<double, double>? Moved;
    public event Action<PetScale>? Scaled;
    public event Action<bool>? IdleChanged;
    public event Action<bool>? Clicked;
    public event Action<double>? DragLean;
    public event Action? Landing;
    public event Action<bool>? Blink;
    public event Action? Greeting;
    public event Action? MenuClosed;
    public event Action<bool>? VisibilityChanged;
    public event Action? Exited;
    public event Action<bool>? DialogueVisibilityChanged;
    public event Action<byte>? MouthChanged;
    public event Action<int, float>? VoiceCue;

    public ScreenRect WorkArea { get; set; } = new(0, 0, 360, 640);

    public double CharacterSize => Scale switch
    {
        PetScale.Small => 250,
        PetScale.Large => 390,
        _ => 320
    };

    public async Task StartAsync()
    {
        var settings = await _settingsService.LoadAsync().ConfigureAwait(true);
        var memory = await _memoryService.LoadForDeferredWarmupAsync().ConfigureAwait(true);
        if (_exit)
        {
            return;
        }

        _settings = settings;
        _dialogue = DialogueService.CreateDeferred(memory, null, TimeProvider.System);
        _warmup = new DialogueWarmupCoordinator(_dialogue);

        if (settings.Tuning is { } tuning && _parameters.TryCopyTuning(tuning))
        {
            _scheduler.TestParameters = _parameters;
            if (_voice is not null)
            {
                _voice.Enabled = tuning.VoiceEnabled;
                _voice.ApplySpeech(
                    _parameters.SpeechSpeed,
                    _parameters.SpeechTemperature,
                    _parameters.SpeechRepetition,
                    _parameters.TopK,
                    _parameters.TopP);
            }
        }

        Scale = settings.Scale;
        _paused = settings.AnimationPaused;
        _topmost = settings.AlwaysOnTop;
        DialogueEnabled = settings.DialogueEnabled;
        _left = settings.Left;
        _top = settings.Top;
        if (!double.IsFinite(_left) || !double.IsFinite(_top))
        {
            var origin = DefaultOrigin();
            _left = origin.X;
            _top = origin.Y;
        }

        Clamp();
        _ready = true;
        Scaled?.Invoke(Scale);
        Moved?.Invoke(_left, _top);
        IdleChanged?.Invoke(!_paused);
        if (_paused)
        {
            _actions.Pause();
        }

        var now = DateTime.Now;
        var fullscreen = ObserveFullscreen();
        _events = new CompanionEventPump(now, null);
        ShowEvent(CompanionEvent.Startup, now, fullscreen);
        _startupRevision = _replyRevision;
        _ = RunWarmupAsync();
        if (DialogueEnabled)
        {
            DialogueVisibilityChanged?.Invoke(true);
        }

        ScheduleGreeting();
        ArmAutomatic(now, fullscreen);
        Schedule(TimeSpan.FromSeconds(15), ReminderTick);
        Schedule(TimeSpan.FromSeconds(30), EventTick);
        Changed?.Invoke();
    }

    public void Click(double xDip)
    {
        if (!_ready || _hidden)
        {
            return;
        }

        var tiltPositive = xDip < CharacterSize / 2;
        Clicked?.Invoke(tiltPositive);
        if (SayLabel.StartsWith("重试", StringComparison.Ordinal))
        {
            _ = RunWarmupAsync(retry: true);
        }

        ShowEvent(CompanionEvent.Click, DateTime.Now, ObserveFullscreen());
    }

    public void Say()
    {
        if (_hidden)
        {
            ShowFromTray();
        }

        Click(CharacterSize / 2);
    }

    public void BeginDrag()
    {
        if (!_ready)
        {
            return;
        }

        _dragged = true;
        _pressLeft = _left;
        _pressTop = _top;
        PreserveGreeting();
        CancelAmbient();
        _actions.BeginDrag();
        IdleChanged?.Invoke(false);
    }

    public void DragBy(double dxDip, double dyDip)
    {
        if (!_dragged)
        {
            return;
        }

        var before = _left;
        _left += dxDip;
        _top += dyDip;
        Clamp();
        DragLean?.Invoke(_left - before);
        Moved?.Invoke(_left, _top);
    }

    public void DragTo(double leftDip, double topDip)
    {
        if (!_dragged)
        {
            return;
        }

        var before = _left;
        _left = leftDip;
        _top = topDip;
        Clamp();
        DragLean?.Invoke(_left - before);
        Moved?.Invoke(_left, _top);
    }

    public void EndDrag()
    {
        if (!_dragged)
        {
            return;
        }

        var moved = Math.Abs(_left - _pressLeft) > ClickSlopDips
            || Math.Abs(_top - _pressTop) > ClickSlopDips;
        _dragged = false;
        if (!moved)
        {
            _actions.CancelDrag();
            DragLean?.Invoke(0);
            if (!_paused)
            {
                IdleChanged?.Invoke(true);
                ScheduleNextAmbient();
            }

            return;
        }

        _actions.BeginLanding();
        Landing?.Invoke();
        _ = SaveAsync();
    }

    public void CompleteLanding()
    {
        _actions.Complete(PetActionState.Landing);
        if (!_paused && _actions.State == PetActionState.Idle)
        {
            IdleChanged?.Invoke(true);
            ScheduleNextAmbient();
        }
    }

    public void HoldBubble()
    {
        _fingerHold = true;
        if (!_bubbleVisible || _bubbleHeld)
        {
            return;
        }

        _bubbleHeld = true;
        _bubbleRemaining = _bubbleDeadlineUtc - DateTime.UtcNow;
        if (_bubbleRemaining < TimeSpan.Zero)
        {
            _bubbleRemaining = TimeSpan.Zero;
        }
    }

    public void ReleaseBubble()
    {
        _fingerHold = false;
        if (_voiceLine || _awaitingReply || !_bubbleHeld)
        {
            return;
        }

        _bubbleHeld = false;
        if (_bubbleVisible)
        {
            _bubbleDeadlineUtc = DateTime.UtcNow + _bubbleRemaining;
            Schedule(_bubbleRemaining, HideBubbleIfDue);
        }
    }

    public void Greet()
    {
        PreserveGreeting();
        CancelAmbient();
        if (_actions.TryBeginAmbient(PetAmbientAction.Greeting))
        {
            Greeting?.Invoke();
        }
    }

    public void CompleteGreeting()
    {
        if (_greeting == StartupGreetingPhase.Running)
        {
            _greeting = StartupGreetingPhase.Completed;
        }

        _actions.Complete(PetActionState.Greeting);
        if (!_paused)
        {
            ScheduleNextAmbient();
        }
    }

    public void CompleteBlink()
    {
        _actions.Complete(PetActionState.Blinking);
        if (!_paused)
        {
            ScheduleNextAmbient();
        }
    }

    public void TogglePause()
    {
        _paused = !_paused;
        if (_paused)
        {
            PreserveGreeting();
            CancelAmbient();
            _actions.Pause();
            IdleChanged?.Invoke(false);
            ShowEvent(CompanionEvent.AnimationPaused, DateTime.Now, ObserveFullscreen());
        }
        else
        {
            _actions.Resume();
            IdleChanged?.Invoke(true);
            ShowEvent(CompanionEvent.AnimationResumed, DateTime.Now, ObserveFullscreen());
            ScheduleNextAmbient();
        }

        _ = SaveAsync();
        Changed?.Invoke();
    }

    public void SetScale(PetScale scale)
    {
        Scale = scale;
        Clamp();
        Scaled?.Invoke(scale);
        Moved?.Invoke(_left, _top);
        ShowEvent(CompanionEvent.SizeChanged, DateTime.Now, ObserveFullscreen());
        _ = SaveAsync();
        Changed?.Invoke();
        MenuClosed?.Invoke();
    }

    public void ToggleTopmost()
    {
        _topmost = !_topmost;
        _ = SaveAsync();
        Changed?.Invoke();
        MenuClosed?.Invoke();
    }

    public void ToggleAutostart()
    {
        var enabled = !AutostartEnabled();
        SetAutostart(enabled);
        Changed?.Invoke();
        MenuClosed?.Invoke();
    }

    public void RestorePosition()
    {
        var origin = DefaultOrigin();
        _left = origin.X;
        _top = origin.Y;
        Moved?.Invoke(_left, _top);
        ShowEvent(CompanionEvent.PositionRestored, DateTime.Now, ObserveFullscreen());
        _ = SaveAsync();
        MenuClosed?.Invoke();
    }

    public void HideToTray()
    {
        _hidden = true;
        DisarmAutomatic();
        PreserveGreeting();
        CancelAmbient();
        IdleChanged?.Invoke(false);
        _bubbleVisible = false;
        BubbleCleared?.Invoke();
        MenuClosed?.Invoke();
        VisibilityChanged?.Invoke(false);
    }

    public void ShowFromTray()
    {
        if (!_hidden)
        {
            return;
        }

        _hidden = false;
        VisibilityChanged?.Invoke(true);
        if (!_paused)
        {
            IdleChanged?.Invoke(true);
            ScheduleNextAmbient();
        }

        ArmAutomatic(DateTime.Now, ObserveFullscreen());
        Changed?.Invoke();
    }

    public void ToggleVoice()
    {
        if (_voice is null)
        {
            ShowText("这台电脑的语音还没连上。");
            return;
        }

        _voice.Enabled = !_voice.Enabled;
        if (!_voice.Enabled)
        {
            _voice.Stop();
        }

        _ = SaveAsync();
        Changed?.Invoke();
        MenuClosed?.Invoke();
    }

    public void ToggleDialogue()
    {
        DialogueEnabled = !DialogueEnabled;
        DialogueVisibilityChanged?.Invoke(DialogueEnabled);
        if (DialogueEnabled)
        {
        _ = Task.Run(() => AnnounceDialogueAsync());
        }

        _ = SaveAsync();
        Changed?.Invoke();
        MenuClosed?.Invoke();
    }

    public void SubmitDialogue(string text)
    {
        if (!DialogueEnabled || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        ShowText("……", hold: true);
        var line = text.Trim();
        _ = Task.Run(() => SendDialogueAsync(line));
    }

    public void SaveEndpoint(string baseUrl, string model, string apiKey, string auth)
    {
        _ = Task.Run(() => SaveEndpointAsync(baseUrl, model, apiKey, auth));
    }

    private async Task AnnounceDialogueAsync()
    {
        var ready = await _dialogueClient.PingAsync(_lifetime.Token).ConfigureAwait(false);
        if (_exit)
        {
            return;
        }

        _handler.Post(() =>
        {
            if (!DialogueEnabled)
            {
                return;
            }

            ShowText(ready
                ? "对话接上了，在上面的框里跟我说。"
                : "对话服务还没开。先在电脑上启动对话宿主，或先填写接口。");
        });
    }

    private async Task SendDialogueAsync(string text)
    {
        var settings = DialogueSkills.Describe(_parameters, Scale, _topmost, _paused, VoiceEnabled);
        PersonaDialogueReply reply;
        try
        {
            reply = await _dialogueClient.ReplyAsync(text, settings, _lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error("jiayi", exception.ToString());
            reply = new PersonaDialogueReply(false, "这句话我没接住，你再说一次。", false);
        }

        if (_exit)
        {
            return;
        }

        _handler.Post(() => PresentDialogue(reply));
    }

    private void PresentDialogue(PersonaDialogueReply reply)
    {
        if (_exit || !DialogueEnabled)
        {
            return;
        }

        _reminders ??= new PetReminderStore(_stateDirectory);
        _reminders.Accept(reply.Actions, DateTime.UtcNow);
        if (DialogueSkills.TryApply(
                reply.Actions,
                _parameters,
                Scale,
                _topmost,
                _paused,
                VoiceEnabled,
                out var state)
            && state.Changed)
        {
            ApplySkillState(state);
        }

        var answer = reply.Ok && !string.IsNullOrWhiteSpace(reply.Text)
            ? reply.Text
            : "这句话我没接住，你再说一次。";
        if (!reply.Ok && !string.IsNullOrWhiteSpace(reply.Text))
        {
            answer = reply.Text;
        }

        _awaitingReply = false;
        ShowText(answer);
        if (reply.Ok)
        {
            NoteSpoken(answer);
            SpeakLine(answer, "gentle", "dialogue");
        }
    }

    private void ApplySkillState(DialogueSkillState state)
    {
        _parameters.CopyFrom(state.Timing);
        _scheduler.TestParameters = _parameters;
        _voice?.ApplySpeech(
            _parameters.SpeechSpeed,
            _parameters.SpeechTemperature,
            _parameters.SpeechRepetition,
            _parameters.TopK,
            _parameters.TopP);
        if (Scale != state.Scale)
        {
            Scale = state.Scale;
            Clamp();
            Scaled?.Invoke(state.Scale);
            Moved?.Invoke(_left, _top);
        }

        _topmost = state.AlwaysOnTop;
        if (_paused != state.AnimationPaused)
        {
            _paused = state.AnimationPaused;
            if (_paused)
            {
                PreserveGreeting();
                CancelAmbient();
                _actions.Pause();
                IdleChanged?.Invoke(false);
            }
            else
            {
                _actions.Resume();
                IdleChanged?.Invoke(true);
                ScheduleNextAmbient();
            }
        }

        if (_voice is not null && _voice.Enabled != state.VoiceEnabled)
        {
            _voice.Enabled = state.VoiceEnabled;
            if (!state.VoiceEnabled)
            {
                _voice.Stop();
            }
        }

        ArmAutomatic(DateTime.Now, ObserveFullscreen());
        _ = SaveAsync();
        Changed?.Invoke();
    }

    private async Task SaveEndpointAsync(string baseUrl, string model, string apiKey, string auth)
    {
        var error = await _dialogueClient.SaveEndpointAsync(baseUrl, model, apiKey, auth, _lifetime.Token)
            .ConfigureAwait(false);
        if (_exit)
        {
            return;
        }

        _handler.Post(() =>
        {
            if (error is null)
            {
                DialogueEnabled = true;
                DialogueVisibilityChanged?.Invoke(true);
                ShowText("对话接口保存了，她在重新醒。");
            }
            else
            {
                ShowText(error);
            }

            _ = SaveAsync();
            Changed?.Invoke();
            MenuClosed?.Invoke();
        });
    }

    private void NoteSpoken(string text)
    {
        _spoken.Note(text);
        if (DialogueEnabled)
        {
            _ = Task.Run(() => FlushSpokenAsync());
        }
    }

    private async Task FlushSpokenAsync()
    {
        if (Interlocked.CompareExchange(ref _spokenFlush, 1, 0) != 0)
        {
            return;
        }

        var failed = false;
        try
        {
            if (!DialogueEnabled)
            {
                return;
            }

            var batch = _spoken.Take();
            for (var index = 0; index < batch.Count; index++)
            {
                if (!DialogueEnabled || _exit)
                {
                    _spoken.RestoreFront(batch.Skip(index).ToArray());
                    failed = true;
                    return;
                }

                if (!await _dialogueClient.RememberAsync(batch[index], _lifetime.Token).ConfigureAwait(false))
                {
                    _spoken.RestoreFront(batch.Skip(index).ToArray());
                    failed = true;
                    return;
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _spokenFlush, 0);
            if (!failed && _spoken.HasPending && DialogueEnabled && !_exit)
            {
                _ = FlushSpokenAsync();
            }
        }
    }

    public void ApplyParameters(DeveloperTestParameters draft)
    {
        var validation = draft.Validate();
        if (validation is not null)
        {
            ShowText(validation);
            return;
        }

        _parameters.CopyFrom(draft);
        _scheduler.TestParameters = _parameters;
        _voice?.ApplySpeech(
            _parameters.SpeechSpeed,
            _parameters.SpeechTemperature,
            _parameters.SpeechRepetition,
            _parameters.TopK,
            _parameters.TopP);
        ArmAutomatic(DateTime.Now, ObserveFullscreen());
        _ = SaveAsync();
        ShowText("这组只用在这一次。");
        Changed?.Invoke();
    }

    public void ResetParameters()
    {
        _parameters.CopyFrom(DeveloperTestParameters.CreateDefault());
        _scheduler.TestParameters = null;
        ArmAutomatic(DateTime.Now, ObserveFullscreen());
        _ = SaveAsync();
        Changed?.Invoke();
    }

    public void PreviewLine(string text)
    {
        ShowText(text);
        MenuClosed?.Invoke();
    }

    public void RefreshOcclusion(bool coveringHome)
    {
        var occluded = !_topmost && !coveringHome;
        if (occluded == _occluded)
        {
            return;
        }

        _occluded = occluded;
        VisibilityChanged?.Invoke(!_hidden && !_occluded);
    }

    public void Exit()
    {
        _exit = true;
        _ = SaveAsync();
        Exited?.Invoke();
    }

    public void Dispose()
    {
        _exit = true;
        _lifetime.Cancel();
        _dialogueClient.Dispose();
        _voice?.Dispose();
        _lifetime.Dispose();
    }

    private void ShowEvent(CompanionEvent trigger, DateTime localTime, FullscreenSnapshot fullscreen)
    {
        if (_exit)
        {
            return;
        }

        var startPump = false;
        lock (_replyQueue)
        {
            _replyRequests.Enqueue(new PendingReply(trigger, localTime, fullscreen));
            if (_replyPumpRunning == 0)
            {
                _replyPumpRunning = 1;
                startPump = true;
            }
        }

        if (startPump)
        {
            _ = Task.Run(PumpReplies);
        }
    }

    private void PumpReplies()
    {
        while (!_exit)
        {
            PendingReply request;
            lock (_replyQueue)
            {
                if (_replyRequests.Count == 0)
                {
                    _replyPumpRunning = 0;
                    if (_replyRequests.Count == 0)
                    {
                        return;
                    }

                    _replyPumpRunning = 1;
                }

                request = _replyRequests.Dequeue();
            }

            AgentReply reply;
            try
            {
                lock (_replyGate)
                {
                    reply = _dialogue.GetReply(request.Trigger, request.LocalTime, _random, request.Fullscreen);
                }
            }
            catch (Exception exception)
            {
                global::Android.Util.Log.Error("jiayi", exception.ToString());
                continue;
            }

            var presented = reply;
            _handler.Post(() => PresentReply(request, presented));
        }
    }

    private void PresentReply(PendingReply request, AgentReply reply)
    {
        if (_exit)
        {
            return;
        }

        try
        {
            _replyRevision++;
            if (reply.ShouldDisplayText)
            {
                var text = SpeechFragments.DropLeadingRepeat(reply.Text, _lastUtterance);
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = string.IsNullOrWhiteSpace(reply.Text) ? string.Empty : "我换一句。";
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    _lastUtterance = text.Trim();
                    ShowText(text);
                    NoteSpoken(text);
                    if (_voice is { Enabled: true } && reply.SourceLine?.SourceKind != "builtin_fallback")
                    {
                        SpeakLine(text, reply.SourceLine?.Tone, reply.SourceLine?.Trigger.ToString());
                    }
                }
            }

            if (request.Trigger != CompanionEvent.Automatic && reply.ShouldDisplayText)
            {
                ArmAutomatic(request.LocalTime, request.Fullscreen);
            }

            if (_dialogue.IsReady)
            {
                Schedule(TimeSpan.FromSeconds(2), () => _ = SaveMemoryAsync());
            }
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error("jiayi", exception.ToString());
        }
    }

    private void ShowText(string text, bool hold = false)
    {
        if (_awaitingReply && !hold)
        {
            return;
        }

        _bubbleVisible = true;
        _awaitingReply = hold;
        _bubbleHeld = hold || _voiceLine || _fingerHold;
        _bubbleRemaining = TimeSpan.FromSeconds(Math.Clamp(_parameters.BubbleSeconds, 2, 60));
        _bubbleDeadlineUtc = DateTime.UtcNow + _bubbleRemaining;
        BubbleRequested?.Invoke(text);
        if (!_bubbleHeld)
        {
            Schedule(_bubbleRemaining, HideBubbleIfDue);
        }
    }

    private void SpeakLine(string text, string? tone, string? trigger)
    {
        if (_voice is not { Enabled: true } || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _awaitingReply = false;
        _voiceLine = true;
        _bubbleHeld = true;
        VoiceCue?.Invoke(1, 0);
        _voice.Speak(text, tone, trigger, urgent: true);
    }

    private void ReleaseVoiceHold()
    {
        if (!_voiceLine)
        {
            return;
        }

        _voiceLine = false;
        MouthChanged?.Invoke(0);
        VoiceCue?.Invoke(0, 0);
        if (_awaitingReply || _fingerHold)
        {
            _bubbleHeld = true;
            return;
        }
        if (!_bubbleVisible)
        {
            return;
        }

        _bubbleHeld = false;
        _bubbleRemaining = TimeSpan.FromSeconds(Math.Clamp(_parameters.BubbleSeconds, 2, 60));
        _bubbleDeadlineUtc = DateTime.UtcNow + _bubbleRemaining;
        Schedule(_bubbleRemaining, HideBubbleIfDue);
    }

    private void HideBubbleIfDue()
    {
        if (!_bubbleVisible || _bubbleHeld || DateTime.UtcNow < _bubbleDeadlineUtc)
        {
            return;
        }

        _bubbleVisible = false;
        BubbleCleared?.Invoke();
    }

    private async Task RunWarmupAsync(bool retry = false)
    {
        var generation = ++_warmupGeneration;
        var outcome = retry
            ? await _warmup.RetryAfterFailureAsync(_lifetime.Token).ConfigureAwait(true)
            : await _warmup.StartAsync(_lifetime.Token).ConfigureAwait(true);
        if (_exit || generation != _warmupGeneration || generation == _appliedWarmup)
        {
            return;
        }

        _appliedWarmup = generation;
        if (outcome == DialogueWarmupOutcome.Ready)
        {
            SayLabel = "说句话 ♡";
            if (_replayStartup && _startupRevision == _replyRevision)
            {
                _replayStartup = false;
                ShowEvent(CompanionEvent.Startup, DateTime.Now, ObserveFullscreen());
            }

            Changed?.Invoke();
            return;
        }

        if (outcome is DialogueWarmupOutcome.PermanentFailure or DialogueWarmupOutcome.RetriesExhausted)
        {
            SayLabel = "重试文库 ♡";
            ShowText("文库没醒，点我重试");
            Changed?.Invoke();
        }
    }

    private void ReminderTick()
    {
        if (_exit || _hidden)
        {
            Schedule(TimeSpan.FromSeconds(15), ReminderTick);
            return;
        }

        _reminders ??= new PetReminderStore(_stateDirectory);
        var line = _reminders.TakeDue(DateTime.UtcNow);
        if (!string.IsNullOrWhiteSpace(line))
        {
            ShowText(line);
        }

        Schedule(TimeSpan.FromSeconds(15), ReminderTick);
    }

    private void EventTick()
    {
        if (_exit || _hidden)
        {
            return;
        }

        var now = DateTime.Now;
        var fullscreen = ObserveFullscreen();
        if (_cadence.RequiresModeRearm(now, fullscreen.EffectiveQuietMode))
        {
            ArmAutomatic(now, fullscreen);
        }

        _events ??= new CompanionEventPump(now, null);
        var trigger = _events.Poll(now, null, _dialogue.NextStoryDueAt);
        if (trigger is { } companionEvent)
        {
            ShowEvent(companionEvent, now, fullscreen);
        }

        Schedule(TimeSpan.FromSeconds(30), EventTick);
    }

    private void ArmAutomatic(DateTime localTime, FullscreenSnapshot fullscreen)
    {
        if (_hidden || _exit)
        {
            return;
        }

        var generation = ++_automaticGeneration;
        var delay = _cadence.Arm(localTime, fullscreen.EffectiveQuietMode);
        Schedule(delay < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : delay, () =>
        {
            if (generation == _automaticGeneration)
            {
                AutomaticTick();
            }
        });
    }

    private void DisarmAutomatic()
    {
        _automaticGeneration++;
        _cadence.Reset();
    }

    private void AutomaticTick()
    {
        if (_hidden || _exit)
        {
            return;
        }

        var now = DateTime.Now;
        var fullscreen = ObserveFullscreen();
        var evaluation = _cadence.Evaluate(now, fullscreen.EffectiveQuietMode);
        switch (evaluation.Decision)
        {
            case AutomaticCadenceDecision.Wait:
                Schedule(evaluation.Remaining, AutomaticTick);
                return;
            case AutomaticCadenceDecision.Speak:
                ShowEvent(CompanionEvent.Automatic, now, fullscreen);
                ArmAutomatic(now, fullscreen);
                return;
            default:
                ArmAutomatic(now, fullscreen);
                return;
        }
    }

    private FullscreenSnapshot ObserveFullscreen() =>
        _fullscreenState.Update(PetAwareness.Fullscreen);

    private void ScheduleGreeting()
    {
        if (_paused || _greeting != StartupGreetingPhase.Pending)
        {
            return;
        }

        _greeting = StartupGreetingPhase.Scheduled;
        _pendingAmbient = PetAmbientAction.Greeting;
        _ambientDue = _ambient.GetDeadline(TimeSpan.FromMilliseconds(650));
        _armedAmbient = ++_ambientGeneration;
        Schedule(TimeSpan.FromMilliseconds(650), AmbientTick);
    }

    private void ScheduleNextAmbient()
    {
        if (_paused || _hidden || _actions.State != PetActionState.Idle)
        {
            return;
        }

        if (_greeting == StartupGreetingPhase.Pending)
        {
            ScheduleGreeting();
            return;
        }

        if (_greeting != StartupGreetingPhase.Completed)
        {
            return;
        }

        _pendingAmbient = PetAmbientAction.Blink;
        var delay = _ambient.NextBlinkDelay();
        _ambientDue = _ambient.GetDeadline(delay);
        _armedAmbient = ++_ambientGeneration;
        Schedule(delay, AmbientTick);
    }

    private void AmbientTick()
    {
        if (_armedAmbient != _ambientGeneration || _paused || _hidden)
        {
            return;
        }

        var remaining = _ambient.GetRemaining(_ambientDue);
        if (remaining > TimeSpan.Zero)
        {
            Schedule(remaining, AmbientTick);
            return;
        }

        var action = _pendingAmbient;
        var startup = action == PetAmbientAction.Greeting && _greeting == StartupGreetingPhase.Scheduled;
        _ambientGeneration++;
        if (!_actions.TryBeginAmbient(action))
        {
            if (startup)
            {
                _greeting = StartupGreetingPhase.Pending;
            }

            return;
        }

        if (startup)
        {
            _greeting = StartupGreetingPhase.Running;
        }

        if (action == PetAmbientAction.Blink)
        {
            Blink?.Invoke(_ambient.ShouldDoubleBlink());
        }
        else
        {
            Greeting?.Invoke();
        }
    }

    private void PreserveGreeting()
    {
        if (_greeting == StartupGreetingPhase.Scheduled)
        {
            _greeting = StartupGreetingPhase.Pending;
        }
    }

    private void CancelAmbient() => _ambientGeneration++;

    private ScreenPoint DefaultOrigin()
    {
        var size = CharacterSize;
        return new ScreenPoint(
            WorkArea.Right - size - 24,
            WorkArea.Bottom - size - 24);
    }

    private void Clamp()
    {
        var clamped = ScreenPlacementService.ClampVisibleBounds(
            new ScreenPoint(_left, _top),
            new ScreenRect(0, 0, CharacterSize, CharacterSize),
            [WorkArea]);
        _left = clamped.X;
        _top = clamped.Y;
    }

    private readonly record struct PendingReply(
        CompanionEvent Trigger,
        DateTime LocalTime,
        FullscreenSnapshot Fullscreen);

    private void Schedule(TimeSpan delay, Action action)
    {
        if (_exit)
        {
            return;
        }

        var milliseconds = (long)Math.Clamp(delay.TotalMilliseconds, 1, int.MaxValue);
        _handler.PostDelayed(action, milliseconds);
    }

    private async Task SaveAsync()
    {
        if (!_ready)
        {
            return;
        }

        var settings = new PetSettings(_left, _top, Scale, _paused, _topmost)
        {
            DialogueEnabled = DialogueEnabled,
            Tuning = _parameters.ToTuning(VoiceEnabled)
        };
        _settings = settings;
        try
        {
            await _settingsService.SaveAsync(settings).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task SaveMemoryAsync()
    {
        if (!_dialogue.IsReady || _exit)
        {
            return;
        }

        try
        {
            await _memoryService.SaveAsync(_dialogue.CreateSnapshot()).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
