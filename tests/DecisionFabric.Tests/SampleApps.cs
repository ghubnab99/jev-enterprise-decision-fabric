namespace DecisionFabric.Tests;

/// <summary>
/// Test classes that start a sample app with <c>WebApplicationFactory</c> belong here.
/// The factory reaches the app's host by running its entry point and watching a process-wide
/// diagnostic listener, so two factories starting at the same moment can capture each other's
/// host and a test then fails on an <c>ObjectDisposedException</c> for a host it never started.
/// xUnit runs the classes of one collection in sequence, which keeps the starts apart.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SampleApps
{
    public const string Name = "sample apps";
}
