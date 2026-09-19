using DecisionFabric.Core;
using DecisionFabric.Hosting;

namespace DecisionFabric.PaymentDisputes.Api;

/// <summary>Values recorded from the payment-card-block-negation-v1 live evaluation.</summary>
internal static class PaymentDisputeFixtures
{
    private const string FixtureModel = "fixture/jev-1.13.0-recorded";

    public static DecisionFixtureSet Create() => new()
    {
        ContractId = PaymentDisputeContract.Definition.Id,
        Model = FixtureModel,
        KeySelector = state =>
            (state.GetProperty("customer_message").GetString() ?? string.Empty).Trim(),
        Recorded = new Dictionary<string, IReadOnlyDictionary<string, DecisionAnswer>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Freeze this card immediately. I do not recognize the transaction."] =
                Answers(0.99, "block_card", 1, 2, 1),
            ["Disable my card now and investigate this payment."] =
                Answers(0.99, "block_card", 0.96, 2, 1),
            ["I didn't make this payment. Don't block the card yet."] =
                Answers(0.03, "dispute_transaction", 0.98, 1.93, 0.9),
            ["Keep the card active—actually, maybe freeze it. I'm not sure."] =
                Answers(0.35, "block_card", 0.56, 1.27, 0.43),
            ["I don't want you not to block my card."] =
                Answers(0.84, "block_card", 0.77, 1.59, 0.38)
        },
        Fallback = Answers(0.5, "other", 0.4, 1, 0.3)
    };

    private static Dictionary<string, DecisionAnswer> Answers(
        double blockCardRequested,
        string primaryIntent,
        double intentConfidence,
        double urgency,
        double urgencyConfidence) =>
        new()
        {
            [PaymentDisputeContract.BlockCardRequested] =
                new NoulAnswer { Noul = blockCardRequested },
            [PaymentDisputeContract.PrimaryIntent] = new ChoiceAnswer
            {
                Choice = primaryIntent,
                Probabilities = ChoiceProbabilities(primaryIntent, intentConfidence),
                Confidence = intentConfidence
            },
            [PaymentDisputeContract.Urgency] = new ScoreAnswer
            {
                Score = urgency,
                Legend = new Dictionary<string, string>
                {
                    ["0"] = "No operational urgency is expressed.",
                    ["1"] = "Prompt attention is advisable, but immediate intervention is not requested.",
                    ["2"] = "Immediate intervention or account action is explicitly requested."
                },
                Probabilities = ScoreProbabilities(urgency),
                Confidence = urgencyConfidence
            }
        };

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
}
