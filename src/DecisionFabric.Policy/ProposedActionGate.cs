using DecisionFabric.Core;

namespace DecisionFabric.Policy;

public enum ProposedActionDisposition
{
    Deny,
    RequireApproval,
    Allow
}

public sealed record ProposedActionEvidence
{
    public required NoulAnswer ActionRequestedByUser { get; init; }
    public required ChoiceAnswer ActionImpact { get; init; }
    public required ScoreAnswer ScopeExpansion { get; init; }
    public LinguisticRiskSignal LinguisticRiskSignals { get; init; }
}

/// <summary>
/// Authorization parameters for a proposed agent tool call. The impact vocabulary
/// is supplied by the caller so the gate stays independent of any one contract.
/// </summary>
public sealed record ProposedActionGateOptions
{
    public required NoulPolicyThresholds RequestThresholds { get; init; }

    /// <summary>The impact choice that may be allowed without any further checks.</summary>
    public required string ReadOnlyImpact { get; init; }

    /// <summary>Impact choices that always require a human, however confident the evidence is.</summary>
    public required IReadOnlySet<string> ApprovalRequiredImpacts { get; init; }

    public double MinimumImpactConfidence { get; init; } = 0.8;

    /// <summary>Highest scope-expansion score allowed without approval.</summary>
    public double MaximumAutoApprovedScopeExpansion { get; init; } = 0.5;

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(RequestThresholds);
        RequestThresholds.Validate();

        if (string.IsNullOrWhiteSpace(ReadOnlyImpact))
        {
            throw new ArgumentException("A read-only impact choice must be configured.", nameof(ReadOnlyImpact));
        }

        ArgumentNullException.ThrowIfNull(ApprovalRequiredImpacts);
        if (ApprovalRequiredImpacts.Contains(ReadOnlyImpact))
        {
            throw new ArgumentException(
                $"The read-only impact '{ReadOnlyImpact}' cannot also require approval.",
                nameof(ApprovalRequiredImpacts));
        }

        if (MinimumImpactConfidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumImpactConfidence),
                "Minimum impact confidence must be between 0 and 1.");
        }

        if (MaximumAutoApprovedScopeExpansion < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumAutoApprovedScopeExpansion),
                "Maximum auto-approved scope expansion cannot be negative.");
        }
    }
}

public sealed record ProposedActionDecision(
    ProposedActionDisposition Disposition,
    double RequestedProbability,
    string ObservedImpact,
    double ImpactConfidence,
    double ScopeExpansion,
    LinguisticRiskSignal LinguisticRiskSignals,
    IReadOnlyList<string> Reasons);

/// <summary>
/// Decides whether a proposed agent tool call may run unattended. Every route is
/// driven by separately elicited evidence, so a confident answer to one question
/// can never silently stand in for another.
/// </summary>
public static class ProposedActionGate
{
    public static ProposedActionDecision Evaluate(
        ProposedActionEvidence evidence,
        ProposedActionGateOptions options)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(evidence.ActionRequestedByUser);
        ArgumentNullException.ThrowIfNull(evidence.ActionImpact);
        ArgumentNullException.ThrowIfNull(evidence.ScopeExpansion);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var requested = evidence.ActionRequestedByUser.Noul;
        if (requested is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(evidence),
                "The user-request probability must be between 0 and 1.");
        }

        var impact = evidence.ActionImpact;
        if (impact.Confidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(evidence),
                "Impact confidence must be between 0 and 1.");
        }

        var scope = evidence.ScopeExpansion.Score;
        var impactIsConfident = impact.Confidence >= options.MinimumImpactConfidence;

        if (string.Equals(impact.Choice, options.ReadOnlyImpact, StringComparison.Ordinal) && impactIsConfident)
        {
            return Create(
                ProposedActionDisposition.Allow,
                evidence,
                [$"Read-only action with impact confidence {impact.Confidence:F3}."]);
        }

        if (requested <= options.RequestThresholds.NegativeAtOrBelow)
        {
            return Create(
                ProposedActionDisposition.Deny,
                evidence,
                [$"The user-request probability {requested:F3} is at or below the negative boundary " +
                 $"{options.RequestThresholds.NegativeAtOrBelow:F3}."]);
        }

        var approvalReasons = new List<string>();
        if (requested < options.RequestThresholds.PositiveAtOrAbove)
        {
            approvalReasons.Add($"The user-request probability {requested:F3} is inside the uncertainty band.");
        }

        if (options.ApprovalRequiredImpacts.Contains(impact.Choice))
        {
            approvalReasons.Add($"Impact '{impact.Choice}' always requires human approval.");
        }

        if (!impactIsConfident)
        {
            approvalReasons.Add(
                $"Impact confidence {impact.Confidence:F3} is below the approval-free minimum " +
                $"{options.MinimumImpactConfidence:F3}.");
        }

        if (scope > options.MaximumAutoApprovedScopeExpansion)
        {
            approvalReasons.Add(
                $"Scope expansion {scope:F2} exceeds the approval-free maximum " +
                $"{options.MaximumAutoApprovedScopeExpansion:F2}.");
        }

        if (evidence.LinguisticRiskSignals != LinguisticRiskSignal.None)
        {
            approvalReasons.Add(
                $"Linguistic risk detected in the user instruction: {evidence.LinguisticRiskSignals}.");
        }

        return approvalReasons.Count == 0
            ? Create(
                ProposedActionDisposition.Allow,
                evidence,
                ["Requested, reversible, in-scope action passed every approval-free check."])
            : Create(ProposedActionDisposition.RequireApproval, evidence, approvalReasons);
    }

    private static ProposedActionDecision Create(
        ProposedActionDisposition disposition,
        ProposedActionEvidence evidence,
        IReadOnlyList<string> reasons) =>
        new(
            disposition,
            evidence.ActionRequestedByUser.Noul,
            evidence.ActionImpact.Choice,
            evidence.ActionImpact.Confidence,
            evidence.ScopeExpansion.Score,
            evidence.LinguisticRiskSignals,
            reasons);
}
