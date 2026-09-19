using DecisionFabric.Core;
using DecisionFabric.Policy;

namespace DecisionFabric.Tests;

public sealed class NoulPolicyTests
{
    private static readonly NoulPolicyThresholds Thresholds = new(0.2, 0.8);

    [Theory]
    [InlineData(0.05, PolicyDisposition.Allow)]
    [InlineData(0.50, PolicyDisposition.HumanReview)]
    [InlineData(0.95, PolicyDisposition.RequireConfirmation)]
    public void Route_KeepsUncertainModelOutputAwayFromSideEffects(
        double probability,
        PolicyDisposition expected)
    {
        var result = NoulPolicy.Route(
            new NoulAnswer { Noul = probability },
            Thresholds,
            PolicyDisposition.Allow,
            PolicyDisposition.RequireConfirmation);

        Assert.Equal(expected, result.Disposition);
    }
}
