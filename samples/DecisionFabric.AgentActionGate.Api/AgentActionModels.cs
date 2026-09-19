namespace DecisionFabric.AgentActionGate.Api;

public sealed record EvaluateAgentActionRequest
{
    public required string UserInstruction { get; init; }
    public required string ToolName { get; init; }

    /// <summary>The proposed tool arguments, typically serialized JSON.</summary>
    public required string ToolArguments { get; init; }
}

public sealed record AgentActionEvidence
{
    public required double ActionRequestedByUser { get; init; }
    public required string ActionImpact { get; init; }
    public required double ActionImpactConfidence { get; init; }
    public required double ScopeExpansion { get; init; }
    public required double ScopeExpansionConfidence { get; init; }
}

public sealed record AgentActionDecisionResponse
{
    public required string DecisionId { get; init; }
    public required AgentActionDisposition Disposition { get; init; }
    public required AgentActionEvidence Evidence { get; init; }
    public required IReadOnlyList<string> RiskSignals { get; init; }
    public required IReadOnlyList<string> PolicyReasons { get; init; }
    public required string Model { get; init; }
    public required string ContractId { get; init; }
    public required string ContractVersion { get; init; }
    public required string PolicyVersion { get; init; }
    public required double DurationMilliseconds { get; init; }
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
}
