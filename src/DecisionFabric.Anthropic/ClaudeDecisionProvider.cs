using System.Diagnostics;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using DecisionFabric.Core;

namespace DecisionFabric.Anthropic;

/// <summary>
/// Answers a decision contract with one structured-output Claude call. This is the
/// baseline a typed decision API is measured against: the same questions, the same
/// criteria, one request per decision.
/// </summary>
public sealed class ClaudeDecisionProvider : IDecisionProvider
{
    private readonly AnthropicClient _client;
    private readonly ClaudeClientOptions _options;

    public ClaudeDecisionProvider(AnthropicClient client, ClaudeClientOptions? options = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? new ClaudeClientOptions();
    }

    public async Task<DecisionEvaluationResponse> EvaluateAsync(
        DecisionEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ThinkingConfigParam? thinking = null;
        if (_options.UseAdaptiveThinking)
        {
            thinking = new ThinkingConfigAdaptive();
        }

        var parameters = new MessageCreateParams
        {
            Thinking = thinking,
            Model = request.Model ?? _options.Model,
            MaxTokens = _options.MaxTokens,
            System = DecisionContractPrompt.SystemPrompt,
            Messages =
            [
                new()
                {
                    Role = Role.User,
                    Content = DecisionContractPrompt.RenderUserPrompt(request.Contract, request.State)
                }
            ],
            OutputConfig = BuildOutputConfig(request.Contract)
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.Messages.Create(parameters, cancellationToken: cancellationToken);
        stopwatch.Stop();

        if (response.StopReason == "refusal")
        {
            throw new InvalidOperationException(
                $"Claude declined the decision: {response.StopDetails?.Explanation ?? "no explanation given"}.");
        }

        return new DecisionEvaluationResponse
        {
            // Raw() is the model id itself; ToString() renders the enum as JSON, quotes included.
            Model = response.Model.Raw() ?? _options.Model,
            Answers = ParseAnswers(request.Contract, ReadJson(response)),
            Usage = new DecisionUsage((int)response.Usage.InputTokens, (int)response.Usage.OutputTokens),
            Duration = stopwatch.Elapsed
        };
    }

    private OutputConfig BuildOutputConfig(DecisionContract contract)
    {
        var format = new JsonOutputFormat { Schema = DecisionContractPrompt.BuildSchema(contract) };

        return _options.Effort is { } effort
            ? new OutputConfig { Format = format, Effort = MapEffort(effort) }
            : new OutputConfig { Format = format };
    }

    private static Effort MapEffort(string effort) => effort switch
    {
        "low" => Effort.Low,
        "medium" => Effort.Medium,
        "high" => Effort.High,
        "max" => Effort.Max,
        _ => throw new ArgumentOutOfRangeException(nameof(effort), $"Unsupported effort '{effort}'.")
    };

    private static JsonElement ReadJson(Message response)
    {
        var text = string.Concat(
            response.Content.Select(block => block.Value).OfType<TextBlock>().Select(block => block.Text));
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new JsonException(
                $"Claude returned no text content (stop reason '{response.StopReason}').");
        }

        return JsonDocument.Parse(text).RootElement;
    }

    private static Dictionary<string, DecisionAnswer> ParseAnswers(
        DecisionContract contract,
        JsonElement payload)
    {
        var answers = new Dictionary<string, DecisionAnswer>(StringComparer.Ordinal);
        foreach (var (questionId, question) in contract.Questions)
        {
            if (!payload.TryGetProperty(questionId, out var element))
            {
                throw new JsonException($"Claude omitted an answer for '{questionId}'.");
            }

            answers[questionId] = question switch
            {
                NoulQuestion => new NoulAnswer { Noul = Unit(element, "noul", questionId) },
                ChoiceQuestion => new ChoiceAnswer
                {
                    Choice = element.GetProperty("choice").GetString()
                        ?? throw new JsonException($"Claude returned a null choice for '{questionId}'."),
                    // The baseline reports one confidence rather than a distribution;
                    // recording it as the chosen option's mass keeps the shape comparable.
                    Probabilities = new Dictionary<string, double>
                    {
                        [element.GetProperty("choice").GetString()!] =
                            Unit(element, "confidence", questionId)
                    },
                    Confidence = Unit(element, "confidence", questionId)
                },
                ScoreQuestion score => new ScoreAnswer
                {
                    Score = Bounded(element, "score", questionId, score.Criteria.Count - 1),
                    Legend = new Dictionary<string, string>(),
                    Probabilities = new Dictionary<string, double>(),
                    Confidence = Unit(element, "confidence", questionId)
                },
                _ => throw new NotSupportedException(
                    $"Unsupported question type '{question.GetType().Name}'.")
            };
        }

        return answers;
    }

    private static double Unit(JsonElement element, string property, string questionId) =>
        Bounded(element, property, questionId, 1);

    /// <summary>
    /// Reads a number the schema could not constrain. Structured outputs rejects
    /// minimum and maximum on numeric properties, so the bound is checked here
    /// rather than trusted: an out-of-range answer would otherwise reach the gate
    /// and be rejected there as a policy error rather than a malformed answer.
    /// </summary>
    private static double Bounded(JsonElement element, string property, string questionId, double maximum)
    {
        if (!element.TryGetProperty(property, out var value) ||
            !value.TryGetDouble(out var number))
        {
            throw new JsonException($"Claude returned no numeric '{property}' for '{questionId}'.");
        }

        if (double.IsNaN(number) || number < 0 || number > maximum)
        {
            throw new JsonException(
                $"Claude returned {property} {number} for '{questionId}', outside 0 to {maximum}.");
        }

        return number;
    }
}
