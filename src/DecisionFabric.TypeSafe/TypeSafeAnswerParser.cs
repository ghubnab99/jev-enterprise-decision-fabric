using System.Text.Json;
using DecisionFabric.Core;

namespace DecisionFabric.TypeSafe;

public static class TypeSafeAnswerParser
{
    public static DecisionAnswer Parse(JsonElement element)
    {
        var type = element.GetProperty("type").GetString();

        return type switch
        {
            "noul" => new NoulAnswer
            {
                Noul = element.GetProperty("noul").GetDouble()
            },
            "choice" => new ChoiceAnswer
            {
                Choice = element.GetProperty("choice").GetString()
                    ?? throw new JsonException("Choice answer was null."),
                Probabilities = ReadDoubleMap(element.GetProperty("probabilities")),
                Confidence = element.GetProperty("confidence").GetDouble()
            },
            "score" => new ScoreAnswer
            {
                Score = element.GetProperty("score").GetDouble(),
                Legend = ReadStringMap(element.GetProperty("legend")),
                Probabilities = ReadDoubleMap(element.GetProperty("probabilities")),
                Confidence = element.GetProperty("confidence").GetDouble()
            },
            _ => throw new JsonException($"Unsupported TypeSafe answer type '{type ?? "<missing>"}'.")
        };
    }

    private static Dictionary<string, double> ReadDoubleMap(JsonElement element) =>
        element.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetDouble());

    private static Dictionary<string, string> ReadStringMap(JsonElement element) =>
        element.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.GetString() ?? string.Empty);
}
