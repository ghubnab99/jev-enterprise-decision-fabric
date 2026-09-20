using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DecisionFabric.PaymentDisputes.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DecisionFabric.Tests;

[Collection(SampleApps.Name)]
public sealed class PaymentDisputeAuditTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task AuditRecordsPolicyVersionAndConfirmationReferenceWithoutCustomerText()
    {
        const string message = "I don't want you not to block my card.";
        var sink = new CapturingAuditSink();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IDecisionAuditSink>(sink)));
        using var client = factory.CreateClient();

        using var triage = await client.PostAsJsonAsync(
            "/api/payment-disputes/triage",
            new TriagePaymentDisputeRequest { CustomerMessage = message });
        triage.EnsureSuccessStatusCode();
        var decision = await triage.Content.ReadFromJsonAsync<PaymentDisputeDecisionResponse>(JsonOptions);
        Assert.NotNull(decision);

        using var confirm = await client.PostAsJsonAsync(
            $"/api/payment-disputes/decisions/{decision.DecisionId}/confirm",
            new ConfirmPaymentDisputeRequest { ConfirmationReference = "channel-ref-123" });
        confirm.EnsureSuccessStatusCode();

        var events = sink.Events.ToArray();
        Assert.Collection(
            events,
            evaluated =>
            {
                Assert.Equal("evaluated", evaluated.EventType);
                Assert.Null(evaluated.ConfirmationReference);
                Assert.Equal(decision.PolicyVersion, evaluated.PolicyVersion);
            },
            confirmed =>
            {
                Assert.Equal("confirmed", confirmed.EventType);
                Assert.Equal("channel-ref-123", confirmed.ConfirmationReference);
                Assert.Equal(decision.PolicyVersion, confirmed.PolicyVersion);
                Assert.Equal(DecisionAuthorizationState.AuthorizedByConfirmation, confirmed.AuthorizationState);
            });
        Assert.All(events, auditEvent =>
            Assert.DoesNotContain(message, auditEvent.ToString(), StringComparison.Ordinal));
        Assert.StartsWith("payment-dispute-gate/sha256:", decision.PolicyVersion, StringComparison.Ordinal);
    }

    private sealed class CapturingAuditSink : IDecisionAuditSink
    {
        public ConcurrentQueue<DecisionAuditEvent> Events { get; } = new();

        public ValueTask WriteAsync(
            DecisionAuditEvent auditEvent,
            CancellationToken cancellationToken = default)
        {
            Events.Enqueue(auditEvent);
            return ValueTask.CompletedTask;
        }
    }
}
