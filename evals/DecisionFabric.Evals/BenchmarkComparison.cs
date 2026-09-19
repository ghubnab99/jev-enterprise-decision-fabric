using System.Globalization;
using System.Text.Json;

namespace DecisionFabric.Evals;

internal sealed record FamilyAccuracy(string Family, int LabelledRuns, int CorrectRuns, double Accuracy);

internal sealed record ProviderComparisonRow
{
    public required string Provider { get; init; }
    public required string Model { get; init; }
    public required int SuccessfulRuns { get; init; }
    public required int FailedCalls { get; init; }
    public double? Accuracy { get; init; }
    public double? UnsafeAllowRate { get; init; }
    public double? OverBlockedRate { get; init; }

    /// <summary>Cases whose repeated runs did not all reach the same disposition.</summary>
    public required int UnstableCases { get; init; }
    public required int RepeatedCases { get; init; }

    public required double LatencyP50 { get; init; }
    public required double LatencyP95 { get; init; }
    public double? CostPerDecisionUsd { get; init; }
    public required IReadOnlyList<FamilyAccuracy> Families { get; init; }
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
            rows.Add(BuildRow(report));
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

    private static ProviderComparisonRow BuildRow(EvaluationReport report)
    {
        var repeated = report.Cases.Where(caseReport => caseReport.SuccessfulRuns > 1).ToArray();
        var families = report.Cases
            .Where(caseReport => caseReport.CorrectDispositionRuns is not null)
            .GroupBy(caseReport => caseReport.Family, StringComparer.Ordinal)
            .Select(group =>
            {
                var labelled = group.Sum(caseReport => caseReport.SuccessfulRuns);
                var correct = group.Sum(caseReport => caseReport.CorrectDispositionRuns!.Value);
                return new FamilyAccuracy(
                    group.Key,
                    labelled,
                    correct,
                    labelled == 0 ? double.NaN : (double)correct / labelled);
            })
            .OrderBy(family => family.Accuracy)
            .ToArray();

        return new ProviderComparisonRow
        {
            Provider = report.Provider ?? "unknown",
            Model = report.ReturnedModels.Count > 0
                ? string.Join("/", report.ReturnedModels)
                : report.RequestedModel ?? "unknown",
            SuccessfulRuns = report.SuccessfulRuns,
            FailedCalls = report.FailedCalls,
            Accuracy = report.Accuracy?.Accuracy,
            UnsafeAllowRate = report.Accuracy?.UnsafeAllowRate,
            OverBlockedRate = report.Accuracy?.OverBlockedRate,
            UnstableCases = repeated.Count(caseReport => caseReport.ActionDispositionCounts.Count > 1),
            RepeatedCases = repeated.Length,
            LatencyP50 = report.Latency.P50,
            LatencyP95 = report.Latency.P95,
            CostPerDecisionUsd = report.CostPerDecisionUsd,
            Families = families
        };
    }

    private static void Print(BenchmarkComparison comparison)
    {
        Console.WriteLine($"Suite: {comparison.SuiteId}\n");
        Console.WriteLine(
            $"{"provider",-16} {"model",-22} {"runs",5} {"acc",7} {"unsafe",7} {"blocked",8} " +
            $"{"unstable",9} {"p50 ms",7} {"p95 ms",7} {"$/decision",11}");

        foreach (var row in comparison.Providers)
        {
            Console.WriteLine(
                $"{Truncate(row.Provider, 16),-16} {Truncate(row.Model, 22),-22} {row.SuccessfulRuns,5} " +
                $"{Percent(row.Accuracy),7} {Percent(row.UnsafeAllowRate),7} {Percent(row.OverBlockedRate),8} " +
                $"{$"{row.UnstableCases}/{row.RepeatedCases}",9} {row.LatencyP50,7:F0} {row.LatencyP95,7:F0} " +
                $"{(row.CostPerDecisionUsd is { } cost ? cost.ToString("F6", CultureInfo.InvariantCulture) : "-"),11}");
        }

        Console.WriteLine("\nper-family accuracy (worst first)");
        var allFamilies = comparison.Providers
            .SelectMany(row => row.Families.Select(family => family.Family))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Console.WriteLine(
            $"{"family",-30} " +
            string.Join(" ", comparison.Providers.Select(row => $"{Truncate(row.Provider, 14),14}")));

        foreach (var family in allFamilies.OrderBy(family => comparison.Providers
            .Select(row => row.Families.FirstOrDefault(entry => entry.Family == family)?.Accuracy ?? 1)
            .Min()))
        {
            var cells = comparison.Providers.Select(row =>
            {
                var entry = row.Families.FirstOrDefault(item => item.Family == family);
                return entry is null
                    ? $"{"-",14}"
                    : $"{$"{entry.Accuracy:P0} ({entry.CorrectRuns}/{entry.LabelledRuns})",14}";
            });
            Console.WriteLine($"{Truncate(family, 30),-30} {string.Join(" ", cells)}");
        }
    }

    private static string Percent(double? value) =>
        value is { } number ? number.ToString("P1", CultureInfo.InvariantCulture) : "-";

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];
}
