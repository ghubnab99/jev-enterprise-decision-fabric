using System.Text.Json;
using DecisionFabric.Core;
using DecisionFabric.Policy;
using Microsoft.Extensions.Options;

namespace DecisionFabric.AgentActionGate.Api;

internal sealed record AgentActionInput(string UserInstruction, string ToolName, string ToolArguments);

public enum AgentActionDisposition
{
    Deny,
    RequireApproval,
    Allow
}

internal sealed record AgentActionOutcome(
    AgentActionDisposition Disposition,
    LinguisticRiskSignal RiskSignals,
    IReadOnlyList<string> Reasons);

internal sealed record AgentActionPolicy(
    NoulPolicyThresholds RequestThresholds,
    double MinimumImpactConfidence,
    double MaximumAutoApprovedScopeExpansion);

internal sealed class AgentActionPack : IDecisionPack<AgentActionInput, AgentActionOutcome>
{
    private readonly AgentActionPolicy _policy;

    public AgentActionPack(IOptions<AgentActionPolicyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _policy = options.Value.ToPolicy();
        PolicyVersion = PolicyFingerprint.Create("agent-action-gate", _policy);
    }

    public DecisionContract Contract => AgentActionContract.Definition;

    public string PolicyVersion { get; }

    public JsonElement CreateState(AgentActionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return JsonSerializer.SerializeToElement(new Dictionary<string, string>
        {
            ["user_instruction"] = input.UserInstruction,
            ["proposed_tool"] = input.ToolName,
            ["proposed_arguments"] = input.ToolArguments
        });
    }

    public AgentActionOutcome Decide(AgentActionInput input, DecisionEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(evidence);

        var requested = evidence.Noul(AgentActionContract.ActionRequestedByUser).Noul;
        var impact = evidence.Choice(AgentActionContract.ActionImpact);
        var scope = evidence.Score(AgentActionContract.ScopeExpansion).Score;
        var riskSignals = LinguisticRiskDetector.Detect(input.UserInstruction);
        var impactIsConfident = impact.Confidence >= _policy.MinimumImpactConfidence;

        if (impact.Choice == AgentActionContract.ReadOnly && impactIsConfident)
        {
            return new(AgentActionDisposition.Allow, riskSignals,
                [$"Read-only action with impact confidence {impact.Confidence:F3}."]);
        }

        if (requested <= _policy.RequestThresholds.NegativeAtOrBelow)
        {
            return new(AgentActionDisposition.Deny, riskSignals,
                [$"The user-request probability {requested:F3} is at or below the negative boundary " +
                 $"{_policy.RequestThresholds.NegativeAtOrBelow:F3}."]);
        }

        var approvalReasons = new List<string>();
        if (requested < _policy.RequestThresholds.PositiveAtOrAbove)
        {
            approvalReasons.Add($"The user-request probability {requested:F3} is inside the uncertainty band.");
        }

        if (impact.Choice is AgentActionContract.IrreversibleChange or AgentActionContract.ExternalCommunication)
        {
            approvalReasons.Add($"Impact '{impact.Choice}' always requires human approval.");
        }

        if (!impactIsConfident)
        {
            approvalReasons.Add(
                $"Impact confidence {impact.Confidence:F3} is below the approval-free minimum " +
                $"{_policy.MinimumImpactConfidence:F3}.");
        }

        if (scope > _policy.MaximumAutoApprovedScopeExpansion)
        {
            approvalReasons.Add(
                $"Scope expansion {scope:F2} exceeds the approval-free maximum " +
                $"{_policy.MaximumAutoApprovedScopeExpansion:F2}.");
        }

        if (riskSignals != LinguisticRiskSignal.None)
        {
            approvalReasons.Add($"Linguistic risk detected in the user instruction: {riskSignals}.");
        }

        return approvalReasons.Count == 0
            ? new(AgentActionDisposition.Allow, riskSignals,
                ["Requested, reversible, in-scope action passed every approval-free check."])
            : new(AgentActionDisposition.RequireApproval, riskSignals, approvalReasons);
    }
}
