using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using DecisionFabric.Core;
using DecisionFabric.Policy;
using DecisionFabric.TypeSafe;

namespace DecisionFabric.Evals;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly JsonSerializerOptions ReportJsonOptions = new(JsonOptions)
    {
        WriteIndented = true
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
            var allRuns = new List<EvaluationRunRecord>();
            var successfulRuns = new List<EvaluationRunRecord>();
            var failedCallCount = 0;

            await using var writer = new StreamWriter(outputPath, append: false);
            foreach (var testCase in suite.Cases)
            {
                for (var run = 1; run <= testCase.Repetitions; run++)
                {
                    var record = await ExecuteAsync(suite, testCase, run, provider);
                    await writer.WriteLineAsync(JsonSerializer.Serialize(record, JsonOptions));
                    await writer.FlushAsync();
                    allRuns.Add(record);

                    if (record.Response is not null)
                    {
                        successfulRuns.Add(record);
                    }
                    else
                    {
                        failedCallCount++;
                    }

                    Console.WriteLine($"{testCase.Id} [{run}/{testCase.Repetitions}]: {(record.Passed ? "PASS" : "CHECK")}, {record.DurationMilliseconds:F0} ms");
                }
            }

            PrintNoulSummary(successfulRuns);
            var report = EvaluationReportBuilder.Build(suite, allRuns);
            var reportPath = Path.GetFullPath(options.ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            await File.WriteAllTextAsync(
                reportPath,
                JsonSerializer.Serialize(report, ReportJsonOptions));
            PrintEvaluationReport(report);
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

        ValidateActionPolicy(suite);
        ValidateMetamorphicRelations(suite);
    }

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
            var failures = EvaluateExpectations(testCase.Expectations, response.Answers);
            var actionDecision = EvaluateActionPolicy(suite.ActionPolicy, testCase.State, response.Answers);

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
                ActionDecision = actionDecision,
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

    private static DestructiveActionDecision? EvaluateActionPolicy(
        ActionPolicyDefinition? policy,
        JsonElement state,
        IReadOnlyDictionary<string, DecisionAnswer> answers)
    {
        if (policy is null)
        {
            return null;
        }

        var message = state.GetProperty(policy.MessageStateProperty).GetString() ?? string.Empty;
        var actionRequested = (NoulAnswer)answers[policy.RequestQuestionId];
        var primaryIntent = (ChoiceAnswer)answers[policy.IntentQuestionId];

        return DestructiveActionGate.Evaluate(
            new DestructiveActionEvidence
            {
                ActionRequested = actionRequested,
                PrimaryIntent = primaryIntent,
                LinguisticRiskSignals = LinguisticRiskDetector.Detect(message)
            },
            new DestructiveActionGateOptions
            {
                RequestThresholds = new NoulPolicyThresholds(
                    policy.NegativeAtOrBelow,
                    policy.PositiveAtOrAbove),
                RequiredIntent = policy.RequiredIntent,
                MinimumIntentConfidence = policy.MinimumIntentConfidence
            });
    }

    private static void ValidateActionPolicy(EvaluationSuiteDefinition suite)
    {
        if (suite.ActionPolicy is not { } policy)
        {
            return;
        }

        if (!suite.Contract.Questions.TryGetValue(policy.RequestQuestionId, out var requestQuestion) ||
            requestQuestion is not NoulQuestion)
        {
            throw new InvalidOperationException(
                $"Action policy request question '{policy.RequestQuestionId}' must be a Noul question.");
        }

        if (!suite.Contract.Questions.TryGetValue(policy.IntentQuestionId, out var intentQuestion) ||
            intentQuestion is not ChoiceQuestion)
        {
            throw new InvalidOperationException(
                $"Action policy intent question '{policy.IntentQuestionId}' must be a Choice question.");
        }

        if (suite.Cases.Any(testCase =>
                !testCase.State.TryGetProperty(policy.MessageStateProperty, out var message) ||
                message.ValueKind != JsonValueKind.String))
        {
            throw new InvalidOperationException(
                $"Every action-policy case requires string state property '{policy.MessageStateProperty}'.");
        }

        new DestructiveActionGateOptions
        {
            RequestThresholds = new NoulPolicyThresholds(
                policy.NegativeAtOrBelow,
                policy.PositiveAtOrAbove),
            RequiredIntent = policy.RequiredIntent,
            MinimumIntentConfidence = policy.MinimumIntentConfidence
        }.Validate();
    }

    private static void ValidateMetamorphicRelations(EvaluationSuiteDefinition suite)
    {
        var caseIds = suite.Cases.Select(testCase => testCase.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var relation in suite.MetamorphicRelations)
        {
            if (string.IsNullOrWhiteSpace(relation.Id) ||
                !caseIds.Contains(relation.BaselineCaseId) ||
                relation.VariantCaseIds.Count == 0 ||
                relation.VariantCaseIds.Any(variantCaseId => !caseIds.Contains(variantCaseId)))
            {
                throw new InvalidOperationException(
                    $"Metamorphic relation '{relation.Id}' references missing or empty case definitions.");
            }

            if (!suite.Contract.Questions.TryGetValue(relation.QuestionId, out var question) ||
                question is not NoulQuestion)
            {
                throw new InvalidOperationException(
                    $"Metamorphic relation '{relation.Id}' requires a Noul question.");
            }

            if (relation.MaximumMeanDelta is < 0 or > 1)
            {
                throw new InvalidOperationException(
                    $"Metamorphic relation '{relation.Id}' maximum mean delta must be between 0 and 1.");
            }
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

    private static void PrintEvaluationReport(EvaluationReport report)
    {
        Console.WriteLine("\npolicy and stability summary");
        foreach (var caseReport in report.Cases)
        {
            var dispositions = string.Join(
                ", ",
                caseReport.ActionDispositionCounts.Select(pair => $"{pair.Key}={pair.Value}"));
            Console.WriteLine(
                $"{caseReport.CaseId}: actions=[{dispositions}], " +
                $"decisionFlip={caseReport.DecisionFlipDetected}, " +
                $"choiceFlip={caseReport.PrimaryChoiceFlipDetected}");
        }

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

internal sealed record EvaluationSuiteDefinition
{
    public required string Id { get; init; }
    public string? Description { get; init; }
    public required DecisionContract Contract { get; init; }
    public ActionPolicyDefinition? ActionPolicy { get; init; }
    public required IReadOnlyList<EvaluationCaseDefinition> Cases { get; init; }
    public IReadOnlyList<MetamorphicRelationDefinition> MetamorphicRelations { get; init; } =
        [];
}

internal sealed record ActionPolicyDefinition
{
    public required string RequestQuestionId { get; init; }
    public required string IntentQuestionId { get; init; }
    public required string RequiredIntent { get; init; }
    public required string MessageStateProperty { get; init; }
    public double NegativeAtOrBelow { get; init; } = 0.25;
    public double PositiveAtOrAbove { get; init; } = 0.75;
    public double MinimumIntentConfidence { get; init; } = 0.8;
}

internal sealed record MetamorphicRelationDefinition
{
    public required string Id { get; init; }
    public required string BaselineCaseId { get; init; }
    public required IReadOnlyList<string> VariantCaseIds { get; init; }
    public string QuestionId { get; init; } = "block_card_requested";
    public double MaximumMeanDelta { get; init; } = 0.05;
    public bool RequireSameDecisionBand { get; init; } = true;
    public bool RequireSamePrimaryChoice { get; init; } = true;
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
    public DestructiveActionDecision? ActionDecision { get; init; }
    public required bool Passed { get; init; }
    public required IReadOnlyList<string> ExpectationFailures { get; init; }
    public string? Error { get; init; }
    public required double DurationMilliseconds { get; init; }
}

internal sealed record RunnerOptions(string DatasetPath, string OutputPath, string ReportPath, bool DryRun)
{
    public static RunnerOptions Parse(string[] args)
    {
        var dataset = "evals/datasets/payment-card-block-negation-v1.json";
        var output = $"artifacts/results/payment-card-block-negation-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.jsonl";
        string? report = null;
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
                case "--report" when index + 1 < args.Length:
                    report = args[++index];
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument '{args[index]}'.");
            }
        }

        return new RunnerOptions(
            dataset,
            output,
            report ?? Path.ChangeExtension(output, ".report.json"),
            dryRun);
    }
}
