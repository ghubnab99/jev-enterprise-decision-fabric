using System.Text.Json;
using DecisionFabric.Core;
using DecisionFabric.TypeSafe;

namespace DecisionFabric.Tests;

public sealed class TypeSafeAnswerParserTests
{
    [Fact]
    public void Parse_ReadsAllDocumentedAnswerShapes()
    {
        using var noulJson = JsonDocument.Parse("""{"type":"noul","noul":0.92}""");
        using var choiceJson = JsonDocument.Parse("""{"type":"choice","choice":"fraud","probabilities":{"fraud":0.8,"other":0.2},"confidence":0.6}""");
        using var scoreJson = JsonDocument.Parse("""{"type":"score","score":1.6,"legend":{"0":"low","1":"medium","2":"high"},"probabilities":{"0":0.05,"1":0.3,"2":0.65},"confidence":0.78}""");

        var noul = Assert.IsType<NoulAnswer>(TypeSafeAnswerParser.Parse(noulJson.RootElement));
        var choice = Assert.IsType<ChoiceAnswer>(TypeSafeAnswerParser.Parse(choiceJson.RootElement));
        var score = Assert.IsType<ScoreAnswer>(TypeSafeAnswerParser.Parse(scoreJson.RootElement));

        Assert.Equal(0.92, noul.Noul);
        Assert.Equal("fraud", choice.Choice);
        Assert.Equal(0.8, choice.Probabilities["fraud"]);
        Assert.Equal(1.6, score.Score);
        Assert.Equal("high", score.Legend["2"]);
    }
}
