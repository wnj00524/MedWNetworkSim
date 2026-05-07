namespace MedWNetworkSim.App.Agents;
/// <summary>
/// Represents the agent action log entry component.
/// </summary>

public sealed record AgentActionLogEntry
{
    /// <summary>
    /// Gets or sets the unique identifier for this instance.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the agent id.
    /// </summary>
    public Guid AgentId { get; init; }
    /// <summary>
    /// Gets or sets the actor id.
    /// </summary>
    public string ActorId { get; init; } = string.Empty;
    /// <summary>
    /// Gets or sets the agent name.
    /// </summary>
    public string AgentName { get; init; } = string.Empty;
    /// <summary>
    /// Gets or sets the timestamp.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    /// <summary>
    /// Gets or sets the simulation tick.
    /// </summary>
    public int SimulationTick { get; init; }
    /// <summary>
    /// Gets or sets the action type.
    /// </summary>
    public string ActionType { get; init; } = string.Empty;
    /// <summary>
    /// Gets or sets the target id.
    /// </summary>
    public string TargetId { get; init; } = string.Empty;
    /// <summary>
    /// Gets or sets the state metrics.
    /// </summary>
    public Dictionary<string, double> StateMetrics { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or sets the decision summary.
    /// </summary>
    public string DecisionSummary { get; init; } = string.Empty;
    /// <summary>
    /// Gets the collection of decision factors associated with this entity.
    /// </summary>
    public List<string> DecisionFactors { get; init; } = [];
    /// <summary>
    /// Gets the collection of alternatives considered associated with this entity.
    /// </summary>
    public List<string>? AlternativesConsidered { get; init; }
    /// <summary>
    /// Gets or sets the outcome.
    /// </summary>
    public string Outcome { get; init; } = string.Empty;
    /// <summary>
    /// Gets or sets the utility score.
    /// </summary>
    public double? UtilityScore { get; init; }
}
/// <summary>
/// Defines the contract and required members for iagent action logger implementations.
/// </summary>

public interface IAgentActionLogger
{
    void Log(AgentActionLogEntry entry);
    IReadOnlyList<AgentActionLogEntry> GetAll();
    IReadOnlyList<AgentActionLogEntry> GetByAgent(Guid agentId);
    void Clear();
}
/// <summary>
/// Represents the agent action logger component.
/// </summary>

public sealed class AgentActionLogger : IAgentActionLogger
{
    private readonly List<AgentActionLogEntry> entries = [];
    private readonly object gate = new();
    /// <summary>
    /// Executes the log operation.
    /// </summary>

    public void Log(AgentActionLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (gate)
        {
            entries.Add(entry);
        }
    }
    /// <summary>
    /// Retrieves the all based on the provided parameters.
    /// </summary>

    public IReadOnlyList<AgentActionLogEntry> GetAll()
    {
        lock (gate)
        {
            return entries.ToList();
        }
    }
    /// <summary>
    /// Retrieves the by agent based on the provided parameters.
    /// </summary>

    public IReadOnlyList<AgentActionLogEntry> GetByAgent(Guid agentId)
    {
        lock (gate)
        {
            return entries.Where(e => e.AgentId == agentId).ToList();
        }
    }
    /// <summary>
    /// Executes the clear operation.
    /// </summary>

    public void Clear()
    {
        lock (gate)
        {
            entries.Clear();
        }
    }
}
