using System.Diagnostics;
using System.Diagnostics.Metrics;
using DecisionFabric.Policy;

namespace DecisionFabric.PaymentDisputes.Api;

internal static class DecisionTelemetry
{
    public const string InstrumentationName = "DecisionFabric.PaymentDisputes";

    public static readonly ActivitySource ActivitySource = new(InstrumentationName);

    private static readonly Meter Meter = new(InstrumentationName);
    private static readonly Counter<long> EvaluationCounter =
        Meter.CreateCounter<long>("decision_fabric.evaluations");
    private static readonly Counter<long> ConfirmationCounter =
        Meter.CreateCounter<long>("decision_fabric.confirmations");
    private static readonly Histogram<double> DurationHistogram =
        Meter.CreateHistogram<double>("decision_fabric.evaluation.duration", "ms");

    public static void RecordEvaluation(
        DestructiveActionDisposition disposition,
        string model,
        double durationMilliseconds)
    {
        var tags = new TagList
        {
            { "decision.disposition", disposition.ToString() },
            { "gen_ai.response.model", model }
        };
        EvaluationCounter.Add(1, tags);
        DurationHistogram.Record(durationMilliseconds, tags);
    }

    public static void RecordConfirmation() => ConfirmationCounter.Add(1);
}
