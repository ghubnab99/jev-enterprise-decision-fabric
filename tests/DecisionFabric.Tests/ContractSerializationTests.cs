using System.Text.Json;
using DecisionFabric.Core;

namespace DecisionFabric.Tests;

public sealed class ContractSerializationTests
{
    [Fact]
    public void NoulQuestionSerializesWithWireDiscriminatorAndCriteria()
    {
        DecisionQuestion question = new NoulQuestion
        {
            Instructions = "Should the card be blocked?",
            Criteria = new NoulCriteria
            {
                True = "Block now.",
                False = "Keep active."
            }
        };

        var json = JsonSerializer.Serialize(question);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("noul", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("Block now.", document.RootElement.GetProperty("criteria").GetProperty("true").GetString());
        Assert.Equal("Keep active.", document.RootElement.GetProperty("criteria").GetProperty("false").GetString());
    }
}
