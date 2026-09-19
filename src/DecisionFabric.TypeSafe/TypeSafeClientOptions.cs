namespace DecisionFabric.TypeSafe;

public sealed record TypeSafeClientOptions
{
    public Uri BaseAddress { get; init; } = new("https://api.typesafe.ai/");
    public string Model { get; init; } = "jev-1.13.0";
    public int MaxRetries { get; init; } = 3;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
