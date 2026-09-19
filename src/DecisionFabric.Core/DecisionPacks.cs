using System.Text.Json;

namespace DecisionFabric.Core;

/// <summary>
/// A domain decision: one versioned semantic contract, the mapping from domain
/// input to model state, and the deterministic policy that turns validated
/// evidence into a domain outcome.
/// </summary>
public interface IDecisionPack<in TInput, out TOutcome>
{
    DecisionContract Contract { get; }

    /// <summary>
    /// Identifies the exact policy parameters used by <see cref="Decide"/> so that
    /// a threshold change is visible in results and audits.
    /// </summary>
    string PolicyVersion { get; }

    JsonElement CreateState(TInput input);

    TOutcome Decide(TInput input, DecisionEvidence evidence);
}

public sealed record DecisionResult<TOutcome>
{
    public required TOutcome Outcome { get; init; }
    public required DecisionEvidence Evidence { get; init; }
    public required string Model { get; init; }
    public required string ContractId { get; init; }
    public required string ContractVersion { get; init; }
    public required string PolicyVersion { get; init; }
    public required DecisionUsage Usage { get; init; }
    public required TimeSpan Duration { get; init; }
}

public interface IDecisionFabric
{
    /// <summary>
    /// Evaluates every question in the pack's contract in one provider request,
    /// validates the answers against the contract and applies the pack's policy.
    /// </summary>
    Task<DecisionResult<TOutcome>> EvaluateAsync<TInput, TOutcome>(
        IDecisionPack<TInput, TOutcome> pack,
        TInput input,
        CancellationToken cancellationToken = default);
}
