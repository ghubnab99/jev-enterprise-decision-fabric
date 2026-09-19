using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DecisionFabric.Core;
using DecisionFabric.Policy;

namespace DecisionFabric.PaymentDisputes.Api;

internal sealed class PaymentDisputeDecisionService(
    IDecisionProvider provider,
    PaymentDisputeDecisionStore store,
    IDecisionAuditSink auditSink,
    IConfiguration configuration)
{
    private static readonly DestructiveActionGateOptions GateOptions = new()
    {
        RequestThresholds = new NoulPolicyThresholds(0.25, 0.75),
        RequiredIntent = PaymentDisputeContract.BlockCardIntent,
        MinimumIntentConfidence = 0.8
    };

    public async Task<PaymentDisputeDecisionResponse> TriageAsync(
        string customerMessage,
        CancellationToken cancellationToken = default)
    {
        using var activity = DecisionTelemetry.ActivitySource.StartActivity(
            "payment_dispute.triage",
            ActivityKind.Internal);
        var state = JsonSerializer.SerializeToElement(new Dictionary<string, string>
        {
            ["customer_message"] = customerMessage
        });
        var response = await provider.EvaluateAsync(
            new DecisionEvaluationRequest
            {
                State = state,
                Contract = PaymentDisputeContract.Definition,
                Model = configuration["DecisionFabric:Model"]
            },
            cancellationToken);

        var blockCardRequested = ReadAnswer<NoulAnswer>(
            response,
            PaymentDisputeContract.BlockCardRequested);
        var primaryIntent = ReadAnswer<ChoiceAnswer>(
            response,
            PaymentDisputeContract.PrimaryIntent);
        var urgency = ReadAnswer<ScoreAnswer>(
            response,
            PaymentDisputeContract.Urgency);
        var riskSignals = LinguisticRiskDetector.Detect(customerMessage);
        var policyDecision = DestructiveActionGate.Evaluate(
            new DestructiveActionEvidence
            {
                ActionRequested = blockCardRequested,
                PrimaryIntent = primaryIntent,
                LinguisticRiskSignals = riskSignals
            },
            GateOptions);
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
            Model = response.Model,
            ContractId = PaymentDisputeContract.Definition.Id,
            ContractVersion = PaymentDisputeContract.Definition.Version,
            DurationMilliseconds = response.Duration.TotalMilliseconds,
            InputTokens = response.Usage.InputTokens,
            OutputTokens = response.Usage.OutputTokens,
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

    private static TAnswer ReadAnswer<TAnswer>(
        DecisionEvaluationResponse response,
        string questionId)
        where TAnswer : DecisionAnswer
    {
        if (!response.Answers.TryGetValue(questionId, out var answer) || answer is not TAnswer typed)
        {
            throw new InvalidOperationException(
                $"Decision response did not contain expected {typeof(TAnswer).Name} answer '{questionId}'.");
        }

        return typed;
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
