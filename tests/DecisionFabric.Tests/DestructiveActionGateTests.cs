using DecisionFabric.Core;
using DecisionFabric.Policy;

namespace DecisionFabric.Tests;

public sealed class DestructiveActionGateTests
{
    private static readonly DestructiveActionGateOptions Options = new()
    {
        RequestThresholds = new NoulPolicyThresholds(0.25, 0.75),
        RequiredIntent = "block_card",
        MinimumIntentConfidence = 0.8
    };

    [Fact]
    public void ClearPositiveEvidenceAuthorizesTheAction()
    {
        var decision = Evaluate(0.99, "block_card", 0.97);

        Assert.Equal(DestructiveActionDisposition.Authorized, decision.Disposition);
    }

    [Fact]
    public void ClearNegativeEvidenceDoesNotAuthorizeTheAction()
    {
        var decision = Evaluate(0.03, "dispute_transaction", 0.98);

        Assert.Equal(DestructiveActionDisposition.NotAuthorized, decision.Disposition);
    }

    [Fact]
    public void UncertaintyBandRequiresConfirmation()
    {
        var decision = Evaluate(0.35, "block_card", 0.95);

        Assert.Equal(DestructiveActionDisposition.RequireConfirmation, decision.Disposition);
    }

    [Theory]
    [InlineData("dispute_transaction", 0.95, LinguisticRiskSignal.None)]
    [InlineData("block_card", 0.79, LinguisticRiskSignal.None)]
    [InlineData("block_card", 0.95, LinguisticRiskSignal.MultipleNegations)]
    public void PositiveProbabilityStillRequiresIndependentSafetyEvidence(
        string intent,
        double confidence,
        LinguisticRiskSignal riskSignals)
    {
        var decision = Evaluate(0.85, intent, confidence, riskSignals);

        Assert.Equal(DestructiveActionDisposition.RequireConfirmation, decision.Disposition);
        Assert.NotEmpty(decision.Reasons);
    }

    private static DestructiveActionDecision Evaluate(
        double probability,
        string intent,
        double confidence,
        LinguisticRiskSignal riskSignals = LinguisticRiskSignal.None) =>
        DestructiveActionGate.Evaluate(
            new DestructiveActionEvidence
            {
                ActionRequested = new NoulAnswer { Noul = probability },
                PrimaryIntent = new ChoiceAnswer
                {
                    Choice = intent,
                    Probabilities = new Dictionary<string, double> { [intent] = 1 },
                    Confidence = confidence
                },
                LinguisticRiskSignals = riskSignals
            },
            Options);
}
