using System.Diagnostics;
using System.Text.Json;
using DecisionFabric.Core;

namespace DecisionFabric.Hosting;

/// <summary>
/// Canned answers for one contract so a sample can run without credentials.
/// </summary>
public sealed record DecisionFixtureSet
{
    public required string ContractId { get; init; }

    /// <summary>
    /// Reported as the response model. Must say whether values were recorded from a
    /// live model or are synthetic.
    /// </summary>
    public required string Model { get; init; }

    /// <summary>Derives the lookup key for <see cref="Recorded"/> from the request state.</summary>
    public required Func<JsonElement, string> KeySelector { get; init; }

    /// <summary>Answers by key; construct with the comparer the key semantics require.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, DecisionAnswer>> Recorded { get; init; }

    /// <summary>Answers returned for any state without a recorded entry.</summary>
    public required IReadOnlyDictionary<string, DecisionAnswer> Fallback { get; init; }

    /// <summary>
    /// Reported as the response model when <see cref="Fallback"/> answers. Set it
    /// when <see cref="Recorded"/> came from a live model but the fallback was
    /// written by hand, so a hand-written answer never carries a live model's name.
    /// Defaults to <see cref="Model"/>.
    /// </summary>
    public string? FallbackModel { get; init; }
}

public sealed class FixtureDecisionProvider : IDecisionProvider
{
    private readonly Dictionary<string, DecisionFixtureSet> _sets;

    public FixtureDecisionProvider(IEnumerable<DecisionFixtureSet> fixtureSets)
    {
        ArgumentNullException.ThrowIfNull(fixtureSets);
        _sets = fixtureSets.ToDictionary(set => set.ContractId, StringComparer.Ordinal);
    }

    public Task<DecisionEvaluationResponse> EvaluateAsync(
        DecisionEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        if (!_sets.TryGetValue(request.Contract.Id, out var set))
        {
            throw new InvalidOperationException(
                $"No fixture set is registered for contract '{request.Contract.Id}'.");
        }

        var isRecorded = set.Recorded.TryGetValue(set.KeySelector(request.State), out var recorded);
        stopwatch.Stop();

        return Task.FromResult(new DecisionEvaluationResponse
        {
            Model = isRecorded ? set.Model : set.FallbackModel ?? set.Model,
            Answers = isRecorded ? recorded! : set.Fallback,
            Usage = new DecisionUsage(0, 0),
            Duration = stopwatch.Elapsed
        });
    }
}
