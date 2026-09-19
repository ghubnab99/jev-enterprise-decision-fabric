using System.Text.Json;
using DecisionFabric.Core;
using DecisionFabric.Hosting;

namespace DecisionFabric.Tests;

public sealed class FixtureDecisionProviderTests
{
    private static readonly DecisionContract Contract = new()
    {
        Id = "fixture-contract",
        Version = "1.0.0",
        Questions = new Dictionary<string, DecisionQuestion>
        {
            ["q"] = new NoulQuestion { Instructions = "Q?" }
        }
    };

    private static readonly DecisionFixtureSet FixtureSet = new()
    {
        ContractId = Contract.Id,
        Model = "fixture/synthetic",
        KeySelector = state => state.GetProperty("text").GetString()!.Trim(),
        Recorded = new Dictionary<string, IReadOnlyDictionary<string, DecisionAnswer>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["known"] = new Dictionary<string, DecisionAnswer> { ["q"] = new NoulAnswer { Noul = 0.9 } }
        },
        Fallback = new Dictionary<string, DecisionAnswer> { ["q"] = new NoulAnswer { Noul = 0.5 } }
    };

    [Theory]
    [InlineData(" KNOWN ", 0.9)]
    [InlineData("unknown", 0.5)]
    public async Task ReturnsRecordedAnswersOrFallbackForTheRequestedContract(string text, double expected)
    {
        var provider = new FixtureDecisionProvider([FixtureSet]);

        var response = await provider.EvaluateAsync(Request(Contract, text));

        Assert.Equal("fixture/synthetic", response.Model);
        Assert.Equal(expected, Assert.IsType<NoulAnswer>(response.Answers["q"]).Noul);
    }

    [Fact]
    public async Task UnregisteredContractIsRejected()
    {
        var provider = new FixtureDecisionProvider([FixtureSet]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.EvaluateAsync(Request(Contract with { Id = "other" }, "known")));
    }

    private static DecisionEvaluationRequest Request(DecisionContract contract, string text) => new()
    {
        Contract = contract,
        State = JsonSerializer.SerializeToElement(new { text })
    };
}
