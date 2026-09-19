using DecisionFabric.Policy;
using Microsoft.Extensions.Options;

namespace DecisionFabric.PaymentDisputes.Api;

/// <summary>
/// Card-block authorization parameters bound from configuration. Every value is
/// required so a missing setting cannot silently fall back to a permissive default.
/// </summary>
public sealed class PaymentDisputePolicyOptions
{
    public const string SectionName = "DecisionFabric:Policies:PaymentDisputeGate";

    public double? NegativeAtOrBelow { get; set; }
    public double? PositiveAtOrAbove { get; set; }
    public double? MinimumIntentConfidence { get; set; }

    public DestructiveActionGateOptions ToGateOptions()
    {
        var options = new DestructiveActionGateOptions
        {
            RequestThresholds = new NoulPolicyThresholds(
                Required(NegativeAtOrBelow, nameof(NegativeAtOrBelow)),
                Required(PositiveAtOrAbove, nameof(PositiveAtOrAbove))),
            RequiredIntent = PaymentDisputeContract.BlockCardIntent,
            MinimumIntentConfidence = Required(MinimumIntentConfidence, nameof(MinimumIntentConfidence))
        };
        options.Validate();
        return options;
    }

    private static double Required(double? value, string name) =>
        value ?? throw new ArgumentException($"{SectionName}:{name} is required.", name);
}

internal sealed class PaymentDisputePolicyOptionsValidator : IValidateOptions<PaymentDisputePolicyOptions>
{
    public ValidateOptionsResult Validate(string? name, PaymentDisputePolicyOptions options)
    {
        try
        {
            options.ToGateOptions();
            return ValidateOptionsResult.Success;
        }
        catch (ArgumentException exception)
        {
            return ValidateOptionsResult.Fail(exception.Message);
        }
    }
}
