using System.Text.Json;
using DecisionFabric.Core;

namespace DecisionFabric.Tests;

public sealed class DecisionFabricTests
{
    private static readonly DecisionContract Contract = new()
    {
        Id = "test-contract",
        Version = "2.1.0",
        Questions = new Dictionary<string, DecisionQuestion>
        {
            ["requested"] = new NoulQuestion { Instructions = "Requested?" },
            ["intent"] = new ChoiceQuestion
            {
                Instructions = "Intent?",
                Criteria = new Dictionary<string, string?> { ["a"] = "A", ["b"] = "B" }
            },
            ["urgency"] = new ScoreQuestion { Instructions = "Urgency?", Criteria = ["low", "high"] }
        }
    };

    [Fact]
    public async Task EvaluatesWholeContractInOneProviderRequestAndAppliesPackPolicy()
    {
        var provider = new RecordingProvider(ValidAnswers());
        var fabric = new DefaultDecisionFabric(provider, new DecisionFabricOptions { Model = "jev-test" });

        var result = await fabric.EvaluateAsync(new TestPack(), "hello");

        var request = Assert.Single(provider.Requests);
        Assert.Same(Contract, request.Contract);
        Assert.Equal("jev-test", request.Model);
        Assert.Equal("hello", request.State.GetProperty("text").GetString());
        Assert.Equal("requested=0.9;intent=a;urgency=1", result.Outcome);
        Assert.Equal("returned-model", result.Model);
        Assert.Equal("test-contract", result.ContractId);
        Assert.Equal("2.1.0", result.ContractVersion);
        Assert.Equal("test-policy/v1", result.PolicyVersion);
        Assert.Equal(new DecisionUsage(10, 3), result.Usage);
        Assert.Equal(TimeSpan.FromMilliseconds(42), result.Duration);
    }

    [Fact]
    public async Task MissingAnswerFailsClosedBeforePolicyRuns()
    {
        var answers = ValidAnswers();
        answers.Remove("urgency");
        var pack = new TestPack();

        var exception = await Assert.ThrowsAsync<DecisionContractViolationException>(() =>
            new DefaultDecisionFabric(new RecordingProvider(answers)).EvaluateAsync(pack, "x"));

        Assert.Equal("urgency", exception.QuestionId);
        Assert.Equal(0, pack.DecideCalls);
    }

    [Fact]
    public async Task MismatchedAnswerTypeIsRejected()
    {
        var answers = ValidAnswers();
        answers["requested"] = new ScoreAnswer
        {
            Score = 1,
            Legend = new Dictionary<string, string>(),
            Probabilities = new Dictionary<string, double>(),
            Confidence = 0.5
        };

        var exception = await Assert.ThrowsAsync<DecisionContractViolationException>(() =>
            new DefaultDecisionFabric(new RecordingProvider(answers)).EvaluateAsync(new TestPack(), "x"));

        Assert.Equal("requested", exception.QuestionId);
    }

    [Fact]
    public async Task UndeclaredChoiceIsRejected()
    {
        var answers = ValidAnswers();
        answers["intent"] = Choice("c", 0.9);

        var exception = await Assert.ThrowsAsync<DecisionContractViolationException>(() =>
            new DefaultDecisionFabric(new RecordingProvider(answers)).EvaluateAsync(new TestPack(), "x"));

        Assert.Equal("intent", exception.QuestionId);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public async Task OutOfRangeProbabilityIsRejected(double noul)
    {
        var answers = ValidAnswers();
        answers["requested"] = new NoulAnswer { Noul = noul };

        await Assert.ThrowsAsync<DecisionContractViolationException>(() =>
            new DefaultDecisionFabric(new RecordingProvider(answers)).EvaluateAsync(new TestPack(), "x"));
    }

    [Fact]
    public void PolicyFingerprintIsStableAndChangesWithParameters()
    {
        var first = PolicyFingerprint.Create("gate", new { Negative = 0.25, Positive = 0.75 });
        var same = PolicyFingerprint.Create("gate", new { Negative = 0.25, Positive = 0.75 });
        var changed = PolicyFingerprint.Create("gate", new { Negative = 0.25, Positive = 0.8 });

        Assert.Equal(first, same);
        Assert.NotEqual(first, changed);
        Assert.Matches("^gate/sha256:[0-9a-f]{12}$", first);
    }

    private static Dictionary<string, DecisionAnswer> ValidAnswers() => new()
    {
        ["requested"] = new NoulAnswer { Noul = 0.9 },
        ["intent"] = Choice("a", 0.8),
        ["urgency"] = new ScoreAnswer
        {
            Score = 1,
            Legend = new Dictionary<string, string> { ["0"] = "low", ["1"] = "high" },
            Probabilities = new Dictionary<string, double> { ["0"] = 0.2, ["1"] = 0.8 },
            Confidence = 0.8
        }
    };

    private static ChoiceAnswer Choice(string choice, double confidence) => new()
    {
        Choice = choice,
        Probabilities = new Dictionary<string, double> { [choice] = confidence },
        Confidence = confidence
    };

    private sealed class TestPack : IDecisionPack<string, string>
    {
        public int DecideCalls { get; private set; }

        public DecisionContract Contract => DecisionFabricTests.Contract;

        public string PolicyVersion => "test-policy/v1";

        public JsonElement CreateState(string input) =>
            JsonSerializer.SerializeToElement(new { text = input });

        public string Decide(string input, DecisionEvidence evidence)
        {
            DecideCalls++;
            return $"requested={evidence.Noul("requested").Noul};" +
                $"intent={evidence.Choice("intent").Choice};" +
                $"urgency={evidence.Score("urgency").Score}";
        }
    }

    internal sealed class RecordingProvider(IReadOnlyDictionary<string, DecisionAnswer> answers)
        : IDecisionProvider
    {
        public List<DecisionEvaluationRequest> Requests { get; } = [];

        public Task<DecisionEvaluationResponse> EvaluateAsync(
            DecisionEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new DecisionEvaluationResponse
            {
                Model = "returned-model",
                Answers = answers,
                Usage = new DecisionUsage(10, 3),
                Duration = TimeSpan.FromMilliseconds(42)
            });
        }
    }
}
