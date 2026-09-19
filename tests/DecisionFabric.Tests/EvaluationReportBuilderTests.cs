using System.Text.Json;
using DecisionFabric.Core;
using DecisionFabric.Evals;

namespace DecisionFabric.Tests;

public sealed class EvaluationReportBuilderTests
{
    [Fact]
    public void BuildReportsStableMetamorphicRelation()
    {
        var suite = CreateSuite();
        var records = new[]
        {
            CreateRecord("baseline", 1, 0.02, "dispute_transaction"),
            CreateRecord("baseline", 2, 0.03, "dispute_transaction"),
            CreateRecord("variant", 1, 0.04, "dispute_transaction"),
            CreateRecord("variant", 2, 0.05, "dispute_transaction")
        };

        var report = EvaluationReportBuilder.Build(suite, records);

        var comparison = Assert.Single(report.MetamorphicComparisons);
        Assert.True(comparison.Passed);
        Assert.False(comparison.DecisionFlipDetected);
        Assert.False(comparison.PrimaryChoiceFlipDetected);
        Assert.All(report.Cases, caseReport => Assert.False(caseReport.DecisionFlipDetected));
    }

    [Fact]
    public void BuildReportsDecisionAndChoiceFlipAcrossVariant()
    {
        var suite = CreateSuite();
        var records = new[]
        {
            CreateRecord("baseline", 1, 0.03, "dispute_transaction"),
            CreateRecord("variant", 1, 0.85, "block_card")
        };

        var report = EvaluationReportBuilder.Build(suite, records);

        var comparison = Assert.Single(report.MetamorphicComparisons);
        Assert.False(comparison.Passed);
        Assert.True(comparison.DecisionFlipDetected);
        Assert.True(comparison.PrimaryChoiceFlipDetected);
        Assert.NotEmpty(comparison.Failures);
    }

    private static EvaluationSuiteDefinition CreateSuite() =>
        new()
        {
            Id = "metamorphic-test",
            Contract = new DecisionContract
            {
                Id = "test-contract",
                Version = "1.0.0",
                Questions = new Dictionary<string, DecisionQuestion>
                {
                    ["block_card_requested"] = new NoulQuestion
                    {
                        Instructions = "Should the card be blocked?"
                    },
                    ["primary_intent"] = new ChoiceQuestion
                    {
                        Instructions = "What is the primary intent?",
                        Criteria = new Dictionary<string, string?>
                        {
                            ["block_card"] = "Block the card.",
                            ["dispute_transaction"] = "Dispute the transaction."
                        }
                    }
                }
            },
            Cases =
            [
                CreateCase("baseline"),
                CreateCase("variant")
            ],
            MetamorphicRelations =
            [
                new MetamorphicRelationDefinition
                {
                    Id = "meaning-preserving-variant",
                    BaselineCaseId = "baseline",
                    VariantCaseIds = ["variant"],
                    MaximumMeanDelta = 0.05
                }
            ]
        };

    private static EvaluationCaseDefinition CreateCase(string id) =>
        new()
        {
            Id = id,
            Family = "test",
            State = JsonSerializer.SerializeToElement(new { customer_message = id }),
            ExpectedBehavior = "Stable decision",
            Repetitions = 1
        };

    private static EvaluationRunRecord CreateRecord(
        string caseId,
        int run,
        double noul,
        string choice) =>
        new()
        {
            SuiteId = "metamorphic-test",
            CaseId = caseId,
            Family = "test",
            Run = run,
            StartedAt = DateTimeOffset.UtcNow,
            ContractId = "test-contract",
            ContractVersion = "1.0.0",
            ExpectedBehavior = "Stable decision",
            Response = new DecisionEvaluationResponse
            {
                Model = "test-model",
                Answers = new Dictionary<string, DecisionAnswer>
                {
                    ["block_card_requested"] = new NoulAnswer { Noul = noul },
                    ["primary_intent"] = new ChoiceAnswer
                    {
                        Choice = choice,
                        Probabilities = new Dictionary<string, double> { [choice] = 1 },
                        Confidence = 1
                    }
                },
                Usage = new DecisionUsage(1, 1),
                Duration = TimeSpan.FromMilliseconds(100)
            },
            Passed = true,
            ExpectationFailures = [],
            DurationMilliseconds = 100
        };
}
