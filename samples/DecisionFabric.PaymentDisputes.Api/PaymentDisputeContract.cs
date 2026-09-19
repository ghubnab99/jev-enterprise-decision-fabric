using DecisionFabric.Core;

namespace DecisionFabric.PaymentDisputes.Api;

internal static class PaymentDisputeContract
{
    public const string BlockCardRequested = "block_card_requested";
    public const string PrimaryIntent = "primary_intent";
    public const string Urgency = "urgency";
    public const string BlockCardIntent = "block_card";

    public static DecisionContract Definition { get; } = new()
    {
        Id = "payment-dispute-triage",
        Version = "1.0.0",
        Description = "Atomic customer-intent decisions for payment dispute intake.",
        Questions = new Dictionary<string, DecisionQuestion>
        {
            [BlockCardRequested] = new NoulQuestion
            {
                Instructions = "Does `customer_message` indicate that the customer wants their card to be blocked or frozen now?",
                Criteria = new NoulCriteria
                {
                    True = "The customer directly asks or clearly indicates that the card should be blocked, frozen, disabled, or stopped now.",
                    False = "The customer does not request blocking, is only asking for information, or explicitly asks that the card remain active or not be blocked yet."
                }
            },
            [PrimaryIntent] = new ChoiceQuestion
            {
                Instructions = "What is the customer's primary intent in `customer_message`?",
                Criteria = new Dictionary<string, string?>
                {
                    ["dispute_transaction"] = "The customer is contesting or questioning whether they authorized the transaction.",
                    [BlockCardIntent] = "The customer primarily wants their card blocked, frozen, disabled, or stopped.",
                    ["request_information"] = "The customer primarily wants information or clarification without requesting an account action.",
                    ["other"] = "The primary intent does not match the other categories."
                }
            },
            [Urgency] = new ScoreQuestion
            {
                Instructions = "How urgently does `customer_message` require operational attention?",
                Criteria =
                [
                    "No operational urgency is expressed.",
                    "Prompt attention is advisable, but immediate intervention is not requested.",
                    "Immediate intervention or account action is explicitly requested."
                ]
            }
        }
    };
}
