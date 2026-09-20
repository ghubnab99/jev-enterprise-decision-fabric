namespace DecisionFabric.Inspector;

/// <summary>Everything the dashboard needs to draw its overview: suite, policy, legs and families.</summary>
public sealed record ArchiveView(
    string SuiteId,
    string Description,
    string ContractId,
    string ContractVersion,
    IReadOnlyList<string> AnnotationRules,
    string AnnotationHistory,
    GatePolicyView Policy,
    IReadOnlyList<LegView> Legs,
    IReadOnlyList<FamilyRowView> Families);

/// <summary>The gate boundaries the recorded decisions were produced under.</summary>
public sealed record GatePolicyView(
    double NegativeAtOrBelow,
    double PositiveAtOrAbove,
    double MinimumImpactConfidence,
    double MaximumAutoApprovedScopeExpansion,
    string ReadOnlyImpact,
    IReadOnlyList<string> ApprovalRequiredImpacts);

/// <summary>One recorded run. Every number is read from the run's report, not recomputed here.</summary>
public sealed record LegView(
    string Id,
    string Label,
    string Provider,
    string RequestedModel,
    IReadOnlyList<string> ReturnedModels,
    DateTimeOffset GeneratedAt,
    int SuccessfulRuns,
    int FailedCalls,
    int LabelledCases,
    int CorrectRuns,
    double CallAccuracy,
    int CorrectCases,
    double CaseAccuracy,
    string CaseAccuracyMethod,
    int UnsafeAllowRuns,
    int UnsafeAllowCases,
    int OverBlockedRuns,
    int OverBlockedCases,
    int UnstableCases,
    int TiedCases,
    double LatencyP50,
    double LatencyP95,
    long InputTokens,
    long OutputTokens,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> CallConfusion,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> CaseConfusion);

/// <summary>A family's counts for one leg.</summary>
public sealed record FamilyLegView(
    string LegId,
    int CorrectCases,
    int LabelledCases,
    int CorrectRuns,
    int LabelledRuns);

public sealed record FamilyRowView(string Family, IReadOnlyList<FamilyLegView> Legs);

/// <summary>A case as the list shows it: the label, and what each leg concluded.</summary>
public sealed record CaseRowView(
    string CaseId,
    string Family,
    string ExpectedDisposition,
    string Instruction,
    string Tool,
    IReadOnlyList<CaseLegRowView> Legs);

public sealed record CaseLegRowView(
    string LegId,
    string? MajorityDisposition,
    bool Correct,
    bool Stable,
    int Calls,
    int CorrectCalls,
    bool UnsafeAllow,
    bool OverBlock,
    double LatencyP50);

/// <summary>One case in full, with every call each leg made against it.</summary>
public sealed record CaseDetailView(
    string CaseId,
    string Family,
    string ExpectedDisposition,
    string ExpectedBehavior,
    string Instruction,
    string Tool,
    string ToolArguments,
    int Repetitions,
    IReadOnlyList<ExpectationView> Expectations,
    IReadOnlyList<CaseLegDetailView> Legs);

/// <summary>An answer-level expectation the dataset places on this case, before any policy runs.</summary>
public sealed record ExpectationView(string QuestionId, string Requirement);

public sealed record CaseLegDetailView(
    string LegId,
    string Label,
    string? MajorityDisposition,
    bool Correct,
    bool Stable,
    IReadOnlyList<CallView> Calls);

/// <summary>
/// One recorded call, kept in the three layers the evaluation write-up separates: the raw
/// answers, the gate's reading of them, and the disposition that came out.
/// </summary>
public sealed record CallView(
    int Run,
    string Model,
    DateTimeOffset StartedAt,
    double DurationMilliseconds,
    long InputTokens,
    long OutputTokens,
    IReadOnlyList<AnswerView> Answers,
    GateReadingView Gate,
    string Disposition,
    string ExpectedDisposition,
    bool Correct,
    IReadOnlyList<string> ExpectationFailures);

/// <summary>Layer 1: what the provider answered, verbatim from the recorded call.</summary>
public sealed record AnswerView(
    string QuestionId,
    string Type,
    double? Noul,
    string? Choice,
    double? Score,
    double? Confidence,
    IReadOnlyDictionary<string, double>? Probabilities);

/// <summary>Layer 2: the gate's reading, with the reasons it recorded.</summary>
public sealed record GateReadingView(
    double RequestedProbability,
    string ObservedChoice,
    double ChoiceConfidence,
    double ScopeExpansion,
    string LinguisticRiskSignals,
    IReadOnlyList<string> Reasons);
