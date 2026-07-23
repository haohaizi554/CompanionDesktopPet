namespace CompanionDesktopPet.Services;

public sealed class DialogueService
{
    private readonly OfflineCompanionAgent _agent;

    public DialogueService(AgentMemorySnapshot? snapshot = null)
    {
        _agent = snapshot is null
            ? new OfflineCompanionAgent()
            : new OfflineCompanionAgent(snapshot);
    }

    public AgentMemorySnapshot CreateSnapshot() => _agent.CreateSnapshot();

    public AgentReply GetReply(CompanionEvent trigger, DateTime localTime, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return _agent.Respond(trigger, localTime, random);
    }
}
