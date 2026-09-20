using System.Text.Json;
using DecisionFabric.Core;
using DecisionFabric.Policy;

namespace DecisionFabric.Evals;

/// <summary>
/// One gate decision in a shape both suites share, so reporting and benchmarking
/// never need to know which policy produced it.
/// </summary>
internal sealed record GateDecisionRecord
{
    public required string Disposition { get; init; }
    public required double RequestedProbability { get; init; }
    public required string ObservedChoice { get; init; }
    public required double ChoiceConfidence { get; init; }
    public double? ScopeExpansion { get; init; }
    public required LinguisticRiskSignal LinguisticRiskSignals { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
}

/// <summary>
/// Which questions the report summarizes when a suite reports on a single
/// headline probability and a single headline choice.
/// </summary>
internal sealed record ReportingQuestions(string NoulQuestionId, string ChoiceQuestionId);

internal static class GateEvaluation
{
    public static GateDecisionRecord? Evaluate(
        EvaluationSuiteDefinition suite,
        JsonElement state,
        IReadOnlyDictionary<string, DecisionAnswer> answers)
    {
        if (suite.ActionPolicy is { } destructive)
        {
            return EvaluateDestructive(destructive, state, answers);
        }

        return suite.AgentActionPolicy is { } agentAction
            ? EvaluateAgentAction(agentAction, state, answers)
            : null;
    }

    /// <summary>
    /// The disposition that lets the action run unattended. Producing it when the
    /// label says otherwise is the one error class a safety gate cannot absorb.
    /// </summary>
    public static string? ResolvePermissiveDisposition(EvaluationSuiteDefinition suite) =>
        suite switch
        {
            { ActionPolicy: not null } => nameof(DestructiveActionDisposition.Authorized),
            { AgentActionPolicy: not null } => nameof(ProposedActionDisposition.Allow),
            _ => null
        };

    public static ReportingQuestions ResolveReportingQuestions(EvaluationSuiteDefinition suite) =>
        suite switch
        {
            { ActionPolicy: { } destructive } =>
                new ReportingQuestions(destructive.RequestQuestionId, destructive.IntentQuestionId),
            { AgentActionPolicy: { } agentAction } =>
                new ReportingQuestions(agentAction.RequestQuestionId, agentAction.ImpactQuestionId),
            _ => new ReportingQuestions("block_card_requested", "primary_intent")
        };

    private static GateDecisionRecord EvaluateDestructive(
        ActionPolicyDefinition policy,
        JsonElement state,
        IReadOnlyDictionary<string, DecisionAnswer> answers)
    {
        var message = state.GetProperty(policy.MessageStateProperty).GetString() ?? string.Empty;
        var decision = DestructiveActionGate.Evaluate(
            new DestructiveActionEvidence
            {
                ActionRequested = (NoulAnswer)answers[policy.RequestQuestionId],
                PrimaryIntent = (ChoiceAnswer)answers[policy.IntentQuestionId],
                LinguisticRiskSignals = LinguisticRiskDetector.Detect(message)
            },
            policy.ToGateOptions());

        return new GateDecisionRecord
        {
            Disposition = decision.Disposition.ToString(),
            RequestedProbability = decision.RequestedProbability,
            ObservedChoice = decision.ObservedIntent,
            ChoiceConfidence = decision.IntentConfidence,
            LinguisticRiskSignals = decision.LinguisticRiskSignals,
            Reasons = decision.Reasons
        };
    }

    private static GateDecisionRecord EvaluateAgentAction(
        AgentActionPolicyDefinition policy,
        JsonElement state,
        IReadOnlyDictionary<string, DecisionAnswer> answers)
    {
        var instruction = state.GetProperty(policy.InstructionStateProperty).GetString() ?? string.Empty;
        var decision = ProposedActionGate.Evaluate(
            new ProposedActionEvidence
            {
                ActionRequestedByUser = (NoulAnswer)answers[policy.RequestQuestionId],
                ActionImpact = (ChoiceAnswer)answers[policy.ImpactQuestionId],
                ScopeExpansion = (ScoreAnswer)answers[policy.ScopeQuestionId],
                LinguisticRiskSignals = LinguisticRiskDetector.Detect(instruction)
            },
            policy.ToGateOptions());

        return new GateDecisionRecord
        {
            Disposition = decision.Disposition.ToString(),
            RequestedProbability = decision.RequestedProbability,
            ObservedChoice = decision.ObservedImpact,
            ChoiceConfidence = decision.ImpactConfidence,
            ScopeExpansion = decision.ScopeExpansion,
            LinguisticRiskSignals = decision.LinguisticRiskSignals,
            Reasons = decision.Reasons
        };
    }
}
