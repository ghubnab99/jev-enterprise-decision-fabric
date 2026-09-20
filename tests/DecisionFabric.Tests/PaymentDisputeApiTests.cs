using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DecisionFabric.PaymentDisputes.Api;
using DecisionFabric.Policy;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DecisionFabric.Tests;

[Collection(SampleApps.Name)]
public sealed class PaymentDisputeApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly HttpClient _client;

    public PaymentDisputeApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ClearBlockRequestCrossesPolicyAuthorizationBoundary()
    {
        var decision = await TriageAsync(
            "Freeze this card immediately. I do not recognize the transaction.");

        Assert.Equal(DestructiveActionDisposition.Authorized, decision.PolicyDisposition);
        Assert.Equal(DecisionAuthorizationState.AuthorizedByPolicy, decision.AuthorizationState);
        Assert.Equal(PaymentDisputeNextAction.BlockCard, decision.NextAction);
        Assert.Empty(decision.RiskSignals);
    }

    [Fact]
    public async Task DoubleNegationRequiresConfirmationBeforeAction()
    {
        var decision = await TriageAsync("I don't want you not to block my card.");

        Assert.Equal(DestructiveActionDisposition.RequireConfirmation, decision.PolicyDisposition);
        Assert.Equal(DecisionAuthorizationState.AwaitingConfirmation, decision.AuthorizationState);
        Assert.Equal(PaymentDisputeNextAction.RequestCustomerConfirmation, decision.NextAction);
        Assert.Contains("MultipleNegations", decision.RiskSignals);

        using var response = await _client.PostAsJsonAsync(
            $"/api/payment-disputes/decisions/{decision.DecisionId}/confirm",
            new ConfirmPaymentDisputeRequest
            {
                ConfirmationReference = "trusted-demo-reference"
            });
        response.EnsureSuccessStatusCode();
        var confirmed = await response.Content.ReadFromJsonAsync<PaymentDisputeDecisionResponse>(
            JsonOptions);

        Assert.NotNull(confirmed);
        Assert.Equal(
            DecisionAuthorizationState.AuthorizedByConfirmation,
            confirmed.AuthorizationState);
        Assert.Equal(PaymentDisputeNextAction.BlockCard, confirmed.NextAction);
        Assert.NotNull(confirmed.ConfirmedAt);
    }

    [Fact]
    public async Task ConfirmationIsIdempotent()
    {
        var decision = await TriageAsync(
            "Keep the card active—actually, maybe freeze it. I'm not sure.");
        var request = new ConfirmPaymentDisputeRequest
        {
            ConfirmationReference = "trusted-demo-reference"
        };

        using var first = await _client.PostAsJsonAsync(
            $"/api/payment-disputes/decisions/{decision.DecisionId}/confirm",
            request);
        using var second = await _client.PostAsJsonAsync(
            $"/api/payment-disputes/decisions/{decision.DecisionId}/confirm",
            request);

        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();
        var firstDecision = await first.Content.ReadFromJsonAsync<PaymentDisputeDecisionResponse>(
            JsonOptions);
        var secondDecision = await second.Content.ReadFromJsonAsync<PaymentDisputeDecisionResponse>(
            JsonOptions);
        Assert.NotNull(firstDecision);
        Assert.NotNull(secondDecision);
        Assert.Equal(firstDecision.DecisionId, secondDecision.DecisionId);
        Assert.Equal(firstDecision.AuthorizationState, secondDecision.AuthorizationState);
        Assert.Equal(firstDecision.NextAction, secondDecision.NextAction);
        Assert.Equal(firstDecision.ConfirmedAt, secondDecision.ConfirmedAt);
    }

    [Fact]
    public async Task NegativeDecisionCannotCrossConfirmationBoundary()
    {
        var decision = await TriageAsync(
            "I didn't make this payment. Don't block the card yet.");

        using var response = await _client.PostAsJsonAsync(
            $"/api/payment-disputes/decisions/{decision.DecisionId}/confirm",
            new ConfirmPaymentDisputeRequest
            {
                ConfirmationReference = "trusted-demo-reference"
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ApiDoesNotReturnRawCustomerMessage()
    {
        const string message = "I don't want you not to block my card.";
        using var response = await _client.PostAsJsonAsync(
            "/api/payment-disputes/triage",
            new TriagePaymentDisputeRequest { CustomerMessage = message });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(message, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenApiDocumentIsAvailable()
    {
        using var response = await _client.GetAsync("/openapi/v1.json");

        response.EnsureSuccessStatusCode();
    }

    private async Task<PaymentDisputeDecisionResponse> TriageAsync(string customerMessage)
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/payment-disputes/triage",
            new TriagePaymentDisputeRequest { CustomerMessage = customerMessage });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PaymentDisputeDecisionResponse>(JsonOptions)
            ?? throw new JsonException("Payment dispute response was empty.");
    }
}
