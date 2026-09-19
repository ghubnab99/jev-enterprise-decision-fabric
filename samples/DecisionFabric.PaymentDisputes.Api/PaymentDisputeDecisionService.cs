using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DecisionFabric.Core;
using DecisionFabric.Policy;

namespace DecisionFabric.PaymentDisputes.Api;

internal sealed class PaymentDisputeDecisionService(
    IDecisionFabric fabric,
    PaymentDisputePack pack,
    PaymentDisputeDecisionStore store,
    IDecisionAuditSink auditSink)
{
    public async Task<PaymentDisputeDecisionResponse> TriageAsync(
        string customerMessage,
        CancellationToken cancellationToken = default)
    {
        using var activity = DecisionTelemetry.ActivitySource.StartActivity(
            "payment_dispute.triage",
            ActivityKind.Internal);
        var result = await fabric.EvaluateAsync(
            pack,
            new PaymentDisputeInput(customerMessage),
            cancellationToken);

        var blockCardRequested = result.Evidence.Noul(PaymentDisputeContract.BlockCardRequested);
        var primaryIntent = result.Evidence.Choice(PaymentDisputeContract.PrimaryIntent);
        var urgency = result.Evidence.Score(PaymentDisputeContract.Urgency);
        var riskSignals = result.Outcome.RiskSignals;
        var policyDecision = result.Outcome.Gate;
        var (authorizationState, nextAction) = MapAuthorization(policyDecision.Disposition);
        var now = DateTimeOffset.UtcNow;
        var decision = new PaymentDisputeDecisionResponse
        {
            DecisionId = $"dec_{Guid.NewGuid():N}",
            PolicyDisposition = policyDecision.Disposition,
            AuthorizationState = authorizationState,
            NextAction = nextAction,
            Evidence = new PaymentDisputeEvidence
            {
                BlockCardRequested = blockCardRequested.Noul,
                PrimaryIntent = primaryIntent.Choice,
                PrimaryIntentConfidence = primaryIntent.Confidence,
                Urgency = urgency.Score,
                UrgencyConfidence = urgency.Confidence
            },
            RiskSignals = ExpandSignals(riskSignals),
            PolicyReasons = policyDecision.Reasons,
            Model = result.Model,
            ContractId = result.ContractId,
            ContractVersion = result.ContractVersion,
            DurationMilliseconds = result.Duration.TotalMilliseconds,
            InputTokens = result.Usage.InputTokens,
            OutputTokens = result.Usage.OutputTokens,
            CreatedAt = now
        };
        var inputSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(customerMessage)));
        store.Add(new StoredPaymentDisputeDecision(decision, inputSha256));

        DecisionTelemetry.RecordEvaluation(
            decision.PolicyDisposition,
            decision.Model,
            decision.DurationMilliseconds);
        activity?.SetTag("decision.id", decision.DecisionId);
        activity?.SetTag("decision.contract.id", decision.ContractId);
        activity?.SetTag("decision.contract.version", decision.ContractVersion);
        activity?.SetTag("decision.policy.disposition", decision.PolicyDisposition.ToString());
        activity?.SetTag("decision.authorization.state", decision.AuthorizationState.ToString());
        activity?.SetTag("gen_ai.response.model", decision.Model);

        await auditSink.WriteAsync(
            CreateAuditEvent("evaluated", decision, inputSha256, now),
            cancellationToken);
        return decision;
    }

    public PaymentDisputeDecisionResponse? Get(string decisionId) =>
        store.Get(decisionId)?.Response;

    public async Task<ConfirmationResult> ConfirmAsync(
        string decisionId,
        CancellationToken cancellationToken = default)
    {
        using var activity = DecisionTelemetry.ActivitySource.StartActivity(
            "payment_dispute.confirm",
            ActivityKind.Internal);
        var confirmedAt = DateTimeOffset.UtcNow;
        var result = store.Confirm(decisionId, confirmedAt);
        activity?.SetTag("decision.id", decisionId);
        activity?.SetTag("decision.confirmation.status", result.Status.ToString());

        if (result.Status == ConfirmationStatus.Confirmed && result.Decision is { } decision)
        {
            var inputSha256 = store.Get(decisionId)!.InputSha256;
            DecisionTelemetry.RecordConfirmation();
            await auditSink.WriteAsync(
                CreateAuditEvent("confirmed", decision, inputSha256, confirmedAt),
                cancellationToken);
        }

        return result;
    }

    private static (DecisionAuthorizationState State, PaymentDisputeNextAction NextAction)
        MapAuthorization(DestructiveActionDisposition disposition) =>
        disposition switch
        {
            DestructiveActionDisposition.NotAuthorized =>
                (DecisionAuthorizationState.NotAuthorized, PaymentDisputeNextAction.NoAction),
            DestructiveActionDisposition.RequireConfirmation =>
                (DecisionAuthorizationState.AwaitingConfirmation, PaymentDisputeNextAction.RequestCustomerConfirmation),
            DestructiveActionDisposition.Authorized =>
                (DecisionAuthorizationState.AuthorizedByPolicy, PaymentDisputeNextAction.BlockCard),
            _ => throw new ArgumentOutOfRangeException(nameof(disposition))
        };

    private static string[] ExpandSignals(LinguisticRiskSignal signals) =>
        Enum.GetValues<LinguisticRiskSignal>()
            .Where(signal => signal != LinguisticRiskSignal.None && signals.HasFlag(signal))
            .Select(signal => signal.ToString())
            .ToArray();

    private static DecisionAuditEvent CreateAuditEvent(
        string eventType,
        PaymentDisputeDecisionResponse decision,
        string inputSha256,
        DateTimeOffset occurredAt) =>
        new(
            eventType,
            decision.DecisionId,
            inputSha256,
            decision.Model,
            decision.ContractVersion,
            decision.PolicyDisposition,
            decision.AuthorizationState,
            occurredAt);
}
