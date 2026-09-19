using DecisionFabric.AgentActionGate.Api;
using DecisionFabric.Core;
using DecisionFabric.Hosting;
using DecisionFabric.PaymentDisputes.Api;
using DecisionFabric.Policy;
using Microsoft.Extensions.Options;

namespace DecisionFabric.Tests;

/// <summary>
/// Acceptance check for the decision-pack abstraction: two unrelated domains run
/// through one fabric instance, each with exactly one provider request, and the
/// fabric contains no knowledge of either domain.
/// </summary>
public sealed class CrossDomainFabricTests
{
    [Fact]
    public async Task PaymentAndAgentPacksShareOneFabricWithOneRequestEach()
    {
        var provider = new CountingProvider(new FixtureDecisionProvider(
            [PaymentDisputeFixtures.Create(), AgentActionFixtures.Create()]));
        var fabric = new DefaultDecisionFabric(provider);
        var paymentPack = new PaymentDisputePack(Options.Create(new PaymentDisputePolicyOptions
        {
            NegativeAtOrBelow = 0.25,
            PositiveAtOrAbove = 0.75,
            MinimumIntentConfidence = 0.8
        }));
        var agentPack = new AgentActionPack(Options.Create(new AgentActionPolicyOptions
        {
            NegativeAtOrBelow = 0.25,
            PositiveAtOrAbove = 0.75,
            MinimumImpactConfidence = 0.8,
            MaximumAutoApprovedScopeExpansion = 0.5
        }));

        var payment = await fabric.EvaluateAsync(
            paymentPack,
            new PaymentDisputeInput("I don't want you not to block my card."));
        var agent = await fabric.EvaluateAsync(
            agentPack,
            new AgentActionInput("Clean up the Q3 folder.", "drive.delete_folder", """{"folder":"Q3"}"""));

        Assert.Equal(DestructiveActionDisposition.RequireConfirmation, payment.Outcome.Gate.Disposition);
        Assert.Equal(ProposedActionDisposition.RequireApproval, agent.Outcome.Disposition);
        Assert.Equal(["payment-dispute-triage", "agent-action-gate"], provider.ContractIds);
        Assert.Equal(
            [paymentPack.Contract.Questions.Count, agentPack.Contract.Questions.Count],
            provider.QuestionCounts);
        Assert.NotEqual(payment.PolicyVersion, agent.PolicyVersion);
    }

    [Fact]
    public void FabricAssemblyHasNoDomainDependencies()
    {
        var references = typeof(DefaultDecisionFabric).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.DoesNotContain(references, name => name!.StartsWith("DecisionFabric.", StringComparison.Ordinal));
    }

    private sealed class CountingProvider(IDecisionProvider inner) : IDecisionProvider
    {
        public List<string> ContractIds { get; } = [];

        public List<int> QuestionCounts { get; } = [];

        public Task<DecisionEvaluationResponse> EvaluateAsync(
            DecisionEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            ContractIds.Add(request.Contract.Id);
            QuestionCounts.Add(request.Contract.Questions.Count);
            return inner.EvaluateAsync(request, cancellationToken);
        }
    }
}
