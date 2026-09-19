using System.Text.Json;
using DecisionFabric.Core;
using DecisionFabric.Policy;
using Microsoft.Extensions.Options;

namespace DecisionFabric.PaymentDisputes.Api;

internal sealed record PaymentDisputeInput(string CustomerMessage);

internal sealed record PaymentDisputeOutcome(
    DestructiveActionDecision Gate,
    LinguisticRiskSignal RiskSignals);

internal sealed class PaymentDisputePack : IDecisionPack<PaymentDisputeInput, PaymentDisputeOutcome>
{
    private readonly DestructiveActionGateOptions _gateOptions;

    public PaymentDisputePack(IOptions<PaymentDisputePolicyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _gateOptions = options.Value.ToGateOptions();
        PolicyVersion = PolicyFingerprint.Create("payment-dispute-gate", _gateOptions);
    }

    public DecisionContract Contract => PaymentDisputeContract.Definition;

    public string PolicyVersion { get; }

    public JsonElement CreateState(PaymentDisputeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return JsonSerializer.SerializeToElement(new Dictionary<string, string>
        {
            ["customer_message"] = input.CustomerMessage
        });
    }

    public PaymentDisputeOutcome Decide(PaymentDisputeInput input, DecisionEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(evidence);

        var riskSignals = LinguisticRiskDetector.Detect(input.CustomerMessage);
        var gate = DestructiveActionGate.Evaluate(
            new DestructiveActionEvidence
            {
                ActionRequested = evidence.Noul(PaymentDisputeContract.BlockCardRequested),
                PrimaryIntent = evidence.Choice(PaymentDisputeContract.PrimaryIntent),
                LinguisticRiskSignals = riskSignals
            },
            _gateOptions);
        return new PaymentDisputeOutcome(gate, riskSignals);
    }
}
