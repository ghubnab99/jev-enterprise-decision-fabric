using Anthropic;
using DecisionFabric.Anthropic;
using DecisionFabric.Core;
using DecisionFabric.TypeSafe;

namespace DecisionFabric.Evals;

/// <summary>Published list price per million tokens, used to cost a run.</summary>
internal sealed record ProviderPricing(double InputPerMillion, double OutputPerMillion)
{
    public double CostUsd(int inputTokens, int outputTokens) =>
        (inputTokens * InputPerMillion / 1_000_000) + (outputTokens * OutputPerMillion / 1_000_000);
}

internal sealed record ProviderSelection(
    IDecisionProvider Provider,
    string Label,
    string Model,
    ProviderPricing? Pricing,
    IDisposable? Lifetime);

internal static class Providers
{
    /// <summary>
    /// Claude list prices as of 2026-09. TypeSafe does not publish per-token prices,
    /// so a Jev run reports tokens and latency but no cost unless one is supplied.
    /// </summary>
    private static readonly Dictionary<string, ProviderPricing> ClaudePricing =
        new Dictionary<string, ProviderPricing>(StringComparer.Ordinal)
        {
            ["claude-opus-5"] = new(5.00, 25.00),
            ["claude-sonnet-5"] = new(2.00, 10.00),
            ["claude-haiku-4-5"] = new(1.00, 5.00),
            ["claude-fable-5-1"] = new(10.00, 50.00)
        };

    public const string Jev = "jev";
    public const string Claude = "claude";

    /// <summary>
    /// Finds a provider's key without requiring a global environment variable.
    /// Claude Code resolves its own credentials from ANTHROPIC_API_KEY ahead of a
    /// subscription login, so exporting that name machine-wide silently bills the
    /// editor to the API. The key file is checked first so this tool can hold a
    /// key the rest of the machine never sees.
    /// </summary>
    private static string ResolveApiKey(string provider, string environmentVariable, string? explicitPath)
    {
        var path = explicitPath ?? Path.Combine(".secrets", $"{provider}.key");
        if (File.Exists(path))
        {
            var fromFile = File.ReadAllText(path).Trim();
            if (fromFile.Length > 0)
            {
                return fromFile;
            }
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        throw new InvalidOperationException(
            $"No {provider} API key. Put one in '{path}' (gitignored), pass --api-key-file, " +
            $"or set {environmentVariable} for this process only.");
    }

    public static ProviderSelection Create(RunnerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Provider switch
        {
            Jev => CreateJev(options),
            Claude => CreateClaude(options),
            _ => throw new ArgumentException(
                $"Unknown provider '{options.Provider}'. Use '{Jev}' or '{Claude}'.")
        };
    }

    private static ProviderSelection CreateJev(RunnerOptions options)
    {
        var apiKey = ResolveApiKey("typesafe", "TYPESAFE_API_KEY", options.ApiKeyFile);

        var clientOptions = new TypeSafeClientOptions();
        if (options.Model is { } model)
        {
            clientOptions = clientOptions with { Model = model };
        }

        var httpClient = new HttpClient();
        return new ProviderSelection(
            new TypeSafeDecisionProvider(httpClient, apiKey, clientOptions),
            Jev,
            clientOptions.Model,
            options.Pricing,
            httpClient);
    }

    private static ProviderSelection CreateClaude(RunnerOptions options)
    {
        var apiKey = ResolveApiKey("anthropic", "ANTHROPIC_API_KEY", options.ApiKeyFile);

        var model = options.Model ?? "claude-opus-5";
        var clientOptions = ClaudeClientOptions.ForModel(model);
        if (options.Effort is { } effort)
        {
            clientOptions = clientOptions with { Effort = effort };
        }

        var client = new AnthropicClient { ApiKey = apiKey };
        ClaudePricing.TryGetValue(model, out var listPrice);

        return new ProviderSelection(
            new ClaudeDecisionProvider(client, clientOptions),
            clientOptions.Effort is null ? Claude : $"{Claude}/{clientOptions.Effort}",
            model,
            options.Pricing ?? listPrice,
            null);
    }
}
