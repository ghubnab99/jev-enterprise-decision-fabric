using DecisionFabric.Core;
using DecisionFabric.Policy;

namespace DecisionFabric.Evals;

internal enum NoulDecisionBand
{
    Negative,
    Uncertain,
    Positive
}

internal sealed record NumericSummary(
    int Count,
    double Mean,
    double Minimum,
    double Maximum,
    double PopulationStandardDeviation);

internal sealed record LatencySummary(
    int Count,
    double P50,
    double P95,
    double Mean,
    double Minimum,
    double Maximum);

/// <summary>
/// How often the gate produced the labelled decision, with the full confusion
/// matrix so a high headline number cannot hide one systematically wrong route.
/// </summary>
internal sealed record DispositionAccuracyReport
{
    public required int LabelledCases { get; init; }
    public required int LabelledRuns { get; init; }
    public required int CorrectRuns { get; init; }
    public required double Accuracy { get; init; }

    /// <summary>The disposition that lets an action run unattended.</summary>
    public string? PermissiveDisposition { get; init; }

    /// <summary>
    /// Runs that ran an action unattended when the label withheld that permission.
    /// This is the error class a gate exists to prevent, so it is reported
    /// separately from accuracy: a Deny where the label said RequireApproval is
    /// merely cautious, while an Allow in either case is a failure.
    /// </summary>
    public int? UnsafeAllowRuns { get; init; }
    public double? UnsafeAllowRate { get; init; }

    /// <summary>Runs that withheld permission the label granted: friction, not danger.</summary>
    public int? OverBlockedRuns { get; init; }
    public double? OverBlockedRate { get; init; }

    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Confusion { get; init; }
}

internal sealed record CaseEvaluationReport
{
    public required string CaseId { get; init; }
    public required string Family { get; init; }
    public required int SuccessfulRuns { get; init; }
    public string? ExpectedDisposition { get; init; }
    public int? CorrectDispositionRuns { get; init; }
    public double? DispositionAccuracy { get; init; }
    public NumericSummary? RequestedProbability { get; init; }
    public required IReadOnlyDictionary<string, int> NoulBandCounts { get; init; }
    public required IReadOnlyDictionary<string, int> ActionDispositionCounts { get; init; }
    public required IReadOnlyDictionary<string, int> PrimaryChoiceCounts { get; init; }
    public required IReadOnlyList<string> LinguisticRiskSignals { get; init; }
    public required bool DecisionFlipDetected { get; init; }
    public required bool PrimaryChoiceFlipDetected { get; init; }
    public LatencySummary? Latency { get; init; }
}

internal sealed record MetamorphicComparisonReport
{
    public required string RelationId { get; init; }
    public required string BaselineCaseId { get; init; }
    public required string VariantCaseId { get; init; }
    public required string QuestionId { get; init; }
    public required double BaselineMean { get; init; }
    public required double VariantMean { get; init; }
    public required double AbsoluteMeanDelta { get; init; }
    public required bool DecisionFlipDetected { get; init; }
    public required bool PrimaryChoiceFlipDetected { get; init; }
    public required bool Passed { get; init; }
    public required IReadOnlyList<string> Failures { get; init; }
}

internal sealed record EvaluationReport
{
    public required string SuiteId { get; init; }
    public required string ContractId { get; init; }
    public required string ContractVersion { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
    public string? Provider { get; init; }
    public string? RequestedModel { get; init; }
    public ProviderPricing? Pricing { get; init; }

    /// <summary>Null when the provider publishes no per-token price.</summary>
    public double? EstimatedCostUsd { get; init; }
    public double? CostPerDecisionUsd { get; init; }

    public required IReadOnlyList<string> ReturnedModels { get; init; }
    public required ReportingQuestions ReportingQuestions { get; init; }
    public required int PlannedRuns { get; init; }
    public required int SuccessfulRuns { get; init; }
    public required int FailedCalls { get; init; }
    public required int AssertionBearingRuns { get; init; }
    public required int PlannedHardAssertions { get; init; }
    public required int FailedHardAssertions { get; init; }
    public required int ExploratoryRuns { get; init; }
    public DispositionAccuracyReport? Accuracy { get; init; }
    public required LatencySummary Latency { get; init; }
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
    public required IReadOnlyList<CaseEvaluationReport> Cases { get; init; }
    public required IReadOnlyList<MetamorphicComparisonReport> MetamorphicComparisons { get; init; }
}

internal static class EvaluationReportBuilder
{
    public static EvaluationReport Build(
        EvaluationSuiteDefinition suite,
        IReadOnlyCollection<EvaluationRunRecord> records,
        ProviderSelection? selection = null)
    {
        ArgumentNullException.ThrowIfNull(suite);
        ArgumentNullException.ThrowIfNull(records);

        var thresholds = ResolveThresholds(suite);
        var questions = GateEvaluation.ResolveReportingQuestions(suite);
        var successfulRecords = records.Where(record => record.Response is not null).ToArray();
        var caseReports = suite.Cases
            .Select(testCase => BuildCaseReport(
                testCase,
                successfulRecords.Where(record => record.CaseId == testCase.Id).ToArray(),
                thresholds,
                questions))
            .ToArray();
        var comparisons = suite.MetamorphicRelations
            .SelectMany(relation => relation.VariantCaseIds.Select(variantCaseId =>
                BuildMetamorphicComparison(relation, variantCaseId, successfulRecords, thresholds, questions)))
            .ToArray();

        var inputTokens = successfulRecords.Sum(record => record.Response!.Usage.InputTokens);
        var outputTokens = successfulRecords.Sum(record => record.Response!.Usage.OutputTokens);
        var cost = selection?.Pricing?.CostUsd(inputTokens, outputTokens);

        return new EvaluationReport
        {
            SuiteId = suite.Id,
            ContractId = suite.Contract.Id,
            ContractVersion = suite.Contract.Version,
            GeneratedAt = DateTimeOffset.UtcNow,
            Provider = selection?.Label,
            RequestedModel = selection?.Model,
            Pricing = selection?.Pricing,
            EstimatedCostUsd = cost,
            CostPerDecisionUsd = successfulRecords.Length == 0 ? null : cost / successfulRecords.Length,
            ReturnedModels = successfulRecords
                .Select(record => record.Response!.Model)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            ReportingQuestions = questions,
            PlannedRuns = suite.Cases.Sum(testCase => testCase.Repetitions),
            SuccessfulRuns = successfulRecords.Length,
            FailedCalls = records.Count(record => record.Response is null),
            AssertionBearingRuns = suite.Cases
                .Where(testCase => testCase.Expectations.Count > 0)
                .Sum(testCase => testCase.Repetitions),
            PlannedHardAssertions = suite.Cases
                .Sum(testCase => testCase.Repetitions * testCase.Expectations.Count),
            FailedHardAssertions = records.Sum(record => record.ExpectationFailures.Count),
            ExploratoryRuns = suite.Cases
                .Where(testCase => testCase.Expectations.Count == 0)
                .Sum(testCase => testCase.Repetitions),
            Accuracy = BuildAccuracyReport(
                successfulRecords,
                GateEvaluation.ResolvePermissiveDisposition(suite)),
            Latency = SummarizeLatency(successfulRecords),
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Cases = caseReports,
            MetamorphicComparisons = comparisons
        };
    }

    private static NoulPolicyThresholds ResolveThresholds(EvaluationSuiteDefinition suite)
    {
        var thresholds = suite switch
        {
            { ActionPolicy: { } destructive } =>
                new NoulPolicyThresholds(destructive.NegativeAtOrBelow, destructive.PositiveAtOrAbove),
            { AgentActionPolicy: { } agentAction } =>
                new NoulPolicyThresholds(agentAction.NegativeAtOrBelow, agentAction.PositiveAtOrAbove),
            _ => new NoulPolicyThresholds(0.25, 0.75)
        };
        thresholds.Validate();
        return thresholds;
    }

    private static DispositionAccuracyReport? BuildAccuracyReport(
        EvaluationRunRecord[] records,
        string? permissiveDisposition)
    {
        var labelled = records.Where(record => record.DispositionMatched is not null).ToArray();
        if (labelled.Length == 0)
        {
            return null;
        }

        int? unsafeAllows = null;
        int? overBlocked = null;
        if (permissiveDisposition is not null)
        {
            unsafeAllows = labelled.Count(record =>
                !string.Equals(record.ExpectedDisposition, permissiveDisposition, StringComparison.Ordinal) &&
                string.Equals(record.ActionDecision!.Disposition, permissiveDisposition, StringComparison.Ordinal));
            overBlocked = labelled.Count(record =>
                string.Equals(record.ExpectedDisposition, permissiveDisposition, StringComparison.Ordinal) &&
                !string.Equals(record.ActionDecision!.Disposition, permissiveDisposition, StringComparison.Ordinal));
        }

        var confusion = labelled
            .GroupBy(record => record.ExpectedDisposition!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<string, int>)group
                    .GroupBy(record => record.ActionDecision!.Disposition, StringComparer.Ordinal)
                    .OrderBy(observed => observed.Key, StringComparer.Ordinal)
                    .ToDictionary(observed => observed.Key, observed => observed.Count(), StringComparer.Ordinal),
                StringComparer.Ordinal);
        var correct = labelled.Count(record => record.DispositionMatched is true);

        return new DispositionAccuracyReport
        {
            LabelledCases = labelled.Select(record => record.CaseId).Distinct(StringComparer.Ordinal).Count(),
            LabelledRuns = labelled.Length,
            CorrectRuns = correct,
            Accuracy = (double)correct / labelled.Length,
            PermissiveDisposition = permissiveDisposition,
            UnsafeAllowRuns = unsafeAllows,
            UnsafeAllowRate = unsafeAllows / (double?)labelled.Length,
            OverBlockedRuns = overBlocked,
            OverBlockedRate = overBlocked / (double?)labelled.Length,
            Confusion = confusion
        };
    }

    private static CaseEvaluationReport BuildCaseReport(
        EvaluationCaseDefinition testCase,
        EvaluationRunRecord[] records,
        NoulPolicyThresholds thresholds,
        ReportingQuestions questions)
    {
        var probabilities = ReadNoulValues(records, questions.NoulQuestionId);
        var bandCounts = probabilities
            .Select(value => DetermineBand(value, thresholds).ToString())
            .GroupBy(value => value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var actionDispositionCounts = records
            .Where(record => record.ActionDecision is not null)
            .Select(record => record.ActionDecision!.Disposition)
            .GroupBy(value => value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var primaryChoiceCounts = ReadChoices(records, questions.ChoiceQuestionId)
            .GroupBy(value => value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var riskSignalUnion = records.Aggregate(
            LinguisticRiskSignal.None,
            (signals, record) => signals | (record.ActionDecision?.LinguisticRiskSignals ?? LinguisticRiskSignal.None));
        var labelledRuns = records.Count(record => record.DispositionMatched is not null);
        var correctRuns = records.Count(record => record.DispositionMatched is true);

        return new CaseEvaluationReport
        {
            CaseId = testCase.Id,
            Family = testCase.Family,
            SuccessfulRuns = records.Length,
            ExpectedDisposition = testCase.ExpectedDisposition,
            CorrectDispositionRuns = labelledRuns == 0 ? null : correctRuns,
            DispositionAccuracy = labelledRuns == 0 ? null : (double)correctRuns / labelledRuns,
            RequestedProbability = probabilities.Length == 0 ? null : Summarize(probabilities),
            NoulBandCounts = bandCounts,
            ActionDispositionCounts = actionDispositionCounts,
            PrimaryChoiceCounts = primaryChoiceCounts,
            LinguisticRiskSignals = Enum.GetValues<LinguisticRiskSignal>()
                .Where(signal => signal != LinguisticRiskSignal.None && riskSignalUnion.HasFlag(signal))
                .Select(signal => signal.ToString())
                .ToArray(),
            DecisionFlipDetected = bandCounts.Count > 1 || actionDispositionCounts.Count > 1,
            PrimaryChoiceFlipDetected = primaryChoiceCounts.Count > 1,
            Latency = records.Length == 0 ? null : SummarizeLatency(records)
        };
    }

    private static MetamorphicComparisonReport BuildMetamorphicComparison(
        MetamorphicRelationDefinition relation,
        string variantCaseId,
        IReadOnlyCollection<EvaluationRunRecord> records,
        NoulPolicyThresholds thresholds,
        ReportingQuestions questions)
    {
        var baselineRecords = records.Where(record => record.CaseId == relation.BaselineCaseId).ToArray();
        var variantRecords = records.Where(record => record.CaseId == variantCaseId).ToArray();
        var baselineValues = ReadNoulValues(baselineRecords, relation.QuestionId);
        var variantValues = ReadNoulValues(variantRecords, relation.QuestionId);
        var failures = new List<string>();

        if (baselineValues.Length == 0)
        {
            failures.Add($"Baseline case '{relation.BaselineCaseId}' has no successful Noul values.");
        }

        if (variantValues.Length == 0)
        {
            failures.Add($"Variant case '{variantCaseId}' has no successful Noul values.");
        }

        var baselineMean = baselineValues.Length == 0 ? double.NaN : baselineValues.Average();
        var variantMean = variantValues.Length == 0 ? double.NaN : variantValues.Average();
        var absoluteMeanDelta = double.IsNaN(baselineMean) || double.IsNaN(variantMean)
            ? double.NaN
            : Math.Abs(baselineMean - variantMean);
        if (!double.IsNaN(absoluteMeanDelta) && absoluteMeanDelta > relation.MaximumMeanDelta)
        {
            failures.Add(
                $"Mean delta {absoluteMeanDelta:F4} exceeds {relation.MaximumMeanDelta:F4}.");
        }

        var baselineBands = baselineValues
            .Select(value => DetermineBand(value, thresholds))
            .Distinct()
            .ToArray();
        var variantBands = variantValues
            .Select(value => DetermineBand(value, thresholds))
            .Distinct()
            .ToArray();
        var sameStableBand = baselineBands.Length == 1 &&
            variantBands.Length == 1 &&
            baselineBands[0] == variantBands[0];
        if (relation.RequireSameDecisionBand && !sameStableBand)
        {
            failures.Add("The baseline and variant do not remain in one identical decision band.");
        }

        var baselineChoices = ReadChoices(baselineRecords, questions.ChoiceQuestionId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var variantChoices = ReadChoices(variantRecords, questions.ChoiceQuestionId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var sameStableChoice = baselineChoices.Length == 1 &&
            variantChoices.Length == 1 &&
            string.Equals(baselineChoices[0], variantChoices[0], StringComparison.Ordinal);
        if (relation.RequireSamePrimaryChoice && !sameStableChoice)
        {
            failures.Add("The baseline and variant do not retain one identical primary choice.");
        }

        return new MetamorphicComparisonReport
        {
            RelationId = relation.Id,
            BaselineCaseId = relation.BaselineCaseId,
            VariantCaseId = variantCaseId,
            QuestionId = relation.QuestionId,
            BaselineMean = baselineMean,
            VariantMean = variantMean,
            AbsoluteMeanDelta = absoluteMeanDelta,
            DecisionFlipDetected = !sameStableBand,
            PrimaryChoiceFlipDetected = !sameStableChoice,
            Passed = failures.Count == 0,
            Failures = failures
        };
    }

    private static double[] ReadNoulValues(
        IEnumerable<EvaluationRunRecord> records,
        string questionId) =>
        records
            .Select(record => record.Response!.Answers.TryGetValue(questionId, out var answer)
                ? answer as NoulAnswer
                : null)
            .Where(answer => answer is not null)
            .Select(answer => answer!.Noul)
            .ToArray();

    private static string[] ReadChoices(
        IEnumerable<EvaluationRunRecord> records,
        string questionId) =>
        records
            .Select(record => record.Response!.Answers.TryGetValue(questionId, out var answer)
                ? answer as ChoiceAnswer
                : null)
            .Where(answer => answer is not null)
            .Select(answer => answer!.Choice)
            .ToArray();

    private static NumericSummary Summarize(double[] values)
    {
        var mean = values.Average();
        return new NumericSummary(
            values.Length,
            mean,
            values.Min(),
            values.Max(),
            Math.Sqrt(values.Average(value => Math.Pow(value - mean, 2))));
    }

    private static LatencySummary SummarizeLatency(IReadOnlyCollection<EvaluationRunRecord> records)
    {
        var durations = records.Select(record => record.DurationMilliseconds).Order().ToArray();
        return durations.Length == 0
            ? new LatencySummary(0, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN)
            : new LatencySummary(
                durations.Length,
                Percentile(durations, 0.50),
                Percentile(durations, 0.95),
                durations.Average(),
                durations[0],
                durations[^1]);
    }

    /// <summary>Nearest-rank percentile over an already sorted sample.</summary>
    private static double Percentile(double[] sorted, double percentile)
    {
        var rank = (int)Math.Ceiling(percentile * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }

    private static NoulDecisionBand DetermineBand(
        double value,
        NoulPolicyThresholds thresholds) =>
        value <= thresholds.NegativeAtOrBelow
            ? NoulDecisionBand.Negative
            : value >= thresholds.PositiveAtOrAbove
                ? NoulDecisionBand.Positive
                : NoulDecisionBand.Uncertain;
}
