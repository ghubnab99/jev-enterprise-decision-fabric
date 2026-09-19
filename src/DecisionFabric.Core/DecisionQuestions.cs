using System.Text.Json.Serialization;

namespace DecisionFabric.Core;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(NoulQuestion), "noul")]
[JsonDerivedType(typeof(ChoiceQuestion), "choice")]
[JsonDerivedType(typeof(ScoreQuestion), "score")]
public abstract record DecisionQuestion
{
    [JsonPropertyName("instructions")]
    public required string Instructions { get; init; }
}

public sealed record NoulQuestion : DecisionQuestion
{
    [JsonPropertyName("criteria")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NoulCriteria? Criteria { get; init; }
}

public sealed record ChoiceQuestion : DecisionQuestion
{
    [JsonPropertyName("criteria")]
    public required IReadOnlyDictionary<string, string?> Criteria { get; init; }
}

public sealed record ScoreQuestion : DecisionQuestion
{
    [JsonPropertyName("criteria")]
    public required IReadOnlyList<string> Criteria { get; init; }
}

public sealed record NoulCriteria
{
    [JsonPropertyName("true")]
    public required string True { get; init; }

    [JsonPropertyName("false")]
    public required string False { get; init; }
}
