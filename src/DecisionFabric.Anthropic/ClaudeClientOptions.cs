namespace DecisionFabric.Anthropic;

/// <summary>
/// How the Claude baseline is asked for a decision. Defaults describe a frontier
/// model; <see cref="ForModel"/> adjusts them for models with a different surface.
/// </summary>
public sealed record ClaudeClientOptions
{
    public string Model { get; init; } = "claude-opus-5";

    /// <summary>Adaptive thinking. Not available on Haiku 4.5, which takes a token budget instead.</summary>
    public bool UseAdaptiveThinking { get; init; } = true;

    /// <summary>One of low, medium, high, xhigh, max. Null omits the parameter.</summary>
    public string? Effort { get; init; } = "medium";

    public int MaxTokens { get; init; } = 4096;

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Returns options a given model actually accepts. Haiku 4.5 rejects the effort
    /// parameter and has no adaptive thinking, so it runs as a plain fast classifier.
    /// </summary>
    public static ClaudeClientOptions ForModel(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        return model.StartsWith("claude-haiku", StringComparison.Ordinal)
            ? new ClaudeClientOptions
            {
                Model = model,
                UseAdaptiveThinking = false,
                Effort = null,
                MaxTokens = 1024
            }
            : new ClaudeClientOptions { Model = model };
    }
}
