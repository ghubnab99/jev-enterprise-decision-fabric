using DecisionFabric.Core;
using DecisionFabric.Policy;

namespace DecisionFabric.Tests;

public sealed class AgentActionGateTests
{
    private const string ReadOnly = "read_only";
    private const string ReversibleChange = "reversible_change";
    private const string IrreversibleChange = "irreversible_change";
    private const string ExternalCommunication = "external_communication";

    private static readonly ProposedActionGateOptions Options = new()
    {
        RequestThresholds = new NoulPolicyThresholds(0.25, 0.75),
        ReadOnlyImpact = ReadOnly,
        ApprovalRequiredImpacts = new HashSet<string>(StringComparer.Ordinal)
        {
            IrreversibleChange,
            ExternalCommunication
        },
        MinimumImpactConfidence = 0.8,
        MaximumAutoApprovedScopeExpansion = 0.5
    };

    [Fact]
    public void ConfidentReadOnlyActionsAreAllowed()
    {
        var decision = Evaluate(0.97, ReadOnly, 0.95, 0.1);

        Assert.Equal(ProposedActionDisposition.Allow, decision.Disposition);
    }

    [Fact]
    public void ReadOnlyActionsTheModelIsUnsureAboutStillNeedApproval()
    {
        var decision = Evaluate(0.97, ReadOnly, 0.55, 0.1);

        Assert.Equal(ProposedActionDisposition.RequireApproval, decision.Disposition);
    }

    [Fact]
    public void ActionsTheUserClearlyDidNotAskForAreDenied()
    {
        var decision = Evaluate(0.03, ExternalCommunication, 0.97, 2);

        Assert.Equal(ProposedActionDisposition.Deny, decision.Disposition);
    }

    [Fact]
    public void RequestedReversibleInScopeActionsAreAllowed()
    {
        var decision = Evaluate(0.96, ReversibleChange, 0.9, 0.2);

        Assert.Equal(ProposedActionDisposition.Allow, decision.Disposition);
    }

    [Theory]
    [InlineData(IrreversibleChange, 0.95, 0.1, LinguisticRiskSignal.None)]
    [InlineData(ExternalCommunication, 0.95, 0.1, LinguisticRiskSignal.None)]
    [InlineData(ReversibleChange, 0.6, 0.1, LinguisticRiskSignal.None)]
    [InlineData(ReversibleChange, 0.95, 1.4, LinguisticRiskSignal.None)]
    [InlineData(ReversibleChange, 0.95, 0.1, LinguisticRiskSignal.MultipleNegations)]
    public void EachApprovalGateIsCheckedIndependently(
        string impact,
        double impactConfidence,
        double scopeExpansion,
        LinguisticRiskSignal riskSignals)
    {
        var decision = Evaluate(0.96, impact, impactConfidence, scopeExpansion, riskSignals);

        Assert.Equal(ProposedActionDisposition.RequireApproval, decision.Disposition);
        Assert.NotEmpty(decision.Reasons);
    }

    [Fact]
    public void UncertaintyAboutTheRequestRequiresApproval()
    {
        var decision = Evaluate(0.5, ReversibleChange, 0.95, 0.1);

        Assert.Equal(ProposedActionDisposition.RequireApproval, decision.Disposition);
    }

    [Fact]
    public void ConfidentReadOnlyEvidenceOutranksAWeakRequestSignal()
    {
        // A search the user did not literally ask for still cannot damage anything.
        var decision = Evaluate(0.05, ReadOnly, 0.96, 1.8);

        Assert.Equal(ProposedActionDisposition.Allow, decision.Disposition);
    }

    [Fact]
    public void EveryFailedGateIsReported()
    {
        var decision = Evaluate(0.5, IrreversibleChange, 0.4, 1.9, LinguisticRiskSignal.MultipleNegations);

        Assert.Equal(ProposedActionDisposition.RequireApproval, decision.Disposition);
        Assert.Equal(5, decision.Reasons.Count);
    }

    [Fact]
    public void AReadOnlyImpactCannotAlsoRequireApproval()
    {
        var options = Options with
        {
            ApprovalRequiredImpacts = new HashSet<string>(StringComparer.Ordinal) { ReadOnly }
        };

        Assert.Throws<ArgumentException>(() =>
            ProposedActionGate.Evaluate(Evidence(0.9, ReversibleChange, 0.9, 0.1), options));
    }

    [Fact]
    public void ProbabilitiesOutsideTheUnitIntervalAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Evaluate(1.4, ReversibleChange, 0.9, 0.1));
    }

    private static ProposedActionDecision Evaluate(
        double requested,
        string impact,
        double impactConfidence,
        double scopeExpansion,
        LinguisticRiskSignal riskSignals = LinguisticRiskSignal.None) =>
        ProposedActionGate.Evaluate(
            Evidence(requested, impact, impactConfidence, scopeExpansion, riskSignals),
            Options);

    private static ProposedActionEvidence Evidence(
        double requested,
        string impact,
        double impactConfidence,
        double scopeExpansion,
        LinguisticRiskSignal riskSignals = LinguisticRiskSignal.None) =>
        new()
        {
            ActionRequestedByUser = new NoulAnswer { Noul = requested },
            ActionImpact = new ChoiceAnswer
            {
                Choice = impact,
                Probabilities = new Dictionary<string, double> { [impact] = impactConfidence },
                Confidence = impactConfidence
            },
            ScopeExpansion = new ScoreAnswer
            {
                Score = scopeExpansion,
                Legend = new Dictionary<string, string>(),
                Probabilities = new Dictionary<string, double>(),
                Confidence = 0.9
            },
            LinguisticRiskSignals = riskSignals
        };
}
