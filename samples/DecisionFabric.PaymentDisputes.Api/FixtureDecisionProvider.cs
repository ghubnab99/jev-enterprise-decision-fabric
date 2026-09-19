using System.Diagnostics;
using DecisionFabric.Core;

namespace DecisionFabric.PaymentDisputes.Api;

internal sealed class FixtureDecisionProvider : IDecisionProvider
{
    private const string FixtureModel = "fixture/jev-1.13.0-recorded";

    private static readonly IReadOnlyDictionary<string, FixtureAnswers> Fixtures =
        new Dictionary<string, FixtureAnswers>(StringComparer.OrdinalIgnoreCase)
        {
            ["Freeze this card immediately. I do not recognize the transaction."] =
                new(0.99, "block_card", 1, 2, 1),
            ["Disable my card now and investigate this payment."] =
                new(0.99, "block_card", 0.96, 2, 1),
            ["I didn't make this payment. Don't block the card yet."] =
                new(0.03, "dispute_transaction", 0.98, 1.93, 0.9),
            ["Keep the card active—actually, maybe freeze it. I'm not sure."] =
                new(0.35, "block_card", 0.56, 1.27, 0.43),
            ["I don't want you not to block my card."] =
                new(0.84, "block_card", 0.77, 1.59, 0.38)
        };

    public Task<DecisionEvaluationResponse> EvaluateAsync(
        DecisionEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var message = request.State.GetProperty("customer_message").GetString() ?? string.Empty;
        var fixture = Fixtures.TryGetValue(message.Trim(), out var recorded)
            ? recorded
            : new FixtureAnswers(0.5, "other", 0.4, 1, 0.3);
        stopwatch.Stop();

        return Task.FromResult(new DecisionEvaluationResponse
        {
            Model = FixtureModel,
            Answers = new Dictionary<string, DecisionAnswer>
            {
                [PaymentDisputeContract.BlockCardRequested] =
                    new NoulAnswer { Noul = fixture.BlockCardRequested },
                [PaymentDisputeContract.PrimaryIntent] = new ChoiceAnswer
                {
                    Choice = fixture.PrimaryIntent,
                    Probabilities = ChoiceProbabilities(fixture.PrimaryIntent, fixture.IntentConfidence),
                    Confidence = fixture.IntentConfidence
                },
                [PaymentDisputeContract.Urgency] = new ScoreAnswer
                {
                    Score = fixture.Urgency,
                    Legend = new Dictionary<string, string>
                    {
                        ["0"] = "No operational urgency is expressed.",
                        ["1"] = "Prompt attention is advisable, but immediate intervention is not requested.",
                        ["2"] = "Immediate intervention or account action is explicitly requested."
                    },
                    Probabilities = ScoreProbabilities(fixture.Urgency),
                    Confidence = fixture.UrgencyConfidence
                }
            },
            Usage = new DecisionUsage(0, 0),
            Duration = stopwatch.Elapsed
        });
    }

    private static Dictionary<string, double> ChoiceProbabilities(string choice, double confidence)
    {
        var probabilities = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["dispute_transaction"] = 0,
            [PaymentDisputeContract.BlockCardIntent] = 0,
            ["request_information"] = 0,
            ["other"] = 0
        };
        probabilities[choice] = confidence;
        probabilities[choice == "other" ? "request_information" : "other"] = 1 - confidence;
        return probabilities;
    }

    private static Dictionary<string, double> ScoreProbabilities(double score)
    {
        var rounded = Math.Clamp((int)Math.Round(score, MidpointRounding.AwayFromZero), 0, 2);
        return new Dictionary<string, double>
        {
            ["0"] = rounded == 0 ? 1 : 0,
            ["1"] = rounded == 1 ? 1 : 0,
            ["2"] = rounded == 2 ? 1 : 0
        };
    }

    private sealed record FixtureAnswers(
        double BlockCardRequested,
        string PrimaryIntent,
        double IntentConfidence,
        double Urgency,
        double UrgencyConfidence);
}
