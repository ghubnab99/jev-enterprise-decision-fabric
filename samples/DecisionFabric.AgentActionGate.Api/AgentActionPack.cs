using System.Text.Json;
using DecisionFabric.Core;
using DecisionFabric.Policy;
using Microsoft.Extensions.Options;

namespace DecisionFabric.AgentActionGate.Api;

internal sealed record AgentActionInput(string UserInstruction, string ToolName, string ToolArguments);

internal sealed record AgentActionOutcome(
    ProposedActionDisposition Disposition,
    LinguisticRiskSignal RiskSignals,
    IReadOnlyList<string> Reasons);

internal sealed class AgentActionPack : IDecisionPack<AgentActionInput, AgentActionOutcome>
{
    private readonly ProposedActionGateOptions _gateOptions;

    public AgentActionPack(IOptions<AgentActionPolicyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _gateOptions = options.Value.ToGateOptions();
        PolicyVersion = PolicyFingerprint.Create("agent-action-gate", _gateOptions);
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

        var decision = ProposedActionGate.Evaluate(
            new ProposedActionEvidence
            {
                ActionRequestedByUser = evidence.Noul(AgentActionContract.ActionRequestedByUser),
                ActionImpact = evidence.Choice(AgentActionContract.ActionImpact),
                ScopeExpansion = evidence.Score(AgentActionContract.ScopeExpansion),
                LinguisticRiskSignals = LinguisticRiskDetector.Detect(input.UserInstruction)
            },
            _gateOptions);

        return new AgentActionOutcome(
            decision.Disposition,
            decision.LinguisticRiskSignals,
            decision.Reasons);
    }
}
