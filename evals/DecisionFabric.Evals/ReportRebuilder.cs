using System.Globalization;
using System.Text.Json;

namespace DecisionFabric.Evals;

/// <summary>
/// Rebuilds a report from a run's raw JSONL without calling any provider, so new
/// metrics can be computed over recorded answers. The raw file is only read.
/// It refuses to rebuild when the dataset's labels or the gate no longer
/// reproduce what was recorded: a report must describe the run that happened,
/// not a relabelled or re-policied version of it.
/// </summary>
internal static class ReportRebuilder
{
    public static async Task<int> RunAsync(string[] args)
    {
        string? dataset = null;
        string? runs = null;
        string? report = null;
        string? provider = null;
        string? model = null;
        int? maximumRepetitions = null;
        double? inputPrice = null;
        double? outputPrice = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--dataset" when index + 1 < args.Length:
                    dataset = args[++index];
                    break;
                case "--runs" when index + 1 < args.Length:
                    runs = args[++index];
                    break;
                case "--report" when index + 1 < args.Length:
                    report = args[++index];
                    break;
                case "--provider" when index + 1 < args.Length:
                    provider = args[++index];
                    break;
                case "--model" when index + 1 < args.Length:
                    model = args[++index];
                    break;
                case "--max-repetitions" when index + 1 < args.Length:
                    maximumRepetitions = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--input-price" when index + 1 < args.Length:
                    inputPrice = double.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--output-price" when index + 1 < args.Length:
                    outputPrice = double.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument '{args[index]}'.");
            }
        }

        if (dataset is null || runs is null || provider is null || model is null)
        {
            throw new ArgumentException(
                "rebuild-report needs --dataset, --runs, --provider (the run's label, e.g. claude/low) and --model.");
        }

        if (inputPrice is null != (outputPrice is null))
        {
            throw new ArgumentException("--input-price and --output-price must be given together.");
        }

        var suite = Program.ApplyRepetitionCap(await EvaluationIo.LoadSuiteAsync(dataset), maximumRepetitions);
        EvaluationSuiteValidator.Validate(suite);

        var recorded = new List<EvaluationRunRecord>();
        foreach (var line in await File.ReadAllLinesAsync(runs))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                recorded.Add(JsonSerializer.Deserialize<EvaluationRunRecord>(line, EvaluationIo.JsonOptions)
                    ?? throw new JsonException($"Could not read a record in '{runs}'."));
            }
        }

        var rebuilt = recorded.Select(record => Reproduce(suite, record)).ToArray();
        var planned = suite.Cases.Sum(testCase => testCase.Repetitions);
        if (rebuilt.Length != planned)
        {
            throw new InvalidOperationException(
                $"'{runs}' holds {rebuilt.Length} records but the dataset plans {planned} calls. " +
                "Pass the --max-repetitions the run used.");
        }

        var pricing = inputPrice is { } input && outputPrice is { } output
            ? new ProviderPricing(input, output)
            : provider.StartsWith(Providers.Claude, StringComparison.Ordinal)
                ? Providers.ClaudeListPrice(model)
                : null;
        var built = EvaluationReportBuilder.Build(suite, rebuilt, new ReportProvenance(provider, model, pricing));

        var reportPath = Path.GetFullPath(report ?? Path.ChangeExtension(runs, ".report.json"));
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(built, EvaluationIo.ReportJsonOptions));
        EvaluationConsole.PrintReport(built);
        Console.WriteLine($"Rebuilt from: {Path.GetFullPath(runs)}");
        Console.WriteLine($"Evaluation report: {reportPath}");
        return 0;
    }

    /// <summary>
    /// Re-derives one record from its recorded answers and checks it against what
    /// was written at run time.
    /// </summary>
    internal static EvaluationRunRecord Reproduce(EvaluationSuiteDefinition suite, EvaluationRunRecord record)
    {
        var testCase = suite.Cases.FirstOrDefault(candidate => candidate.Id == record.CaseId)
            ?? throw new InvalidOperationException($"Recorded case '{record.CaseId}' is not in suite '{suite.Id}'.");

        if (!string.Equals(testCase.ExpectedDisposition, record.ExpectedDisposition, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Case '{record.CaseId}' was recorded with label '{record.ExpectedDisposition}' but the dataset " +
                $"now says '{testCase.ExpectedDisposition}'. Rebuilding would silently relabel the run.");
        }

        if (record.Response is not { } response)
        {
            return record;
        }

        var reproduced = EvaluationRunRecord.FromResponse(
            suite,
            testCase,
            record.Run,
            record.StartedAt,
            // Runs recorded before the ApiEnum fix stored the Claude model id JSON-quoted.
            response with { Model = response.Model.Trim('"') });

        if (!string.Equals(
                reproduced.ActionDecision?.Disposition,
                record.ActionDecision?.Disposition,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Case '{record.CaseId}' run {record.Run} was recorded as '{record.ActionDecision?.Disposition}' " +
                $"but the current gate yields '{reproduced.ActionDecision?.Disposition}' from the same answers.");
        }

        return reproduced;
    }
}
