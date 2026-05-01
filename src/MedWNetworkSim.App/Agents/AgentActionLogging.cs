namespace MedWNetworkSim.App.Agents;

public sealed record AgentActionLogEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid AgentId { get; init; }
    public string ActorId { get; init; } = string.Empty;
    public string AgentName { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public int SimulationTick { get; init; }
    public string ActionType { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
    public Dictionary<string, double> StateMetrics { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string DecisionSummary { get; init; } = string.Empty;
    public List<string> DecisionFactors { get; init; } = [];
    public List<string>? AlternativesConsidered { get; init; }
    public string Outcome { get; init; } = string.Empty;
    public double? UtilityScore { get; init; }
}

public interface IAgentActionLogger
{
    void Log(AgentActionLogEntry entry);
    IReadOnlyList<AgentActionLogEntry> GetAll();
    IReadOnlyList<AgentActionLogEntry> GetByAgent(Guid agentId);
    void Clear();
}

public sealed class AgentActionLogger : IAgentActionLogger
{
    private readonly List<AgentActionLogEntry> entries = [];
    private readonly object gate = new();

    public void Log(AgentActionLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (gate)
        {
            entries.Add(entry);
        }
    }

    public IReadOnlyList<AgentActionLogEntry> GetAll()
    {
        lock (gate)
        {
            return entries.ToList();
        }
    }

    public IReadOnlyList<AgentActionLogEntry> GetByAgent(Guid agentId)
    {
        lock (gate)
        {
            return entries.Where(e => e.AgentId == agentId).ToList();
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            entries.Clear();
        }
    }
}
