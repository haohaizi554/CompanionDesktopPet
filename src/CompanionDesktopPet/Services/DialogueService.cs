namespace CompanionDesktopPet.Services;

public sealed class DialogueService
{
    private static readonly IReadOnlyList<DialogueLine> FallbackClickLines =
    [
        FallbackLine("fallback-click-01", "在呢，别戳啦。", "fallback.click.01"),
        FallbackLine("fallback-click-02", "嗯嗯，我听见了。", "fallback.click.02"),
        FallbackLine("fallback-click-03", "脑袋加载中，等下。", "fallback.click.03"),
        FallbackLine("fallback-click-04", "先陪你一下，马上好。", "fallback.click.04")
    ];

    private static readonly DialogueLine FallbackStartupLine = FallbackLine(
        "fallback-startup-01",
        "我先醒醒，马上就好。",
        "fallback.startup",
        DialogueTrigger.AppStart);

    private readonly object _sync = new();
    private readonly AgentMemorySnapshot? _initialSnapshot;
    private readonly Func<AgentMemorySnapshot?, ICompanionDialogueAgent>? _agentFactory;
    private readonly TimeProvider _timeProvider;
    private ICompanionDialogueAgent? _agent;
    private Task<bool>? _warmupTask;
    private AgentMemorySnapshot? _fallbackSnapshot;
    private Exception? _lastWarmupException;
    private int _fallbackClickIndex;

    public DialogueService(AgentMemorySnapshot? snapshot = null)
    {
        _initialSnapshot = snapshot;
        _timeProvider = TimeProvider.System;
        _agent = snapshot is null
            ? new OfflineCompanionAgent()
            : new OfflineCompanionAgent(snapshot);
    }

    private DialogueService(
        AgentMemorySnapshot? snapshot,
        Func<AgentMemorySnapshot?, ICompanionDialogueAgent> agentFactory,
        TimeProvider? timeProvider)
    {
        _initialSnapshot = snapshot;
        _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal static DialogueService CreateDeferred(
        Func<AgentMemorySnapshot?, ICompanionDialogueAgent>? agentFactory = null,
        TimeProvider? timeProvider = null) =>
        CreateDeferred(null, agentFactory, timeProvider);

    internal static DialogueService CreateDeferred(
        AgentMemorySnapshot? snapshot,
        Func<AgentMemorySnapshot?, ICompanionDialogueAgent>? agentFactory = null,
        TimeProvider? timeProvider = null) =>
        new(snapshot, agentFactory ?? CreateWarmAgent, timeProvider);

    internal bool IsReady
    {
        get
        {
            lock (_sync)
            {
                return _agent is not null;
            }
        }
    }

    internal Exception? LastWarmupException
    {
        get
        {
            lock (_sync)
            {
                return _lastWarmupException;
            }
        }
    }

    public AgentMemorySnapshot CreateSnapshot()
    {
        lock (_sync)
        {
            if (_agent is { } agent)
            {
                return agent.CreateSnapshot();
            }

            return _initialSnapshot ?? CreateFallbackSnapshot();
        }
    }

    public DateTime? NextStoryDueAt
    {
        get
        {
            lock (_sync)
            {
                if (_agent is { } agent)
                {
                    return agent.NextStoryDueAt;
                }

                var stories = (_initialSnapshot ?? _fallbackSnapshot)?.State.ActiveStories;
                return stories is { Count: > 0 }
                    ? stories.Min(story => story.DueAt)
                    : null;
            }
        }
    }

    internal Task<bool> WarmupAsync()
    {
        lock (_sync)
        {
            if (_agent is not null)
            {
                return Task.FromResult(true);
            }

            if (_warmupTask is { IsCompleted: false })
            {
                return _warmupTask;
            }

            _warmupTask = Task.Run(InitializeAgent);
            return _warmupTask;
        }
    }

    public AgentReply GetReply(CompanionEvent trigger, DateTime localTime, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        ICompanionDialogueAgent? agent;
        lock (_sync)
        {
            agent = _agent;
        }

        return agent is null
            ? GetFallbackReply(trigger)
            : agent.Respond(trigger, localTime, random);
    }

    private bool InitializeAgent()
    {
        try
        {
            var agent = _agentFactory!(_initialSnapshot)
                ?? throw new InvalidOperationException("The dialogue factory returned no agent.");
            lock (_sync)
            {
                _agent = agent;
                _lastWarmupException = null;
            }

            return true;
        }
        catch (Exception exception) when (!IsFatalException(exception))
        {
            lock (_sync)
            {
                _lastWarmupException = exception;
            }

            return false;
        }
    }

    private AgentReply GetFallbackReply(CompanionEvent trigger)
    {
        DialogueLine? line = trigger switch
        {
            CompanionEvent.Startup => FallbackStartupLine,
            CompanionEvent.Click => FallbackClickLines[
                (int)((uint)Interlocked.Increment(ref _fallbackClickIndex) - 1)
                % FallbackClickLines.Count],
            _ => null
        };
        if (line is null)
        {
            return new AgentReply(
                string.Empty,
                DialogueCategory.CharacterLife,
                DialogueTreeKind.Companion,
                trigger,
                SceneId: "fallback:silence",
                Expression: SceneExpression.ActionOnly,
                ShouldDisplayText: false,
                SemanticGroup: "fallback.silence");
        }

        return new AgentReply(
            line.Text,
            line.Category,
            DialogueTreeKind.Companion,
            trigger,
            SceneId: $"fallback:{line.SemanticGroup}",
            Expression: SceneExpression.SelfTalk,
            ShouldDisplayText: true,
            SourceLine: line,
            SemanticGroup: line.SemanticGroup);
    }

    private AgentMemorySnapshot CreateFallbackSnapshot()
    {
        _fallbackSnapshot ??= new AgentMemorySnapshot(
            CharacterState.Create(_timeProvider.GetLocalNow().LocalDateTime),
            [],
            0,
            null,
            []);
        return _fallbackSnapshot;
    }

    private static ICompanionDialogueAgent CreateWarmAgent(AgentMemorySnapshot? snapshot)
    {
        var compatibleSnapshot = snapshot is null
            ? null
            : AgentMemoryService.ReconcileForRuntime(snapshot);
        var agent = compatibleSnapshot is null
            ? new OfflineCompanionAgent()
            : new OfflineCompanionAgent(compatibleSnapshot);
        agent.WarmUp();
        return agent;
    }

    private static DialogueLine FallbackLine(
        string id,
        string text,
        string semanticGroup,
        DialogueTrigger trigger = DialogueTrigger.Any) =>
        new(
            id,
            DialogueCategory.CharacterLife,
            DialogueCategoryGroup.CharacterLife,
            "fallback.local",
            semanticGroup,
            DialogueOutputMode.SelfTalk,
            trigger,
            ["none"],
            "dry_warm",
            0,
            1,
            1,
            2,
            1,
            false,
            true,
            text,
            "builtin_fallback",
            "builtin:fallback",
            "cold-start safety fallback");

    private static bool IsFatalException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException;
}
