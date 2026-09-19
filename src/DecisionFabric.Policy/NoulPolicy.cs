using DecisionFabric.Core;

namespace DecisionFabric.Policy;

public enum PolicyDisposition
{
    Allow,
    Block,
    RequireConfirmation,
    HumanReview,
    SystemTwo
}

public sealed record PolicyOutcome(
    PolicyDisposition Disposition,
    string Reason,
    double ObservedValue);

public sealed record NoulPolicyThresholds(double NegativeAtOrBelow, double PositiveAtOrAbove)
{
    public void Validate()
    {
        if (NegativeAtOrBelow is < 0 or > 1 || PositiveAtOrAbove is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(NoulPolicyThresholds), "Thresholds must be between 0 and 1.");
        }

        if (NegativeAtOrBelow >= PositiveAtOrAbove)
        {
            throw new ArgumentException("The negative threshold must be lower than the positive threshold.");
        }
    }
}

public static class NoulPolicy
{
    public static PolicyOutcome Route(
        NoulAnswer answer,
        NoulPolicyThresholds thresholds,
        PolicyDisposition negativeDisposition,
        PolicyDisposition positiveDisposition,
        PolicyDisposition uncertainDisposition = PolicyDisposition.HumanReview)
    {
        ArgumentNullException.ThrowIfNull(answer);
        thresholds.Validate();

        if (answer.Noul <= thresholds.NegativeAtOrBelow)
        {
            return new PolicyOutcome(
                negativeDisposition,
                $"Probability {answer.Noul:F3} is at or below the negative threshold {thresholds.NegativeAtOrBelow:F3}.",
                answer.Noul);
        }

        if (answer.Noul >= thresholds.PositiveAtOrAbove)
        {
            return new PolicyOutcome(
                positiveDisposition,
                $"Probability {answer.Noul:F3} is at or above the positive threshold {thresholds.PositiveAtOrAbove:F3}.",
                answer.Noul);
        }

        return new PolicyOutcome(
            uncertainDisposition,
            $"Probability {answer.Noul:F3} is inside the uncertainty band.",
            answer.Noul);
    }
}
