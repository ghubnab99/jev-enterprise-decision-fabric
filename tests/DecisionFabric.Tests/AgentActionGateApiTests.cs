using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DecisionFabric.AgentActionGate.Api;
using DecisionFabric.Policy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using AgentProgram = DecisionFabric.AgentActionGate.Api.Program;

namespace DecisionFabric.Tests;

public sealed class AgentActionGateApiTests : IClassFixture<WebApplicationFactory<AgentProgram>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly WebApplicationFactory<AgentProgram> _factory;
    private readonly HttpClient _client;

    public AgentActionGateApiTests(WebApplicationFactory<AgentProgram> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("Summarize the open invoices for ACME.", "crm.search_invoices", ProposedActionDisposition.Allow)]
    [InlineData("Rename the Q3 folder to Q3-final.", "drive.rename_folder", ProposedActionDisposition.Allow)]
    [InlineData("Clean up the Q3 folder.", "drive.delete_folder", ProposedActionDisposition.RequireApproval)]
    [InlineData("Summarize the open invoices for ACME.", "email.send", ProposedActionDisposition.Deny)]
    [InlineData("Do whatever you think is best.", "billing.refund", ProposedActionDisposition.RequireApproval)]
    public async Task RoutesProposedToolCallsThroughPolicy(
        string instruction,
        string tool,
        ProposedActionDisposition expected)
    {
        var decision = await EvaluateAsync(instruction, tool);

        Assert.Equal(expected, decision.Disposition);
        Assert.NotEmpty(decision.PolicyReasons);
        Assert.Equal("agent-action-gate", decision.ContractId);
        Assert.Equal("1.0.0", decision.ContractVersion);
        Assert.Equal("fixture/synthetic", decision.Model);
        Assert.StartsWith("agent-action-gate/sha256:", decision.PolicyVersion, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IrreversibleActionRequiresApprovalEvenWhenRequested()
    {
        var decision = await EvaluateAsync("Clean up the Q3 folder.", "drive.delete_folder");

        Assert.Contains(
            decision.PolicyReasons,
            reason => reason.Contains("irreversible_change", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResponseDoesNotEchoInstructionOrArguments()
    {
        const string instruction = "Rename the Q3 folder to Q3-final.";
        const string arguments = """{"folder":"Q3","newName":"Q3-final-secret-marker"}""";
        using var response = await _client.PostAsJsonAsync(
            "/api/agent-actions/evaluate",
            new EvaluateAgentActionRequest
            {
                UserInstruction = instruction,
                ToolName = "drive.rename_folder",
                ToolArguments = arguments
            });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(instruction, body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-marker", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingFieldsAreRejected()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/agent-actions/evaluate",
            new EvaluateAgentActionRequest { UserInstruction = " ", ToolName = "x", ToolArguments = "{}" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void InvalidPolicyConfigurationPreventsStartup()
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder.UseSetting(
            $"{AgentActionPolicyOptions.SectionName}:MaximumAutoApprovedScopeExpansion",
            "3"));

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.True(
            exception is OptionsValidationException ||
            exception.InnerException is OptionsValidationException ||
            (exception is AggregateException aggregate &&
                aggregate.Flatten().InnerExceptions.Any(inner => inner is OptionsValidationException)),
            exception.ToString());
    }

    private async Task<AgentActionDecisionResponse> EvaluateAsync(string instruction, string tool)
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/agent-actions/evaluate",
            new EvaluateAgentActionRequest
            {
                UserInstruction = instruction,
                ToolName = tool,
                ToolArguments = "{}"
            });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentActionDecisionResponse>(JsonOptions)
            ?? throw new JsonException("Agent action response was empty.");
    }
}
