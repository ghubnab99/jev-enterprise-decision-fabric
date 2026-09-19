using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DecisionFabric.Core;

public sealed record DecisionFabricOptions
{
    /// <summary>The model requested from the provider; the provider default when null.</summary>
    public string? Model { get; init; }
}

public sealed class DefaultDecisionFabric(IDecisionProvider provider, DecisionFabricOptions? options = null)
    : IDecisionFabric
{
    private readonly IDecisionProvider _provider =
        provider ?? throw new ArgumentNullException(nameof(provider));

    private readonly DecisionFabricOptions _options = options ?? new DecisionFabricOptions();

    public async Task<DecisionResult<TOutcome>> EvaluateAsync<TInput, TOutcome>(
        IDecisionPack<TInput, TOutcome> pack,
        TInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pack);

        var contract = pack.Contract;
        using var activity = DecisionFabricTelemetry.ActivitySource.StartActivity(
            "decision_fabric.evaluate",
            ActivityKind.Internal);
        activity?.SetTag("decision.contract.id", contract.Id);
        activity?.SetTag("decision.contract.version", contract.Version);
        activity?.SetTag("decision.policy.version", pack.PolicyVersion);

        DecisionEvaluationResponse response;
        try
        {
            response = await _provider.EvaluateAsync(
                new DecisionEvaluationRequest
                {
                    State = pack.CreateState(input),
                    Contract = contract,
                    Model = _options.Model
                },
                cancellationToken);
        }
        catch (Exception exception)
        {
            DecisionFabricTelemetry.RecordFailure(contract, exception);
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }

        DecisionEvidence evidence;
        try
        {
            evidence = DecisionEvidence.Validate(contract, response.Answers);
        }
        catch (DecisionContractViolationException exception)
        {
            DecisionFabricTelemetry.RecordFailure(contract, exception);
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }

        var outcome = pack.Decide(input, evidence);
        activity?.SetTag("gen_ai.response.model", response.Model);
        DecisionFabricTelemetry.RecordEvaluation(contract, response);

        return new DecisionResult<TOutcome>
        {
            Outcome = outcome,
            Evidence = evidence,
            Model = response.Model,
            ContractId = contract.Id,
            ContractVersion = contract.Version,
            PolicyVersion = pack.PolicyVersion,
            Usage = response.Usage,
            Duration = response.Duration
        };
    }
}

public static class DecisionFabricTelemetry
{
    public const string InstrumentationName = "DecisionFabric";

    public static readonly ActivitySource ActivitySource = new(InstrumentationName);

    private static readonly Meter Meter = new(InstrumentationName);
    private static readonly Counter<long> ProviderCallCounter =
        Meter.CreateCounter<long>("decision_fabric.provider.calls");
    private static readonly Histogram<double> ProviderDurationHistogram =
        Meter.CreateHistogram<double>("decision_fabric.provider.duration", "ms");

    internal static void RecordEvaluation(DecisionContract contract, DecisionEvaluationResponse response)
    {
        var tags = new TagList
        {
            { "decision.contract.id", contract.Id },
            { "decision.contract.version", contract.Version },
            { "gen_ai.response.model", response.Model },
            { "outcome", "success" }
        };
        ProviderCallCounter.Add(1, tags);
        ProviderDurationHistogram.Record(response.Duration.TotalMilliseconds, tags);
    }

    internal static void RecordFailure(DecisionContract contract, Exception exception) =>
        ProviderCallCounter.Add(1, new TagList
        {
            { "decision.contract.id", contract.Id },
            { "decision.contract.version", contract.Version },
            { "outcome", "error" },
            { "error.type", exception.GetType().Name }
        });
}
