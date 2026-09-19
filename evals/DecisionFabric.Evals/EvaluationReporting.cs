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

internal sealed record CaseEvaluationReport
{
    public required string CaseId { get; init; }
    public required string Family { get; init; }
    public required int SuccessfulRuns { get; init; }
    public NumericSummary? RequestedProbability { get; init; }
    public required IReadOnlyDictionary<string, int> NoulBandCounts { get; init; }
    public required IReadOnlyDictionary<string, int> ActionDispositionCounts { get; init; }
    public required IReadOnlyDictionary<string, int> PrimaryChoiceCounts { get; init; }
    public required IReadOnlyList<string> LinguisticRiskSignals { get; init; }
    public required bool DecisionFlipDetected { get; init; }
    public required bool PrimaryChoiceFlipDetected { get; init; }
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
    public required IReadOnlyList<string> ReturnedModels { get; init; }
    public required int PlannedRuns { get; init; }
    public required int SuccessfulRuns { get; init; }
    public required int FailedCalls { get; init; }
    public required int AssertionBearingRuns { get; init; }
    public required int PlannedHardAssertions { get; init; }
    public required int FailedHardAssertions { get; init; }
    public required int ExploratoryRuns { get; init; }
    public required IReadOnlyList<CaseEvaluationReport> Cases { get; init; }
    public required IReadOnlyList<MetamorphicComparisonReport> MetamorphicComparisons { get; init; }
}

internal static class EvaluationReportBuilder
{
    public static EvaluationReport Build(
        EvaluationSuiteDefinition suite,
        IReadOnlyCollection<EvaluationRunRecord> records)
    {
        ArgumentNullException.ThrowIfNull(suite);
        ArgumentNullException.ThrowIfNull(records);

        var thresholds = suite.ActionPolicy is null
            ? new NoulPolicyThresholds(0.25, 0.75)
            : new NoulPolicyThresholds(
                suite.ActionPolicy.NegativeAtOrBelow,
                suite.ActionPolicy.PositiveAtOrAbove);
        thresholds.Validate();

        var successfulRecords = records.Where(record => record.Response is not null).ToArray();
        var caseReports = suite.Cases
            .Select(testCase => BuildCaseReport(
                testCase,
                successfulRecords.Where(record => record.CaseId == testCase.Id).ToArray(),
                thresholds))
            .ToArray();
        var comparisons = suite.MetamorphicRelations
            .SelectMany(relation => relation.VariantCaseIds.Select(variantCaseId =>
                BuildMetamorphicComparison(relation, variantCaseId, successfulRecords, thresholds)))
            .ToArray();

        return new EvaluationReport
        {
            SuiteId = suite.Id,
            ContractId = suite.Contract.Id,
            ContractVersion = suite.Contract.Version,
            GeneratedAt = DateTimeOffset.UtcNow,
            ReturnedModels = successfulRecords
                .Select(record => record.Response!.Model)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
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
            Cases = caseReports,
            MetamorphicComparisons = comparisons
        };
    }

    private static CaseEvaluationReport BuildCaseReport(
        EvaluationCaseDefinition testCase,
        EvaluationRunRecord[] records,
        NoulPolicyThresholds thresholds)
    {
        var probabilities = ReadNoulValues(records, "block_card_requested");
        var bandCounts = probabilities
            .Select(value => DetermineBand(value, thresholds).ToString())
            .GroupBy(value => value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var actionDispositionCounts = records
            .Where(record => record.ActionDecision is not null)
            .Select(record => record.ActionDecision!.Disposition.ToString())
            .GroupBy(value => value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var primaryChoiceCounts = ReadPrimaryChoices(records)
            .GroupBy(value => value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var riskSignalUnion = records.Aggregate(
            LinguisticRiskSignal.None,
            (signals, record) => signals | (record.ActionDecision?.LinguisticRiskSignals ?? LinguisticRiskSignal.None));

        return new CaseEvaluationReport
        {
            CaseId = testCase.Id,
            Family = testCase.Family,
            SuccessfulRuns = records.Length,
            RequestedProbability = probabilities.Length == 0 ? null : Summarize(probabilities),
            NoulBandCounts = bandCounts,
            ActionDispositionCounts = actionDispositionCounts,
            PrimaryChoiceCounts = primaryChoiceCounts,
            LinguisticRiskSignals = Enum.GetValues<LinguisticRiskSignal>()
                .Where(signal => signal != LinguisticRiskSignal.None && riskSignalUnion.HasFlag(signal))
                .Select(signal => signal.ToString())
                .ToArray(),
            DecisionFlipDetected = bandCounts.Count > 1 || actionDispositionCounts.Count > 1,
            PrimaryChoiceFlipDetected = primaryChoiceCounts.Count > 1
        };
    }

    private static MetamorphicComparisonReport BuildMetamorphicComparison(
        MetamorphicRelationDefinition relation,
        string variantCaseId,
        IReadOnlyCollection<EvaluationRunRecord> records,
        NoulPolicyThresholds thresholds)
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

        var baselineChoices = ReadPrimaryChoices(baselineRecords).Distinct(StringComparer.Ordinal).ToArray();
        var variantChoices = ReadPrimaryChoices(variantRecords).Distinct(StringComparer.Ordinal).ToArray();
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

    private static string[] ReadPrimaryChoices(IEnumerable<EvaluationRunRecord> records) =>
        records
            .Select(record => record.Response!.Answers.TryGetValue("primary_intent", out var answer)
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

    private static NoulDecisionBand DetermineBand(
        double value,
        NoulPolicyThresholds thresholds) =>
        value <= thresholds.NegativeAtOrBelow
            ? NoulDecisionBand.Negative
            : value >= thresholds.PositiveAtOrAbove
                ? NoulDecisionBand.Positive
                : NoulDecisionBand.Uncertain;
}
