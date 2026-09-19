using System.Text.Json;
using DecisionFabric.Core;
using DecisionFabric.Policy;

namespace DecisionFabric.Evals;

internal sealed record EvaluationSuiteDefinition
{
    public required string Id { get; init; }
    public string? Description { get; init; }
    public required DecisionContract Contract { get; init; }

    /// <summary>Destructive-action gate parameters. Mutually exclusive with <see cref="AgentActionPolicy"/>.</summary>
    public ActionPolicyDefinition? ActionPolicy { get; init; }

    /// <summary>Agent tool-call gate parameters. Mutually exclusive with <see cref="ActionPolicy"/>.</summary>
    public AgentActionPolicyDefinition? AgentActionPolicy { get; init; }

    public required IReadOnlyList<EvaluationCaseDefinition> Cases { get; init; }
    public IReadOnlyList<MetamorphicRelationDefinition> MetamorphicRelations { get; init; } = [];
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

    public DestructiveActionGateOptions ToGateOptions() => new()
    {
        RequestThresholds = new NoulPolicyThresholds(NegativeAtOrBelow, PositiveAtOrAbove),
        RequiredIntent = RequiredIntent,
        MinimumIntentConfidence = MinimumIntentConfidence
    };
}

internal sealed record AgentActionPolicyDefinition
{
    public required string RequestQuestionId { get; init; }
    public required string ImpactQuestionId { get; init; }
    public required string ScopeQuestionId { get; init; }
    public required string InstructionStateProperty { get; init; }
    public required string ReadOnlyImpact { get; init; }
    public required IReadOnlyList<string> ApprovalRequiredImpacts { get; init; }
    public double NegativeAtOrBelow { get; init; } = 0.25;
    public double PositiveAtOrAbove { get; init; } = 0.75;
    public double MinimumImpactConfidence { get; init; } = 0.8;
    public double MaximumAutoApprovedScopeExpansion { get; init; } = 0.5;

    public ProposedActionGateOptions ToGateOptions() => new()
    {
        RequestThresholds = new NoulPolicyThresholds(NegativeAtOrBelow, PositiveAtOrAbove),
        ReadOnlyImpact = ReadOnlyImpact,
        ApprovalRequiredImpacts = ApprovalRequiredImpacts.ToHashSet(StringComparer.Ordinal),
        MinimumImpactConfidence = MinimumImpactConfidence,
        MaximumAutoApprovedScopeExpansion = MaximumAutoApprovedScopeExpansion
    };
}

internal sealed record MetamorphicRelationDefinition
{
    public required string Id { get; init; }
    public required string BaselineCaseId { get; init; }
    public required IReadOnlyList<string> VariantCaseIds { get; init; }
    public required string QuestionId { get; init; }
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

    /// <summary>
    /// The gate disposition a correct decision must produce. Cases without one are
    /// exploratory: they are measured for stability but score no accuracy.
    /// </summary>
    public string? ExpectedDisposition { get; init; }

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

internal static class EvaluationSuiteValidator
{
    public static void Validate(EvaluationSuiteDefinition suite)
    {
        ArgumentNullException.ThrowIfNull(suite);

        if (string.IsNullOrWhiteSpace(suite.Id) || suite.Contract.Questions.Count == 0 || suite.Cases.Count == 0)
        {
            throw new InvalidOperationException("A suite requires an id, at least one question, and at least one case.");
        }

        if (suite.ActionPolicy is not null && suite.AgentActionPolicy is not null)
        {
            throw new InvalidOperationException(
                "A suite configures at most one gate: actionPolicy or agentActionPolicy, not both.");
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
        ValidateAgentActionPolicy(suite);
        ValidateExpectedDispositions(suite);
        ValidateMetamorphicRelations(suite);
    }

    private static void ValidateActionPolicy(EvaluationSuiteDefinition suite)
    {
        if (suite.ActionPolicy is not { } policy)
        {
            return;
        }

        RequireQuestion<NoulQuestion>(suite, policy.RequestQuestionId, "request");
        RequireQuestion<ChoiceQuestion>(suite, policy.IntentQuestionId, "intent");
        RequireStateProperty(suite, policy.MessageStateProperty);
        policy.ToGateOptions().Validate();
    }

    private static void ValidateAgentActionPolicy(EvaluationSuiteDefinition suite)
    {
        if (suite.AgentActionPolicy is not { } policy)
        {
            return;
        }

        RequireQuestion<NoulQuestion>(suite, policy.RequestQuestionId, "request");
        var impact = RequireQuestion<ChoiceQuestion>(suite, policy.ImpactQuestionId, "impact");
        RequireQuestion<ScoreQuestion>(suite, policy.ScopeQuestionId, "scope");
        RequireStateProperty(suite, policy.InstructionStateProperty);

        foreach (var choice in policy.ApprovalRequiredImpacts.Append(policy.ReadOnlyImpact))
        {
            if (!impact.Criteria.ContainsKey(choice))
            {
                throw new InvalidOperationException(
                    $"Agent action policy references impact '{choice}', which question " +
                    $"'{policy.ImpactQuestionId}' does not define.");
            }
        }

        policy.ToGateOptions().Validate();
    }

    private static void ValidateExpectedDispositions(EvaluationSuiteDefinition suite)
    {
        var permitted = suite switch
        {
            { ActionPolicy: not null } => Enum.GetNames<DestructiveActionDisposition>(),
            { AgentActionPolicy: not null } => Enum.GetNames<ProposedActionDisposition>(),
            _ => []
        };

        foreach (var testCase in suite.Cases.Where(testCase => testCase.ExpectedDisposition is not null))
        {
            if (permitted.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Case '{testCase.Id}' labels a disposition, but the suite configures no gate to produce one.");
            }

            if (!permitted.Contains(testCase.ExpectedDisposition, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Case '{testCase.Id}' expects disposition '{testCase.ExpectedDisposition}'. " +
                    $"This gate produces one of: {string.Join(", ", permitted)}.");
            }
        }
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

    private static TQuestion RequireQuestion<TQuestion>(
        EvaluationSuiteDefinition suite,
        string questionId,
        string role)
        where TQuestion : DecisionQuestion
    {
        if (!suite.Contract.Questions.TryGetValue(questionId, out var question) ||
            question is not TQuestion typed)
        {
            throw new InvalidOperationException(
                $"Policy {role} question '{questionId}' must be a {typeof(TQuestion).Name}.");
        }

        return typed;
    }

    private static void RequireStateProperty(EvaluationSuiteDefinition suite, string property)
    {
        if (suite.Cases.Any(testCase =>
                !testCase.State.TryGetProperty(property, out var value) ||
                value.ValueKind != JsonValueKind.String))
        {
            throw new InvalidOperationException(
                $"Every policy case requires string state property '{property}'.");
        }
    }
}
