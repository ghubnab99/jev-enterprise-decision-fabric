using System.Text.Json;

namespace DecisionFabric.Core;

public sealed record DecisionContract
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyDictionary<string, DecisionQuestion> Questions { get; init; }
}

public sealed record DecisionEvaluationRequest
{
    public required JsonElement State { get; init; }
    public required DecisionContract Contract { get; init; }
    public string? Model { get; init; }
}

public sealed record DecisionUsage(int InputTokens, int OutputTokens);

public sealed record DecisionEvaluationResponse
{
    public required string Model { get; init; }
    public required IReadOnlyDictionary<string, DecisionAnswer> Answers { get; init; }
    public required DecisionUsage Usage { get; init; }
    public required TimeSpan Duration { get; init; }
}

public interface IDecisionProvider
{
    Task<DecisionEvaluationResponse> EvaluateAsync(
        DecisionEvaluationRequest request,
        CancellationToken cancellationToken = default);
}
