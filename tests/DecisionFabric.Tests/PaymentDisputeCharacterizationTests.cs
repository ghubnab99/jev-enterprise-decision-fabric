using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DecisionFabric.PaymentDisputes.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DecisionFabric.Tests;

/// <summary>
/// Freezes the externally observable payment-dispute API responses so refactoring
/// cannot silently change them. Volatile fields (identifiers, timestamps and
/// measured durations) are removed before comparison.
/// Set UPDATE_SNAPSHOTS=1 to rewrite the snapshot files deliberately.
/// </summary>
[Collection(SampleApps.Name)]
public sealed class PaymentDisputeCharacterizationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string[] VolatileProperties =
        ["decisionId", "createdAt", "confirmedAt", "durationMilliseconds"];

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly HttpClient _client;

    public PaymentDisputeCharacterizationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("clear-freeze", "Freeze this card immediately. I do not recognize the transaction.")]
    [InlineData("clear-disable", "Disable my card now and investigate this payment.")]
    [InlineData("explicit-negative", "I didn't make this payment. Don't block the card yet.")]
    [InlineData("contradictory", "Keep the card active—actually, maybe freeze it. I'm not sure.")]
    [InlineData("double-negation", "I don't want you not to block my card.")]
    [InlineData("unrecorded-message", "Can you tell me what ABC Digital is?")]
    public async Task TriageResponseMatchesSnapshot(string snapshotName, string customerMessage)
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/payment-disputes/triage",
            new TriagePaymentDisputeRequest { CustomerMessage = customerMessage });
        response.EnsureSuccessStatusCode();

        AssertMatchesSnapshot($"triage-{snapshotName}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ConfirmedResponseMatchesSnapshot()
    {
        using var triage = await _client.PostAsJsonAsync(
            "/api/payment-disputes/triage",
            new TriagePaymentDisputeRequest
            {
                CustomerMessage = "I don't want you not to block my card."
            });
        triage.EnsureSuccessStatusCode();
        var decisionId = JsonNode.Parse(await triage.Content.ReadAsStringAsync())!["decisionId"]!
            .GetValue<string>();

        using var confirm = await _client.PostAsJsonAsync(
            $"/api/payment-disputes/decisions/{decisionId}/confirm",
            new ConfirmPaymentDisputeRequest { ConfirmationReference = "trusted-demo-reference" });
        confirm.EnsureSuccessStatusCode();
        var body = await confirm.Content.ReadAsStringAsync();

        Assert.NotNull(JsonNode.Parse(body)!["confirmedAt"]);
        AssertMatchesSnapshot("confirm-double-negation", body);
    }

    private static void AssertMatchesSnapshot(string name, string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        foreach (var property in VolatileProperties)
        {
            node.Remove(property);
        }

        var actual = node.ToJsonString(SnapshotJsonOptions).ReplaceLineEndings("\n") + "\n";
        var path = Path.Combine(SnapshotDirectory(), $"{name}.json");

        if (Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return;
        }

        Assert.True(File.Exists(path), $"Snapshot '{path}' is missing. Run with UPDATE_SNAPSHOTS=1.");
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual);
    }

    private static string SnapshotDirectory([CallerFilePath] string sourceFile = "") =>
        Path.Combine(Path.GetDirectoryName(sourceFile)!, "Snapshots", "PaymentDisputes");
}
