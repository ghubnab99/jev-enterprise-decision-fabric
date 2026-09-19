using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DecisionFabric.Core;
using DecisionFabric.TypeSafe;

namespace DecisionFabric.Evals;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = RunnerOptions.Parse(args);
            var suite = ApplyRepetitionCap(
                await EvaluationIo.LoadSuiteAsync(options.DatasetPath),
                options.MaximumRepetitions);
            EvaluationSuiteValidator.Validate(suite);

            var plannedCalls = suite.Cases.Sum(testCase => testCase.Repetitions);
            var labelledCases = suite.Cases.Count(testCase => testCase.ExpectedDisposition is not null);
            Console.WriteLine($"Suite: {suite.Id}");
            Console.WriteLine($"Contract: {suite.Contract.Id}@{suite.Contract.Version}");
            Console.WriteLine($"Cases: {suite.Cases.Count}; API calls: {plannedCalls}; questions/call: {suite.Contract.Questions.Count}");
            Console.WriteLine($"Disposition-labelled cases: {labelledCases}/{suite.Cases.Count}");

            if (options.DryRun)
            {
                Console.WriteLine("Dry run passed. Dataset and typed question contracts are valid.");
                return 0;
            }

            var apiKey = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Set TYPESAFE_API_KEY before running live evaluations.");
            }

            var outputPath = Path.GetFullPath(options.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            using var httpClient = new HttpClient();
            var provider = new TypeSafeDecisionProvider(httpClient, apiKey);
            var allRuns = new List<EvaluationRunRecord>();
            var failedCallCount = 0;

            await using var writer = new StreamWriter(outputPath, append: false);
            foreach (var testCase in suite.Cases)
            {
                for (var run = 1; run <= testCase.Repetitions; run++)
                {
                    var record = await ExecuteAsync(suite, testCase, run, provider);
                    await writer.WriteLineAsync(JsonSerializer.Serialize(record, EvaluationIo.JsonOptions));
                    await writer.FlushAsync();
                    allRuns.Add(record);

                    if (record.Response is null)
                    {
                        failedCallCount++;
                    }

                    Console.WriteLine(
                        $"{testCase.Id} [{run}/{testCase.Repetitions}]: {(record.Passed ? "PASS" : "CHECK")}, " +
                        $"{record.ActionDecision?.Disposition ?? "-"}, {record.DurationMilliseconds:F0} ms");
                }
            }

            var report = EvaluationReportBuilder.Build(suite, allRuns);
            var reportPath = Path.GetFullPath(options.ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            await File.WriteAllTextAsync(
                reportPath,
                JsonSerializer.Serialize(report, EvaluationIo.ReportJsonOptions));
            EvaluationConsole.PrintReport(report);
            Console.WriteLine($"Raw JSONL: {outputPath}");
            Console.WriteLine($"Evaluation report: {reportPath}");
            if (failedCallCount > 0)
            {
                Console.Error.WriteLine($"{failedCallCount} API call(s) failed. See the JSONL error fields.");
                return 2;
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    /// <summary>
    /// Trims each case's repetitions so a comparison run can be priced down without
    /// editing the dataset. Cases keep their relative depth: a 10-repetition
    /// stability case still outweighs a single-run breadth case.
    /// </summary>
    private static EvaluationSuiteDefinition ApplyRepetitionCap(
        EvaluationSuiteDefinition suite,
        int? maximumRepetitions) =>
        maximumRepetitions is not { } maximum
            ? suite
            : suite with
            {
                Cases = suite.Cases
                    .Select(testCase => testCase with
                    {
                        Repetitions = Math.Min(testCase.Repetitions, maximum)
                    })
                    .ToArray()
            };

    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The evaluation runner exercises the same provider abstraction consumed by applications and tests.")]
    private static async Task<EvaluationRunRecord> ExecuteAsync(
        EvaluationSuiteDefinition suite,
        EvaluationCaseDefinition testCase,
        int run,
        IDecisionProvider provider)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            var response = await provider.EvaluateAsync(new DecisionEvaluationRequest
            {
                State = testCase.State,
                Contract = suite.Contract
            });
            return EvaluationRunRecord.FromResponse(suite, testCase, run, startedAt, response);
        }
        catch (Exception exception)
        {
            return EvaluationRunRecord.FromFailure(suite, testCase, run, startedAt, exception);
        }
    }
}

internal sealed record RunnerOptions(
    string DatasetPath,
    string OutputPath,
    string ReportPath,
    bool DryRun,
    int? MaximumRepetitions)
{
    public static RunnerOptions Parse(string[] args)
    {
        var dataset = "evals/datasets/payment-card-block-negation-v1.json";
        string? output = null;
        string? report = null;
        var dryRun = false;
        int? maximumRepetitions = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--dataset" when index + 1 < args.Length:
                    dataset = args[++index];
                    break;
                case "--output" when index + 1 < args.Length:
                    output = args[++index];
                    break;
                case "--report" when index + 1 < args.Length:
                    report = args[++index];
                    break;
                case "--max-repetitions" when index + 1 < args.Length:
                    maximumRepetitions = int.Parse(args[++index], CultureInfo.InvariantCulture);
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument '{args[index]}'.");
            }
        }

        output ??= $"artifacts/results/{Path.GetFileNameWithoutExtension(dataset)}-" +
            $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.jsonl";

        if (maximumRepetitions is < 1)
        {
            throw new ArgumentException("--max-repetitions must be at least 1.");
        }

        return new RunnerOptions(
            dataset,
            output,
            report ?? Path.ChangeExtension(output, ".report.json"),
            dryRun,
            maximumRepetitions);
    }
}

internal sealed record EvaluationRunRecord
{
    public required string SuiteId { get; init; }
    public required string CaseId { get; init; }
    public required string Family { get; init; }
    public required int Run { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required string ContractId { get; init; }
    public required string ContractVersion { get; init; }
    public required string ExpectedBehavior { get; init; }
    public string? ExpectedDisposition { get; init; }
    public DecisionEvaluationResponse? Response { get; init; }
    public GateDecisionRecord? ActionDecision { get; init; }

    /// <summary>
    /// Null when the case carries no disposition label or the call failed; otherwise
    /// whether the gate produced the labelled decision.
    /// </summary>
    public bool? DispositionMatched { get; init; }

    public required bool Passed { get; init; }
    public required IReadOnlyList<string> ExpectationFailures { get; init; }
    public string? Error { get; init; }
    public required double DurationMilliseconds { get; init; }

    public static EvaluationRunRecord FromResponse(
        EvaluationSuiteDefinition suite,
        EvaluationCaseDefinition testCase,
        int run,
        DateTimeOffset startedAt,
        DecisionEvaluationResponse response)
    {
        var failures = EvaluateExpectations(testCase.Expectations, response.Answers);
        var gateDecision = GateEvaluation.Evaluate(suite, testCase.State, response.Answers);
        var dispositionMatched = testCase.ExpectedDisposition is { } expected && gateDecision is not null
            ? string.Equals(gateDecision.Disposition, expected, StringComparison.Ordinal)
            : (bool?)null;

        return new EvaluationRunRecord
        {
            SuiteId = suite.Id,
            CaseId = testCase.Id,
            Family = testCase.Family,
            Run = run,
            StartedAt = startedAt,
            ContractId = suite.Contract.Id,
            ContractVersion = suite.Contract.Version,
            ExpectedBehavior = testCase.ExpectedBehavior,
            ExpectedDisposition = testCase.ExpectedDisposition,
            Response = response,
            ActionDecision = gateDecision,
            DispositionMatched = dispositionMatched,
            Passed = failures.Count == 0 && dispositionMatched is not false,
            ExpectationFailures = failures,
            DurationMilliseconds = response.Duration.TotalMilliseconds
        };
    }

    public static EvaluationRunRecord FromFailure(
        EvaluationSuiteDefinition suite,
        EvaluationCaseDefinition testCase,
        int run,
        DateTimeOffset startedAt,
        Exception exception) =>
        new()
        {
            SuiteId = suite.Id,
            CaseId = testCase.Id,
            Family = testCase.Family,
            Run = run,
            StartedAt = startedAt,
            ContractId = suite.Contract.Id,
            ContractVersion = suite.Contract.Version,
            ExpectedBehavior = testCase.ExpectedBehavior,
            ExpectedDisposition = testCase.ExpectedDisposition,
            Passed = false,
            ExpectationFailures = [],
            Error = exception.ToString(),
            DurationMilliseconds = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds
        };

    private static List<string> EvaluateExpectations(
        IReadOnlyDictionary<string, AnswerExpectation> expectations,
        IReadOnlyDictionary<string, DecisionAnswer> answers)
    {
        var failures = new List<string>();
        foreach (var (questionId, expectation) in expectations)
        {
            if (!answers.TryGetValue(questionId, out var answer))
            {
                failures.Add($"Missing answer '{questionId}'.");
                continue;
            }

            var value = answer switch
            {
                NoulAnswer noul => noul.Noul,
                ScoreAnswer score => score.Score,
                _ => (double?)null
            };

            if (expectation.Minimum is { } minimum && value is { } observedMinimum && observedMinimum < minimum)
            {
                failures.Add($"{questionId} value {observedMinimum:F3} was below {minimum:F3}.");
            }

            if (expectation.Maximum is { } maximum && value is { } observedMaximum && observedMaximum > maximum)
            {
                failures.Add($"{questionId} value {observedMaximum:F3} was above {maximum:F3}.");
            }

            if (expectation.Choice is { } expectedChoice &&
                answer is ChoiceAnswer choice &&
                !string.Equals(choice.Choice, expectedChoice, StringComparison.Ordinal))
            {
                failures.Add($"{questionId} chose '{choice.Choice}', expected '{expectedChoice}'.");
            }

            var confidence = answer switch
            {
                ChoiceAnswer choiceAnswer => choiceAnswer.Confidence,
                ScoreAnswer score => score.Confidence,
                _ => (double?)null
            };

            if (expectation.MinimumConfidence is { } minimumConfidence &&
                confidence is { } observedConfidence &&
                observedConfidence < minimumConfidence)
            {
                failures.Add($"{questionId} confidence {observedConfidence:F3} was below {minimumConfidence:F3}.");
            }
        }

        return failures;
    }
}

internal static class EvaluationIo
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static JsonSerializerOptions ReportJsonOptions { get; } = new(JsonOptions)
    {
        WriteIndented = true
    };

    public static async Task<EvaluationSuiteDefinition> LoadSuiteAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<EvaluationSuiteDefinition>(stream, JsonOptions)
            ?? throw new JsonException($"Could not deserialize evaluation suite '{path}'.");
    }
}

internal static class EvaluationConsole
{
    public static void PrintReport(EvaluationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        Console.WriteLine($"\n{report.ReportingQuestions.NoulQuestionId} summary");
        foreach (var caseReport in report.Cases.Where(c => c.RequestedProbability is not null))
        {
            var summary = caseReport.RequestedProbability!;
            Console.WriteLine(
                $"{caseReport.CaseId}: n={summary.Count}, mean={summary.Mean:F4}, " +
                $"min={summary.Minimum:F4}, max={summary.Maximum:F4}, sd={summary.PopulationStandardDeviation:F4}");
        }

        Console.WriteLine("\npolicy and stability summary");
        foreach (var caseReport in report.Cases)
        {
            var dispositions = string.Join(
                ", ",
                caseReport.ActionDispositionCounts.Select(pair => $"{pair.Key}={pair.Value}"));
            var accuracy = caseReport.DispositionAccuracy is { } value ? $"{value:P0}" : "-";
            Console.WriteLine(
                $"{caseReport.CaseId}: expected={caseReport.ExpectedDisposition ?? "-"}, " +
                $"actions=[{dispositions}], correct={accuracy}, " +
                $"decisionFlip={caseReport.DecisionFlipDetected}, " +
                $"choiceFlip={caseReport.PrimaryChoiceFlipDetected}");
        }

        if (report.Accuracy is { } accuracyReport)
        {
            Console.WriteLine(
                $"\ndisposition accuracy: {accuracyReport.CorrectRuns}/{accuracyReport.LabelledRuns} " +
                $"({accuracyReport.Accuracy:P2}) across {accuracyReport.LabelledCases} labelled cases");
            if (accuracyReport.UnsafeAllowRuns is { } unsafeAllows)
            {
                Console.WriteLine(
                    $"unsafe '{accuracyReport.PermissiveDisposition}': {unsafeAllows} " +
                    $"({accuracyReport.UnsafeAllowRate:P2}); " +
                    $"over-blocked: {accuracyReport.OverBlockedRuns} ({accuracyReport.OverBlockedRate:P2})");
            }

            Console.WriteLine("confusion (expected -> observed):");
            foreach (var (expected, observed) in accuracyReport.Confusion)
            {
                var detail = string.Join(", ", observed.Select(pair => $"{pair.Key}={pair.Value}"));
                Console.WriteLine($"  {expected}: {detail}");
            }
        }

        var latency = report.Latency;
        Console.WriteLine(
            $"\nlatency ms: p50={latency.P50:F0}, p95={latency.P95:F0}, " +
            $"mean={latency.Mean:F0}, min={latency.Minimum:F0}, max={latency.Maximum:F0}");
        Console.WriteLine(
            $"tokens: input={report.InputTokens:N0}, output={report.OutputTokens:N0} " +
            $"over {report.SuccessfulRuns:N0} successful runs");

        if (report.MetamorphicComparisons.Count > 0)
        {
            Console.WriteLine("\nmetamorphic comparisons");
            foreach (var comparison in report.MetamorphicComparisons)
            {
                Console.WriteLine(
                    $"{comparison.RelationId}: {comparison.BaselineCaseId} -> {comparison.VariantCaseId}, " +
                    $"delta={comparison.AbsoluteMeanDelta:F4}, " +
                    $"decisionFlip={comparison.DecisionFlipDetected}, " +
                    $"choiceFlip={comparison.PrimaryChoiceFlipDetected}, " +
                    $"{(comparison.Passed ? "PASS" : "CHECK")}");
            }
        }
    }
}
