using DecisionFabric.Core;

namespace DecisionFabric.Policy;

public enum DestructiveActionDisposition
{
    NotAuthorized,
    RequireConfirmation,
    Authorized
}

public sealed record DestructiveActionEvidence
{
    public required NoulAnswer ActionRequested { get; init; }
    public required ChoiceAnswer PrimaryIntent { get; init; }
    public LinguisticRiskSignal LinguisticRiskSignals { get; init; }
}

public sealed record DestructiveActionGateOptions
{
    public required NoulPolicyThresholds RequestThresholds { get; init; }
    public required string RequiredIntent { get; init; }
    public double MinimumIntentConfidence { get; init; } = 0.8;

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(RequestThresholds);
        RequestThresholds.Validate();

        if (string.IsNullOrWhiteSpace(RequiredIntent))
        {
            throw new ArgumentException("A required intent must be configured.", nameof(RequiredIntent));
        }

        if (MinimumIntentConfidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumIntentConfidence),
                "Minimum intent confidence must be between 0 and 1.");
        }
    }
}

public sealed record DestructiveActionDecision(
    DestructiveActionDisposition Disposition,
    double RequestedProbability,
    string ObservedIntent,
    double IntentConfidence,
    LinguisticRiskSignal LinguisticRiskSignals,
    IReadOnlyList<string> Reasons);

public static class DestructiveActionGate
{
    public static DestructiveActionDecision Evaluate(
        DestructiveActionEvidence evidence,
        DestructiveActionGateOptions options)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(evidence.ActionRequested);
        ArgumentNullException.ThrowIfNull(evidence.PrimaryIntent);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var probability = evidence.ActionRequested.Noul;
        if (probability is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(evidence),
                "The requested-action probability must be between 0 and 1.");
        }

        if (evidence.PrimaryIntent.Confidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(evidence),
                "Intent confidence must be between 0 and 1.");
        }

        if (probability <= options.RequestThresholds.NegativeAtOrBelow)
        {
            return CreateDecision(
                DestructiveActionDisposition.NotAuthorized,
                evidence,
                $"The request probability {probability:F3} is at or below the negative boundary " +
                $"{options.RequestThresholds.NegativeAtOrBelow:F3}.");
        }

        if (probability < options.RequestThresholds.PositiveAtOrAbove)
        {
            return CreateDecision(
                DestructiveActionDisposition.RequireConfirmation,
                evidence,
                $"The request probability {probability:F3} is inside the uncertainty band.");
        }

        var confirmationReasons = new List<string>();
        if (evidence.LinguisticRiskSignals != LinguisticRiskSignal.None)
        {
            confirmationReasons.Add(
                $"Linguistic risk detected: {evidence.LinguisticRiskSignals}.");
        }

        if (!string.Equals(
                evidence.PrimaryIntent.Choice,
                options.RequiredIntent,
                StringComparison.Ordinal))
        {
            confirmationReasons.Add(
                $"Primary intent '{evidence.PrimaryIntent.Choice}' does not match required intent " +
                $"'{options.RequiredIntent}'.");
        }

        if (evidence.PrimaryIntent.Confidence < options.MinimumIntentConfidence)
        {
            confirmationReasons.Add(
                $"Intent confidence {evidence.PrimaryIntent.Confidence:F3} is below the confirmation-free " +
                $"minimum {options.MinimumIntentConfidence:F3}.");
        }

        return confirmationReasons.Count == 0
            ? CreateDecision(
                DestructiveActionDisposition.Authorized,
                evidence,
                "Independent request and intent evidence passed the configured authorization gates.")
            : new DestructiveActionDecision(
                DestructiveActionDisposition.RequireConfirmation,
                probability,
                evidence.PrimaryIntent.Choice,
                evidence.PrimaryIntent.Confidence,
                evidence.LinguisticRiskSignals,
                confirmationReasons);
    }

    private static DestructiveActionDecision CreateDecision(
        DestructiveActionDisposition disposition,
        DestructiveActionEvidence evidence,
        string reason) =>
        new(
            disposition,
            evidence.ActionRequested.Noul,
            evidence.PrimaryIntent.Choice,
            evidence.PrimaryIntent.Confidence,
            evidence.LinguisticRiskSignals,
            [reason]);
}
