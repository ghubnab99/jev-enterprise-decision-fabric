using System.Globalization;
using System.Text.Json;

namespace DecisionFabric.Inspector;

/// <summary>
/// The recorded artifacts, read once at startup: the labelled dataset, each leg's report and
/// each leg's JSONL of calls. Aggregates are taken from the reports as written; the archive
/// derives only what a report does not state, and checks those derivations against the report's
/// own counts before it will serve anything.
/// </summary>
public sealed class InspectionArchive
{
    private static readonly JsonDocumentOptions DocumentOptions = new() { AllowTrailingCommas = true };

    private readonly IReadOnlyList<CaseRowView> _rows;
    private readonly IReadOnlyDictionary<string, CaseDetailView> _details;

    private InspectionArchive(
        ArchiveView view,
        IReadOnlyList<CaseRowView> rows,
        IReadOnlyDictionary<string, CaseDetailView> details)
    {
        View = view;
        _rows = rows;
        _details = details;
    }

    public ArchiveView View { get; }

    public IReadOnlyList<CaseRowView> Cases => _rows;

    public CaseDetailView? Case(string caseId) =>
        _details.TryGetValue(caseId, out var detail) ? detail : null;

    public static InspectionArchive Load(InspectorOptions options, string contentRoot)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(contentRoot);

        var root = Path.GetFullPath(Path.Combine(contentRoot, options.ArchiveRoot));
        using var dataset = ReadDocument(Path.Combine(root, options.DatasetPath));
        var suite = dataset.RootElement;

        var cases = suite.GetProperty("cases")
            .EnumerateArray()
            .Select(DatasetCase.From)
            .ToDictionary(item => item.CaseId, StringComparer.Ordinal);

        var legs = new List<LoadedLeg>();
        foreach (var leg in options.Legs)
        {
            using var report = ReadDocument(Path.Combine(root, leg.Report));
            var calls = ReadCalls(Path.Combine(root, leg.Calls));
            try
            {
                legs.Add(LoadedLeg.From(leg, report.RootElement, calls));
            }
            finally
            {
                foreach (var call in calls)
                {
                    call.Dispose();
                }
            }
        }

        var view = new ArchiveView(
            SuiteId: suite.GetProperty("id").GetString() ?? "unknown",
            Description: ReadString(suite, "description"),
            ContractId: suite.GetProperty("contract").GetProperty("id").GetString() ?? "unknown",
            ContractVersion: suite.GetProperty("contract").GetProperty("version").GetString() ?? "unknown",
            AnnotationRules: suite.TryGetProperty("annotationRules", out var rules)
                ? [.. rules.EnumerateArray().Select(rule => rule.GetString() ?? string.Empty)]
                : [],
            AnnotationHistory: ReadString(suite, "annotationHistory"),
            Policy: ReadPolicy(suite.GetProperty("agentActionPolicy")),
            Legs: [.. legs.Select(leg => leg.Summary)],
            Families: BuildFamilies(legs));

        var archive = new InspectionArchive(view, BuildRows(cases, legs), BuildDetails(cases, legs));
        archive.VerifyDerivationsAgainstReports(legs);
        return archive;
    }

    /// <summary>
    /// The list view derives per-case correctness, unsafe allows and over-blocks, which a report
    /// states only as totals. When a derivation disagrees with the report the archive refuses to
    /// start, so the dashboard cannot quietly show different numbers from the committed report.
    /// </summary>
    private void VerifyDerivationsAgainstReports(IReadOnlyList<LoadedLeg> legs)
    {
        var failures = new List<string>();
        foreach (var leg in legs)
        {
            var rows = _rows
                .Select(row => row.Legs.FirstOrDefault(entry => entry.LegId == leg.Summary.Id))
                .OfType<CaseLegRowView>()
                .ToList();

            Compare(failures, leg.Summary.Id, "correct cases", rows.Count(row => row.Correct), leg.Summary.CorrectCases);
            Compare(failures, leg.Summary.Id, "unsafe allow cases", rows.Count(row => row.UnsafeAllow), leg.Summary.UnsafeAllowCases);
            Compare(failures, leg.Summary.Id, "over-blocked cases", rows.Count(row => row.OverBlock), leg.Summary.OverBlockedCases);
            Compare(failures, leg.Summary.Id, "unstable cases", rows.Count(row => !row.Stable), leg.Summary.UnstableCases);
            Compare(failures, leg.Summary.Id, "correct calls", rows.Sum(row => row.CorrectCalls), leg.Summary.CorrectRuns);
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "The inspected runs no longer agree with their reports: " + string.Join("; ", failures) + ".");
        }
    }

    private static void Compare(List<string> failures, string legId, string what, int derived, int reported)
    {
        if (derived != reported)
        {
            failures.Add($"{legId} {what}: the calls give {derived}, the report says {reported}");
        }
    }

    private static IReadOnlyList<CaseRowView> BuildRows(
        IReadOnlyDictionary<string, DatasetCase> cases,
        IReadOnlyList<LoadedLeg> legs) =>
    [
        .. cases.Values
            .OrderBy(item => item.Family, StringComparer.Ordinal)
            .ThenBy(item => item.CaseId, StringComparer.Ordinal)
            .Select(item => new CaseRowView(
                item.CaseId,
                item.Family,
                item.ExpectedDisposition,
                item.Instruction,
                item.Tool,
                [.. legs.Select(leg => leg.Row(item)).OfType<CaseLegRowView>()]))
    ];

    private static Dictionary<string, CaseDetailView> BuildDetails(
        IReadOnlyDictionary<string, DatasetCase> cases,
        IReadOnlyList<LoadedLeg> legs) =>
        cases.Values.ToDictionary(
            item => item.CaseId,
            item => new CaseDetailView(
                item.CaseId,
                item.Family,
                item.ExpectedDisposition,
                item.ExpectedBehavior,
                item.Instruction,
                item.Tool,
                item.ToolArguments,
                item.Repetitions,
                item.Expectations,
                [.. legs.Select(leg => leg.Detail(item)).OfType<CaseLegDetailView>()]),
            StringComparer.Ordinal);

    private static IReadOnlyList<FamilyRowView> BuildFamilies(IReadOnlyList<LoadedLeg> legs) =>
    [
        .. legs
            .SelectMany(leg => leg.Families.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(family => family, StringComparer.Ordinal)
            .Select(family => new FamilyRowView(
                family,
                [
                    .. legs
                        .Where(leg => leg.Families.ContainsKey(family))
                        .Select(leg => leg.Families[family])
                ]))
    ];

    private static GatePolicyView ReadPolicy(JsonElement policy) => new(
        policy.GetProperty("negativeAtOrBelow").GetDouble(),
        policy.GetProperty("positiveAtOrAbove").GetDouble(),
        policy.GetProperty("minimumImpactConfidence").GetDouble(),
        policy.GetProperty("maximumAutoApprovedScopeExpansion").GetDouble(),
        policy.GetProperty("readOnlyImpact").GetString() ?? string.Empty,
        [
            .. policy.GetProperty("approvalRequiredImpacts")
                .EnumerateArray()
                .Select(impact => impact.GetString() ?? string.Empty)
        ]);

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static JsonDocument ReadDocument(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The inspector needs '{path}', which does not exist.", path);
        }

        return JsonDocument.Parse(File.ReadAllText(path), DocumentOptions);
    }

    private static IReadOnlyList<JsonDocument> ReadCalls(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The inspector needs '{path}', which does not exist.", path);
        }

        return
        [
            .. File.ReadLines(path)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonDocument.Parse(line, DocumentOptions))
        ];
    }

    /// <summary>A case as the dataset defines it, before any run.</summary>
    private sealed record DatasetCase(
        string CaseId,
        string Family,
        string ExpectedDisposition,
        string ExpectedBehavior,
        string Instruction,
        string Tool,
        string ToolArguments,
        int Repetitions,
        IReadOnlyList<ExpectationView> Expectations)
    {
        public static DatasetCase From(JsonElement element)
        {
            var state = element.GetProperty("state");
            return new DatasetCase(
                element.GetProperty("id").GetString() ?? "unknown",
                element.GetProperty("family").GetString() ?? "unknown",
                ReadString(element, "expectedDisposition"),
                ReadString(element, "expectedBehavior"),
                ReadString(state, "user_instruction"),
                ReadString(state, "proposed_tool"),
                ReadString(state, "proposed_arguments"),
                element.TryGetProperty("repetitions", out var repetitions) ? repetitions.GetInt32() : 1,
                ReadExpectations(element));
        }

        private static IReadOnlyList<ExpectationView> ReadExpectations(JsonElement element)
        {
            if (!element.TryGetProperty("expectations", out var expectations))
            {
                return [];
            }

            return
            [
                .. expectations.EnumerateObject().Select(expectation => new ExpectationView(
                    expectation.Name,
                    string.Join(", ", expectation.Value.EnumerateObject().Select(Describe))))
            ];
        }

        private static string Describe(JsonProperty property)
        {
            var value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetDouble().ToString("0.##", CultureInfo.InvariantCulture),
                _ => property.Value.ToString()
            };

            return $"{property.Name} {value}";
        }
    }

    /// <summary>One leg's report and calls, indexed by case.</summary>
    private sealed class LoadedLeg
    {
        private LoadedLeg(
            LegView summary,
            IReadOnlyDictionary<string, FamilyLegView> families,
            IReadOnlyDictionary<string, ReportedCase> reportedCases,
            IReadOnlyDictionary<string, IReadOnlyList<CallView>> calls)
        {
            Summary = summary;
            Families = families;
            ReportedCases = reportedCases;
            Calls = calls;
        }

        public LegView Summary { get; }

        public IReadOnlyDictionary<string, FamilyLegView> Families { get; }

        private IReadOnlyDictionary<string, ReportedCase> ReportedCases { get; }

        private IReadOnlyDictionary<string, IReadOnlyList<CallView>> Calls { get; }

        public CaseLegRowView? Row(DatasetCase item)
        {
            if (!ReportedCases.TryGetValue(item.CaseId, out var reported))
            {
                return null;
            }

            var correct = reported.MajorityDisposition is not null &&
                string.Equals(reported.MajorityDisposition, item.ExpectedDisposition, StringComparison.Ordinal);
            var allowed = string.Equals(reported.MajorityDisposition, "Allow", StringComparison.Ordinal);
            var labelAllows = string.Equals(item.ExpectedDisposition, "Allow", StringComparison.Ordinal);

            return new CaseLegRowView(
                Summary.Id,
                reported.MajorityDisposition,
                correct,
                !reported.DecisionFlipDetected,
                reported.SuccessfulRuns,
                reported.CorrectDispositionRuns,
                allowed && !labelAllows,
                labelAllows && reported.MajorityDisposition is not null && !allowed,
                reported.LatencyP50);
        }

        public CaseLegDetailView? Detail(DatasetCase item)
        {
            var row = Row(item);
            return row is null
                ? null
                : new CaseLegDetailView(
                    Summary.Id,
                    Summary.Label,
                    row.MajorityDisposition,
                    row.Correct,
                    row.Stable,
                    Calls.TryGetValue(item.CaseId, out var calls) ? calls : []);
        }

        public static LoadedLeg From(
            InspectorLegOptions options,
            JsonElement report,
            IReadOnlyList<JsonDocument> calls)
        {
            var accuracy = report.GetProperty("accuracy");
            var caseAccuracy = report.GetProperty("caseAccuracy");
            var latency = report.GetProperty("latency");

            var summary = new LegView(
                options.Id,
                string.IsNullOrWhiteSpace(options.Label) ? options.Id : options.Label,
                report.GetProperty("provider").GetString() ?? "unknown",
                report.GetProperty("requestedModel").GetString() ?? "unknown",
                [
                    .. report.GetProperty("returnedModels")
                        .EnumerateArray()
                        .Select(model => model.GetString() ?? string.Empty)
                ],
                report.GetProperty("generatedAt").GetDateTimeOffset(),
                report.GetProperty("successfulRuns").GetInt32(),
                report.GetProperty("failedCalls").GetInt32(),
                accuracy.GetProperty("labelledCases").GetInt32(),
                accuracy.GetProperty("correctRuns").GetInt32(),
                accuracy.GetProperty("accuracy").GetDouble(),
                caseAccuracy.GetProperty("correctCases").GetInt32(),
                caseAccuracy.GetProperty("accuracy").GetDouble(),
                caseAccuracy.GetProperty("method").GetString() ?? string.Empty,
                accuracy.GetProperty("unsafeAllowRuns").GetInt32(),
                caseAccuracy.GetProperty("unsafeAllowCases").GetInt32(),
                accuracy.GetProperty("overBlockedRuns").GetInt32(),
                caseAccuracy.GetProperty("overBlockedCases").GetInt32(),
                caseAccuracy.GetProperty("unstableCases").GetInt32(),
                caseAccuracy.GetProperty("tiedCases").GetInt32(),
                latency.GetProperty("p50").GetDouble(),
                latency.GetProperty("p95").GetDouble(),
                report.GetProperty("inputTokens").GetInt64(),
                report.GetProperty("outputTokens").GetInt64(),
                ReadConfusion(accuracy.GetProperty("confusion")),
                ReadConfusion(caseAccuracy.GetProperty("confusion")));

            var families = report.GetProperty("families")
                .EnumerateArray()
                .ToDictionary(
                    family => family.GetProperty("family").GetString() ?? "unknown",
                    family => new FamilyLegView(
                        options.Id,
                        family.GetProperty("correctCases").GetInt32(),
                        family.GetProperty("labelledCases").GetInt32(),
                        family.GetProperty("correctRuns").GetInt32(),
                        family.GetProperty("labelledRuns").GetInt32()),
                    StringComparer.Ordinal);

            var reportedCases = report.GetProperty("cases")
                .EnumerateArray()
                .ToDictionary(
                    item => item.GetProperty("caseId").GetString() ?? "unknown",
                    ReportedCase.From,
                    StringComparer.Ordinal);

            var callsByCase = calls
                .Select(document => document.RootElement)
                .GroupBy(call => call.GetProperty("caseId").GetString() ?? "unknown", StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<CallView>)
                    [
                        .. group.OrderBy(call => call.GetProperty("run").GetInt32()).Select(ReadCall)
                    ],
                    StringComparer.Ordinal);

            return new LoadedLeg(summary, families, reportedCases, callsByCase);
        }

        private static CallView ReadCall(JsonElement call)
        {
            var response = call.GetProperty("response");
            var decision = call.GetProperty("actionDecision");
            var usage = response.GetProperty("usage");

            return new CallView(
                call.GetProperty("run").GetInt32(),
                response.GetProperty("model").GetString() ?? "unknown",
                call.GetProperty("startedAt").GetDateTimeOffset(),
                call.GetProperty("durationMilliseconds").GetDouble(),
                usage.GetProperty("inputTokens").GetInt64(),
                usage.GetProperty("outputTokens").GetInt64(),
                [.. response.GetProperty("answers").EnumerateObject().Select(ReadAnswer)],
                new GateReadingView(
                    decision.GetProperty("requestedProbability").GetDouble(),
                    decision.GetProperty("observedChoice").GetString() ?? string.Empty,
                    decision.GetProperty("choiceConfidence").GetDouble(),
                    decision.GetProperty("scopeExpansion").GetDouble(),
                    decision.GetProperty("linguisticRiskSignals").GetString() ?? "none",
                    [
                        .. decision.GetProperty("reasons")
                            .EnumerateArray()
                            .Select(reason => reason.GetString() ?? string.Empty)
                    ]),
                decision.GetProperty("disposition").GetString() ?? string.Empty,
                call.GetProperty("expectedDisposition").GetString() ?? string.Empty,
                call.GetProperty("dispositionMatched").GetBoolean(),
                call.TryGetProperty("expectationFailures", out var failures)
                    ? [.. failures.EnumerateArray().Select(failure => failure.GetString() ?? string.Empty)]
                    : []);
        }

        private static AnswerView ReadAnswer(JsonProperty answer)
        {
            var value = answer.Value;
            return new AnswerView(
                answer.Name,
                value.GetProperty("type").GetString() ?? "unknown",
                value.TryGetProperty("noul", out var noul) ? noul.GetDouble() : null,
                value.TryGetProperty("choice", out var choice) ? choice.GetString() : null,
                value.TryGetProperty("score", out var score) ? score.GetDouble() : null,
                value.TryGetProperty("confidence", out var confidence) ? confidence.GetDouble() : null,
                value.TryGetProperty("probabilities", out var probabilities)
                    ? probabilities.EnumerateObject().ToDictionary(
                        item => item.Name,
                        item => item.Value.GetDouble(),
                        StringComparer.Ordinal)
                    : null);
        }

        private static Dictionary<string, IReadOnlyDictionary<string, int>> ReadConfusion(
            JsonElement confusion) =>
            confusion.EnumerateObject().ToDictionary(
                label => label.Name,
                label => (IReadOnlyDictionary<string, int>)label.Value.EnumerateObject().ToDictionary(
                    observed => observed.Name,
                    observed => observed.Value.GetInt32(),
                    StringComparer.Ordinal),
                StringComparer.Ordinal);

        private sealed record ReportedCase(
            int SuccessfulRuns,
            int CorrectDispositionRuns,
            string? MajorityDisposition,
            bool DecisionFlipDetected,
            double LatencyP50)
        {
            public static ReportedCase From(JsonElement element) => new(
                element.GetProperty("successfulRuns").GetInt32(),
                element.GetProperty("correctDispositionRuns").GetInt32(),
                element.TryGetProperty("majorityDisposition", out var majority) &&
                    majority.ValueKind == JsonValueKind.String
                        ? majority.GetString()
                        : null,
                element.GetProperty("decisionFlipDetected").GetBoolean(),
                element.GetProperty("latency").GetProperty("p50").GetDouble());
        }
    }
}
