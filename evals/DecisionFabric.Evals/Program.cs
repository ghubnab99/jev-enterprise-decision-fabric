using System.Text.Json;
using System.Text.Json.Serialization;
using DecisionFabric.Core;
using DecisionFabric.TypeSafe;

namespace DecisionFabric.Evals;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = RunnerOptions.Parse(args);
            var suite = await LoadSuiteAsync(options.DatasetPath);
            Validate(suite);

            var plannedCalls = suite.Cases.Sum(testCase => testCase.Repetitions);
            Console.WriteLine($"Suite: {suite.Id}");
            Console.WriteLine($"Contract: {suite.Contract.Id}@{suite.Contract.Version}");
            Console.WriteLine($"Cases: {suite.Cases.Count}; API calls: {plannedCalls}; questions/call: {suite.Contract.Questions.Count}");

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
            var successfulRuns = new List<EvaluationRunRecord>();

            await using var writer = new StreamWriter(outputPath, append: false);
            foreach (var testCase in suite.Cases)
            {
                for (var run = 1; run <= testCase.Repetitions; run++)
                {
                    var record = await ExecuteAsync(suite, testCase, run, provider);
                    await writer.WriteLineAsync(JsonSerializer.Serialize(record, JsonOptions));
                    await writer.FlushAsync();

                    if (record.Response is not null)
                    {
                        successfulRuns.Add(record);
                    }

                    Console.WriteLine($"{testCase.Id} [{run}/{testCase.Repetitions}]: {(record.Passed ? "PASS" : "CHECK")}, {record.DurationMilliseconds:F0} ms");
                }
            }

            PrintNoulSummary(successfulRuns);
            Console.WriteLine($"Raw JSONL: {outputPath}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task<EvaluationSuiteDefinition> LoadSuiteAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<EvaluationSuiteDefinition>(stream, JsonOptions)
            ?? throw new JsonException($"Could not deserialize evaluation suite '{path}'.");
    }

    private static void Validate(EvaluationSuiteDefinition suite)
    {
        if (string.IsNullOrWhiteSpace(suite.Id) || suite.Contract.Questions.Count == 0 || suite.Cases.Count == 0)
        {
            throw new InvalidOperationException("A suite requires an id, at least one question, and at least one case.");
        }

        var duplicate = suite.Cases.GroupBy(testCase => testCase.Id).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate case id '{duplicate.Key}'.");
        }

        if (suite.Cases.Any(testCase => testCase.Repetitions is < 1 or > 100))
        {
            throw new InvalidOperationException("Case repetitions must be between 1 and 100.");
        }

        foreach (var expectedQuestion in suite.Cases.SelectMany(testCase => testCase.Expectations.Keys).Distinct())
        {
            if (!suite.Contract.Questions.ContainsKey(expectedQuestion))
            {
                throw new InvalidOperationException($"Expectation references unknown question '{expectedQuestion}'.");
            }
        }
    }

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
            var failures = EvaluateExpectations(testCase.Expectations, response.Answers);

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
                Response = response,
                Passed = failures.Count == 0,
                ExpectationFailures = failures,
                DurationMilliseconds = response.Duration.TotalMilliseconds
            };
        }
        catch (Exception exception)
        {
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
                Passed = false,
                ExpectationFailures = [],
                Error = exception.ToString(),
                DurationMilliseconds = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds
            };
        }
    }

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

    private static void PrintNoulSummary(IEnumerable<EvaluationRunRecord> runs)
    {
        var groups = runs
            .Where(record => record.Response!.Answers.TryGetValue("block_card_requested", out var answer) && answer is NoulAnswer)
            .GroupBy(record => record.CaseId);

        Console.WriteLine("\nblock_card_requested summary");
        foreach (var group in groups)
        {
            var values = group
                .Select(record => ((NoulAnswer)record.Response!.Answers["block_card_requested"]).Noul)
                .ToArray();
            var mean = values.Average();
            var standardDeviation = Math.Sqrt(values.Average(value => Math.Pow(value - mean, 2)));
            Console.WriteLine($"{group.Key}: n={values.Length}, mean={mean:F4}, min={values.Min():F4}, max={values.Max():F4}, sd={standardDeviation:F4}");
        }
    }
}

internal sealed record EvaluationSuiteDefinition
{
    public required string Id { get; init; }
    public string? Description { get; init; }
    public required DecisionContract Contract { get; init; }
    public required IReadOnlyList<EvaluationCaseDefinition> Cases { get; init; }
}

internal sealed record EvaluationCaseDefinition
{
    public required string Id { get; init; }
    public required string Family { get; init; }
    public required JsonElement State { get; init; }
    public required string ExpectedBehavior { get; init; }
    public int Repetitions { get; init; } = 1;
    public IReadOnlyDictionary<string, AnswerExpectation> Expectations { get; init; } =
        new Dictionary<string, AnswerExpectation>();
}

internal sealed record AnswerExpectation
{
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public string? Choice { get; init; }
    public double? MinimumConfidence { get; init; }
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
    public DecisionEvaluationResponse? Response { get; init; }
    public required bool Passed { get; init; }
    public required IReadOnlyList<string> ExpectationFailures { get; init; }
    public string? Error { get; init; }
    public required double DurationMilliseconds { get; init; }
}

internal sealed record RunnerOptions(string DatasetPath, string OutputPath, bool DryRun)
{
    public static RunnerOptions Parse(string[] args)
    {
        var dataset = "evals/datasets/payment-card-block-negation-v1.json";
        var output = $"artifacts/results/payment-card-block-negation-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.jsonl";
        var dryRun = false;

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
                case "--dry-run":
                    dryRun = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument '{args[index]}'.");
            }
        }

        return new RunnerOptions(dataset, output, dryRun);
    }
}
