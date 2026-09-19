using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DecisionFabric.Core;

namespace DecisionFabric.TypeSafe;

public sealed class TypeSafeDecisionProvider : IDecisionProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly TypeSafeClientOptions _options;

    public TypeSafeDecisionProvider(
        HttpClient httpClient,
        string apiKey,
        TypeSafeClientOptions? options = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? throw new ArgumentException("A TypeSafe API key is required.", nameof(apiKey))
            : apiKey;
        _options = options ?? new TypeSafeClientOptions();

        if (_options.MaxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxRetries cannot be negative.");
        }
    }

    public async Task<DecisionEvaluationResponse> EvaluateAsync(
        DecisionEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new SystemOneRequest
        {
            State = request.State,
            Model = request.Model ?? _options.Model,
            Questions = request.Contract.Questions
        };

        var stopwatch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);

        for (var attempt = 0; ; attempt++)
        {
            using var message = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(_options.BaseAddress, "v1/systemone"));
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            message.Content = JsonContent.Create(payload, options: JsonOptions);

            using var response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            if (IsRetryable(response.StatusCode) && attempt < _options.MaxRetries)
            {
                await Task.Delay(GetRetryDelay(response, attempt), timeout.Token);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(timeout.Token);
                throw new TypeSafeApiException(response.StatusCode, body);
            }

            var apiResponse = await response.Content.ReadFromJsonAsync<SystemOneResponse>(
                JsonOptions,
                timeout.Token) ?? throw new JsonException("TypeSafe returned an empty response.");

            stopwatch.Stop();
            return new DecisionEvaluationResponse
            {
                Model = apiResponse.Model,
                Answers = apiResponse.Answers.ToDictionary(
                    pair => pair.Key,
                    pair => TypeSafeAnswerParser.Parse(pair.Value)),
                Usage = new DecisionUsage(
                    apiResponse.Usage.InputTokens,
                    apiResponse.Usage.OutputTokens),
                Duration = stopwatch.Elapsed
            };
        }
    }

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || (int)statusCode == 529;

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var serverDelay = date - DateTimeOffset.UtcNow;
            if (serverDelay > TimeSpan.Zero)
            {
                return serverDelay;
            }
        }

        var exponentialMilliseconds = 250 * Math.Pow(2, attempt);
        return TimeSpan.FromMilliseconds(exponentialMilliseconds + Random.Shared.Next(25, 126));
    }

    private sealed record SystemOneRequest
    {
        public required JsonElement State { get; init; }
        public required string Model { get; init; }
        public required IReadOnlyDictionary<string, DecisionQuestion> Questions { get; init; }
    }

    private sealed record SystemOneResponse
    {
        public required string Model { get; init; }
        public required Dictionary<string, JsonElement> Answers { get; init; }
        public required UsageResponse Usage { get; init; }
    }

    private sealed record UsageResponse
    {
        [JsonPropertyName("input_tokens")]
        public required int InputTokens { get; init; }

        [JsonPropertyName("output_tokens")]
        public required int OutputTokens { get; init; }
    }
}
