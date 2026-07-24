namespace CompanionDesktopPet.Services;

public sealed class DialogueService
{
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
        _initialSnapshot = snapshot?.DetachedCopy();
        _timeProvider = TimeProvider.System;
        _agent = _initialSnapshot is null
            ? new OfflineCompanionAgent()
            : new OfflineCompanionAgent(_initialSnapshot);
    }

    private DialogueService(
        AgentMemorySnapshot? snapshot,
        Func<AgentMemorySnapshot?, ICompanionDialogueAgent> agentFactory,
        TimeProvider? timeProvider)
    {
        _initialSnapshot = snapshot?.DetachedCopy();
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
                return agent.CreateSnapshot().DetachedCopy();
            }

            return (_initialSnapshot ?? CreateFallbackSnapshot()).DetachedCopy();
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
        lock (_sync)
        {
            return _agent is { } agent
                ? agent.Respond(trigger, localTime, random)
                : GetFallbackReply(trigger);
        }
    }

    private bool InitializeAgent()
    {
        try
        {
            var agent = _agentFactory!(_initialSnapshot?.DetachedCopy())
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
            CompanionEvent.Startup => FallbackDialogueCatalog.StartupLine,
            CompanionEvent.Click => FallbackDialogueCatalog.ClickLines[
                (int)((uint)Interlocked.Increment(ref _fallbackClickIndex) - 1)
                % FallbackDialogueCatalog.ClickLines.Count],
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

    private static bool IsFatalException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException;
}
