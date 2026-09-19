using System.Text.Json.Serialization;

namespace DecisionFabric.Core;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(NoulAnswer), "noul")]
[JsonDerivedType(typeof(ChoiceAnswer), "choice")]
[JsonDerivedType(typeof(ScoreAnswer), "score")]
public abstract record DecisionAnswer;

public sealed record NoulAnswer : DecisionAnswer
{
    [JsonPropertyName("noul")]
    public required double Noul { get; init; }
}

public sealed record ChoiceAnswer : DecisionAnswer
{
    [JsonPropertyName("choice")]
    public required string Choice { get; init; }

    [JsonPropertyName("probabilities")]
    public required IReadOnlyDictionary<string, double> Probabilities { get; init; }

    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }
}

public sealed record ScoreAnswer : DecisionAnswer
{
    [JsonPropertyName("score")]
    public required double Score { get; init; }

    [JsonPropertyName("legend")]
    public required IReadOnlyDictionary<string, string> Legend { get; init; }

    [JsonPropertyName("probabilities")]
    public required IReadOnlyDictionary<string, double> Probabilities { get; init; }

    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }
}
