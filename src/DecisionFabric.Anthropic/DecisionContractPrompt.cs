using System.Globalization;
using System.Text;
using System.Text.Json;
using DecisionFabric.Core;

namespace DecisionFabric.Anthropic;

/// <summary>
/// Renders a decision contract as a prompt and a matching JSON schema. The baseline
/// is given exactly the criteria the typed API receives, so any difference in the
/// results comes from how the decision is elicited rather than from what was asked.
/// </summary>
internal static class DecisionContractPrompt
{
    public const string SystemPrompt =
        "You answer independent questions about a state object for an automated decision system. " +
        "Answer each question only from the state and the question's own criteria. " +
        "Questions are independent: never let your answer to one question justify another. " +
        "Probabilities and confidences must be calibrated, not rounded to 0 or 1 for emphasis. " +
        "Respond only with the JSON object the schema describes.";

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    public static string RenderUserPrompt(DecisionContract contract, JsonElement state)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<state>");
        builder.AppendLine(JsonSerializer.Serialize(state, IndentedJson));
        builder.AppendLine("</state>");
        builder.AppendLine();
        builder.AppendLine("Answer every question below about that state.");

        foreach (var (questionId, question) in contract.Questions)
        {
            builder.AppendLine();
            switch (question)
            {
                case NoulQuestion noul:
                    builder.AppendLine(
                        CultureInfo.InvariantCulture,
                        $"## {questionId} (probability)");
                    builder.AppendLine(noul.Instructions);
                    if (noul.Criteria is { } criteria)
                    {
                        builder.AppendLine(CultureInfo.InvariantCulture, $"- True when: {criteria.True}");
                        builder.AppendLine(CultureInfo.InvariantCulture, $"- False when: {criteria.False}");
                    }

                    builder.AppendLine("Return `noul`: the probability from 0 to 1 that the answer is true.");
                    break;

                case ChoiceQuestion choice:
                    builder.AppendLine(CultureInfo.InvariantCulture, $"## {questionId} (choice)");
                    builder.AppendLine(choice.Instructions);
                    foreach (var (option, definition) in choice.Criteria)
                    {
                        builder.AppendLine(CultureInfo.InvariantCulture, $"- `{option}`: {definition}");
                    }

                    builder.AppendLine(
                        "Return `choice`: exactly one option id, and `confidence`: how sure you are, from 0 to 1.");
                    break;

                case ScoreQuestion score:
                    builder.AppendLine(
                        CultureInfo.InvariantCulture,
                        $"## {questionId} (score from 0 to {score.Criteria.Count - 1})");
                    builder.AppendLine(score.Instructions);
                    for (var index = 0; index < score.Criteria.Count; index++)
                    {
                        builder.AppendLine(CultureInfo.InvariantCulture, $"- {index}: {score.Criteria[index]}");
                    }

                    builder.AppendLine(
                        "Return `score`: a number on that scale, values between the anchors allowed, " +
                        "and `confidence`: how sure you are, from 0 to 1.");
                    break;

                default:
                    throw new NotSupportedException($"Unsupported question type '{question.GetType().Name}'.");
            }
        }

        return builder.ToString();
    }

    public static Dictionary<string, JsonElement> BuildSchema(DecisionContract contract)
    {
        var properties = new Dictionary<string, object>();
        foreach (var (questionId, question) in contract.Questions)
        {
            // Structured outputs rejects minimum/maximum on numbers, so the range
            // travels in the description and is enforced when the answer is parsed.
            properties[questionId] = question switch
            {
                NoulQuestion => Object(
                    new Dictionary<string, object>
                    {
                        ["noul"] = Number("The probability from 0 to 1 that the answer is true.")
                    },
                    "noul"),
                ChoiceQuestion choice => Object(
                    new Dictionary<string, object>
                    {
                        ["choice"] = new { type = "string", @enum = choice.Criteria.Keys.ToArray() },
                        ["confidence"] = Number("How sure you are of the choice, from 0 to 1.")
                    },
                    "choice",
                    "confidence"),
                ScoreQuestion score => Object(
                    new Dictionary<string, object>
                    {
                        ["score"] = Number(
                            $"A score from 0 to {score.Criteria.Count - 1}; values between anchors are allowed."),
                        ["confidence"] = Number("How sure you are of the score, from 0 to 1.")
                    },
                    "score",
                    "confidence"),
                _ => throw new NotSupportedException($"Unsupported question type '{question.GetType().Name}'.")
            };
        }

        var schema = Object(properties, [.. contract.Questions.Keys]);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            JsonSerializer.Serialize(schema))!;
    }

    private static object Number(string description) => new { type = "number", description };

    private static Dictionary<string, object> Object(
        Dictionary<string, object> properties,
        params string[] required) =>
        new()
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false
        };
}
