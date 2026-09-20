using Microsoft.Extensions.Options;

namespace DecisionFabric.Inspector;

/// <summary>Where the recorded evaluation artifacts live. Nothing here is fetched over the network.</summary>
public sealed class InspectorOptions
{
    public const string SectionName = "DecisionFabric:Inspector";

    /// <summary>Directory the other paths are resolved against, relative to the content root.</summary>
    public string ArchiveRoot { get; set; } = "../../evals";

    /// <summary>The labelled dataset the legs were run against.</summary>
    public string DatasetPath { get; set; } = string.Empty;

    /// <summary>One entry per recorded run being inspected.</summary>
    public IList<InspectorLegOptions> Legs { get; set; } = [];
}

public sealed class InspectorLegOptions
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    /// <summary>The report the evaluation runner wrote, which is the only source of aggregates.</summary>
    public string Report { get; set; } = string.Empty;

    /// <summary>The JSONL of individual calls, which is the only source of per-call detail.</summary>
    public string Calls { get; set; } = string.Empty;
}

internal sealed class InspectorOptionsValidator : IValidateOptions<InspectorOptions>
{
    public ValidateOptionsResult Validate(string? name, InspectorOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ArchiveRoot))
        {
            failures.Add("ArchiveRoot must be set.");
        }

        if (string.IsNullOrWhiteSpace(options.DatasetPath))
        {
            failures.Add("DatasetPath must be set.");
        }

        if (options.Legs.Count == 0)
        {
            failures.Add("At least one leg must be configured.");
        }

        foreach (var (leg, index) in options.Legs.Select((leg, index) => (leg, index)))
        {
            if (string.IsNullOrWhiteSpace(leg.Id))
            {
                failures.Add($"Legs[{index}].Id must be set.");
            }

            if (string.IsNullOrWhiteSpace(leg.Report))
            {
                failures.Add($"Legs[{index}].Report must be set.");
            }

            if (string.IsNullOrWhiteSpace(leg.Calls))
            {
                failures.Add($"Legs[{index}].Calls must be set.");
            }
        }

        var duplicate = options.Legs
            .GroupBy(leg => leg.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            failures.Add($"Leg id '{duplicate.Key}' is configured more than once.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
