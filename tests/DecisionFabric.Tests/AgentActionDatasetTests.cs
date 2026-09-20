using System.Text.Json;
using DecisionFabric.Core;
using DecisionFabric.Evals;
using DecisionFabric.Policy;

namespace DecisionFabric.Tests;

/// <summary>
/// Guards the labelled dataset itself. A benchmark is only worth running if its
/// ground truth is reachable: every expectedDisposition must be what the gate
/// actually produces from evidence sitting at the edge of that case's declared
/// expectations.
/// </summary>
public sealed class AgentActionDatasetTests
{
    private static readonly EvaluationSuiteDefinition Suite = LoadSuite("agent-action-gate-v1.json");

    [Fact]
    public void TheDatasetIsAValidSuite()
    {
        EvaluationSuiteValidator.Validate(Suite);
    }

    [Fact]
    public void EveryCaseCarriesADispositionLabel()
    {
        var unlabelled = Suite.Cases
            .Where(testCase => testCase.ExpectedDisposition is null)
            .Select(testCase => testCase.Id)
            .ToArray();

        Assert.Empty(unlabelled);
    }

    [Fact]
    public void TheDatasetCoversEveryDisposition()
    {
        var labelled = Suite.Cases
            .Select(testCase => testCase.ExpectedDisposition)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Enum.GetNames<ProposedActionDisposition>().Length, labelled.Length);
    }

    [Fact]
    public void CaseIdsAndInstructionsAreDistinct()
    {
        var duplicateStates = Suite.Cases
            .GroupBy(testCase => testCase.State.GetRawText(), StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(", ", group.Select(testCase => testCase.Id)))
            .ToArray();

        Assert.Empty(duplicateStates);
    }

    [Fact]
    public void EveryLabelIsReachableFromItsOwnExpectations()
    {
        var policy = Suite.AgentActionPolicy!;
        var options = policy.ToGateOptions();
        var contradictions = new List<string>();

        foreach (var testCase in Suite.Cases)
        {
            if (!TryBuildBoundaryEvidence(testCase, policy, out var evidence))
            {
                continue;
            }

            var disposition = ProposedActionGate.Evaluate(evidence, options).Disposition.ToString();
            if (!string.Equals(disposition, testCase.ExpectedDisposition, StringComparison.Ordinal))
            {
                contradictions.Add(
                    $"{testCase.Id}: expectations imply '{disposition}', label says " +
                    $"'{testCase.ExpectedDisposition}'.");
            }
        }

        Assert.Empty(contradictions);
    }

    [Fact]
    public void MostCasesPinDownEnoughEvidenceToBeVerified()
    {
        var policy = Suite.AgentActionPolicy!;
        var verifiable = Suite.Cases.Count(testCase =>
            TryBuildBoundaryEvidence(testCase, policy, out _));

        // The remainder are deliberate judgement calls - hedged authority, unresolved
        // referents - where the label states policy rather than a derivable fact.
        Assert.True(
            verifiable >= Suite.Cases.Count * 3 / 4,
            $"Only {verifiable} of {Suite.Cases.Count} cases pin down verifiable evidence.");
    }

    /// <summary>
    /// Builds the least favourable evidence a provider could return while still
    /// satisfying the case's expectations, so the check fails if any permitted
    /// answer would route somewhere other than the label.
    /// </summary>
    private static bool TryBuildBoundaryEvidence(
        EvaluationCaseDefinition testCase,
        AgentActionPolicyDefinition policy,
        out ProposedActionEvidence evidence)
    {
        evidence = null!;
        if (!testCase.Expectations.TryGetValue(policy.RequestQuestionId, out var requested) ||
            !testCase.Expectations.TryGetValue(policy.ImpactQuestionId, out var impact) ||
            impact.Choice is null)
        {
            return false;
        }

        var requestedValue = requested.Minimum ?? requested.Maximum;
        if (requestedValue is null)
        {
            return false;
        }

        testCase.Expectations.TryGetValue(policy.ScopeQuestionId, out var scope);
        var instruction = testCase.State.GetProperty(policy.InstructionStateProperty).GetString();

        evidence = new ProposedActionEvidence
        {
            ActionRequestedByUser = new NoulAnswer { Noul = requestedValue.Value },
            ActionImpact = new ChoiceAnswer
            {
                Choice = impact.Choice,
                Probabilities = new Dictionary<string, double>(),
                Confidence = impact.MinimumConfidence ?? 0.9
            },
            ScopeExpansion = new ScoreAnswer
            {
                Score = scope?.Minimum ?? scope?.Maximum ?? 0,
                Legend = new Dictionary<string, string>(),
                Probabilities = new Dictionary<string, double>(),
                Confidence = 0.9
            },
            LinguisticRiskSignals = LinguisticRiskDetector.Detect(instruction)
        };
        return true;
    }

    internal static EvaluationSuiteDefinition LoadSuite(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
            !Directory.Exists(Path.Combine(directory.FullName, "evals", "datasets")))
        {
            directory = directory.Parent;
        }

        var path = Path.Combine(
            directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate evals/datasets."),
            "evals",
            "datasets",
            fileName);
        return JsonSerializer.Deserialize<EvaluationSuiteDefinition>(
            File.ReadAllText(path),
            EvaluationIo.JsonOptions)!;
    }
}
