using System.Globalization;
using System.Text.Json;

namespace DecisionFabric.Evals;

internal sealed record ProviderComparisonRow
{
    public required string Provider { get; init; }
    public required string Model { get; init; }
    public required int SuccessfulRuns { get; init; }
    public required int FailedCalls { get; init; }

    /// <summary>Call-weighted: repeats of one case each count.</summary>
    public int? CorrectRuns { get; init; }
    public double? Accuracy { get; init; }
    public int? UnsafeAllowRuns { get; init; }
    public double? UnsafeAllowRate { get; init; }
    public int? OverBlockedRuns { get; init; }
    public double? OverBlockedRate { get; init; }

    /// <summary>Case-weighted: each distinct labelled case counts once.</summary>
    public required CaseLevelAccuracyReport CaseAccuracy { get; init; }

    /// <summary>Cases whose repeated runs did not all reach the same disposition.</summary>
    public required int UnstableCases { get; init; }
    public required int RepeatedCases { get; init; }

    public required double LatencyP50 { get; init; }
    public required double LatencyP95 { get; init; }
    public double? CostPerDecisionUsd { get; init; }
    public required IReadOnlyList<FamilyEvaluationReport> Families { get; init; }
}

internal sealed record BenchmarkComparison
{
    public required string SuiteId { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
    public required IReadOnlyList<ProviderComparisonRow> Providers { get; init; }
}

internal static class BenchmarkComparisonBuilder
{
    public static async Task<int> RunAsync(IReadOnlyList<string> reportPaths, string? outputPath)
    {
        if (reportPaths.Count == 0)
        {
            throw new ArgumentException("--compare needs at least one report path.");
        }

        var rows = new List<ProviderComparisonRow>();
        string? suiteId = null;
        foreach (var path in reportPaths)
        {
            var report = JsonSerializer.Deserialize<EvaluationReport>(
                await File.ReadAllTextAsync(path),
                EvaluationIo.ReportJsonOptions)
                ?? throw new JsonException($"Could not read report '{path}'.");

            if (suiteId is not null && !string.Equals(suiteId, report.SuiteId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Report '{path}' is for suite '{report.SuiteId}', not '{suiteId}'. " +
                    "Comparing providers across different suites would not be meaningful.");
            }

            suiteId ??= report.SuiteId;
            rows.Add(BuildRow(report, path));
        }

        var comparison = new BenchmarkComparison
        {
            SuiteId = suiteId!,
            GeneratedAt = DateTimeOffset.UtcNow,
            Providers = rows
        };

        Print(comparison);
        if (outputPath is not null)
        {
            var full = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(
                full,
                JsonSerializer.Serialize(comparison, EvaluationIo.ReportJsonOptions));
            Console.WriteLine($"\nComparison: {full}");
        }

        return 0;
    }

    private static ProviderComparisonRow BuildRow(EvaluationReport report, string path)
    {
        var caseAccuracy = report.CaseAccuracy ?? throw new InvalidOperationException(
            $"Report '{path}' has no per-case accuracy. Regenerate it with 'rebuild-report' from its JSONL.");
        var repeated = report.Cases.Where(caseReport => caseReport.SuccessfulRuns > 1).ToArray();

        return new ProviderComparisonRow
        {
            Provider = report.Provider ?? "unknown",
            Model = report.ReturnedModels.Count > 0
                ? string.Join("/", report.ReturnedModels)
                : report.RequestedModel ?? "unknown",
            SuccessfulRuns = report.SuccessfulRuns,
            FailedCalls = report.FailedCalls,
            CorrectRuns = report.Accuracy?.CorrectRuns,
            Accuracy = report.Accuracy?.Accuracy,
            UnsafeAllowRuns = report.Accuracy?.UnsafeAllowRuns,
            UnsafeAllowRate = report.Accuracy?.UnsafeAllowRate,
            OverBlockedRuns = report.Accuracy?.OverBlockedRuns,
            OverBlockedRate = report.Accuracy?.OverBlockedRate,
            CaseAccuracy = caseAccuracy,
            UnstableCases = repeated.Count(caseReport => caseReport.ActionDispositionCounts.Count > 1),
            RepeatedCases = repeated.Length,
            LatencyP50 = report.Latency.P50,
            LatencyP95 = report.Latency.P95,
            CostPerDecisionUsd = report.CostPerDecisionUsd,
            Families = report.Families
        };
    }

    private static void Print(BenchmarkComparison comparison)
    {
        Console.WriteLine($"Suite: {comparison.SuiteId}\n");
        Console.WriteLine(
            $"{"provider",-12} {"model",-14} {"calls",5} {"call acc",16} {"case acc",14} " +
            $"{"unsafe calls",12} {"unsafe cases",12} {"unstable",9} {"p50 ms",7} {"p95 ms",7} {"$/decision",11}");

        foreach (var row in comparison.Providers)
        {
            var cases = row.CaseAccuracy;
            Console.WriteLine(
                $"{Truncate(row.Provider, 12),-12} {Truncate(row.Model, 14),-14} {row.SuccessfulRuns,5} " +
                $"{Ratio(row.CorrectRuns, row.SuccessfulRuns, row.Accuracy),16} " +
                $"{Ratio(cases.CorrectCases, cases.LabelledCases, cases.Accuracy),14} " +
                $"{$"{row.UnsafeAllowRuns}/{row.SuccessfulRuns}",12} " +
                $"{$"{cases.UnsafeAllowCases}/{cases.LabelledCases}",12} " +
                $"{$"{row.UnstableCases}/{row.RepeatedCases}",9} {row.LatencyP50,7:F0} {row.LatencyP95,7:F0} " +
                $"{(row.CostPerDecisionUsd is { } cost ? cost.ToString("F6", CultureInfo.InvariantCulture) : "n/a"),11}");
        }

        Console.WriteLine("\nper-family accuracy: correct cases/cases [correct calls/calls], sorted by name");
        var allFamilies = comparison.Providers
            .SelectMany(row => row.Families.Select(family => family.Family))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Console.WriteLine(
            $"{"family",-34} " +
            string.Join(" ", comparison.Providers.Select(row => $"{Truncate(row.Provider, 20),20}")));

        foreach (var family in allFamilies)
        {
            var cells = comparison.Providers.Select(row =>
            {
                var entry = row.Families.FirstOrDefault(item => item.Family == family);
                return entry is null
                    ? $"{"-",20}"
                    : $"{$"{entry.CorrectCases}/{entry.LabelledCases} [{entry.CorrectRuns}/{entry.LabelledRuns}]",20}";
            });
            Console.WriteLine($"{family,-34} {string.Join(" ", cells)}");
        }
    }

    private static string Ratio(int? correct, int total, double? rate) =>
        correct is { } count && rate is { } value
            ? $"{count}/{total} {value.ToString("P1", CultureInfo.InvariantCulture)}"
            : "-";

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];
}
