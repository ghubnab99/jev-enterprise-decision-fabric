using DecisionFabric.Policy;
using Microsoft.Extensions.Options;

namespace DecisionFabric.AgentActionGate.Api;

/// <summary>
/// Tool-call authorization parameters bound from configuration. Every value is
/// required so a missing setting cannot silently fall back to a permissive default.
/// </summary>
public sealed class AgentActionPolicyOptions
{
    public const string SectionName = "DecisionFabric:Policies:AgentActionGate";

    public double? NegativeAtOrBelow { get; set; }
    public double? PositiveAtOrAbove { get; set; }
    public double? MinimumImpactConfidence { get; set; }

    /// <summary>Highest <c>scope_expansion</c> score (0–2 scale) allowed without approval.</summary>
    public double? MaximumAutoApprovedScopeExpansion { get; set; }

    internal ProposedActionGateOptions ToGateOptions()
    {
        var maximumScope = Required(MaximumAutoApprovedScopeExpansion, nameof(MaximumAutoApprovedScopeExpansion));
        if (maximumScope is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumAutoApprovedScopeExpansion),
                "Maximum auto-approved scope expansion must be between 0 and 2.");
        }

        var options = new ProposedActionGateOptions
        {
            RequestThresholds = new NoulPolicyThresholds(
                Required(NegativeAtOrBelow, nameof(NegativeAtOrBelow)),
                Required(PositiveAtOrAbove, nameof(PositiveAtOrAbove))),
            ReadOnlyImpact = AgentActionContract.ReadOnly,
            ApprovalRequiredImpacts = AgentActionContract.ApprovalRequiredImpacts,
            MinimumImpactConfidence = Required(MinimumImpactConfidence, nameof(MinimumImpactConfidence)),
            MaximumAutoApprovedScopeExpansion = maximumScope
        };
        options.Validate();
        return options;
    }

    private static double Required(double? value, string name) =>
        value ?? throw new ArgumentException($"{SectionName}:{name} is required.", name);
}

internal sealed class AgentActionPolicyOptionsValidator : IValidateOptions<AgentActionPolicyOptions>
{
    public ValidateOptionsResult Validate(string? name, AgentActionPolicyOptions options)
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
