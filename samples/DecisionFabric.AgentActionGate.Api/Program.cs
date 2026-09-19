using System.Text.Json;
using System.Text.Json.Serialization;
using DecisionFabric.Core;
using DecisionFabric.Hosting;
using DecisionFabric.Policy;
using Microsoft.Extensions.Options;

namespace DecisionFabric.AgentActionGate.Api;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddProblemDetails();
        builder.Services.AddOpenApi();
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });
        builder.Services.AddDecisionFabric(builder.Configuration);
        builder.Services.AddSingleton(AgentActionFixtures.Create());
        builder.Services.AddOptions<AgentActionPolicyOptions>()
            .BindConfiguration(AgentActionPolicyOptions.SectionName)
            .ValidateOnStart();
        builder.Services.AddSingleton<
            IValidateOptions<AgentActionPolicyOptions>,
            AgentActionPolicyOptionsValidator>();
        builder.Services.AddSingleton<AgentActionPack>();

        var app = builder.Build();
        app.UseExceptionHandler();
        app.MapOpenApi();

        app.MapGet("/", () => Results.Ok(new
        {
            name = "Jev Enterprise Decision Fabric — Agent Action Gate",
            provider = app.Configuration["DecisionFabric:Provider"] ?? "Fixture",
            openApi = "/openapi/v1.json",
            evaluate = "/api/agent-actions/evaluate"
        }));

        app.MapPost("/api/agent-actions/evaluate", EvaluateAsync)
            .WithTags("Agent actions")
            .WithName("EvaluateAgentAction")
            .WithSummary("Decide whether an agent's proposed tool call may run, needs approval or is denied.")
            .Produces<AgentActionDecisionResponse>()
            .ProducesValidationProblem();

        app.Run();
    }

    private static async Task<IResult> EvaluateAsync(
        EvaluateAgentActionRequest request,
        IDecisionFabric fabric,
        AgentActionPack pack,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        Require(errors, nameof(request.UserInstruction), request.UserInstruction, 4_000);
        Require(errors, nameof(request.ToolName), request.ToolName, 200);
        Require(errors, nameof(request.ToolArguments), request.ToolArguments, 8_000);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await fabric.EvaluateAsync(
            pack,
            new AgentActionInput(request.UserInstruction, request.ToolName, request.ToolArguments),
            cancellationToken);
        var impact = result.Evidence.Choice(AgentActionContract.ActionImpact);
        var scope = result.Evidence.Score(AgentActionContract.ScopeExpansion);

        return Results.Ok(new AgentActionDecisionResponse
        {
            DecisionId = $"act_{Guid.NewGuid():N}",
            Disposition = result.Outcome.Disposition,
            Evidence = new AgentActionEvidence
            {
                ActionRequestedByUser = result.Evidence.Noul(AgentActionContract.ActionRequestedByUser).Noul,
                ActionImpact = impact.Choice,
                ActionImpactConfidence = impact.Confidence,
                ScopeExpansion = scope.Score,
                ScopeExpansionConfidence = scope.Confidence
            },
            RiskSignals = Enum.GetValues<LinguisticRiskSignal>()
                .Where(signal => signal != LinguisticRiskSignal.None &&
                    result.Outcome.RiskSignals.HasFlag(signal))
                .Select(signal => signal.ToString())
                .ToArray(),
            PolicyReasons = result.Outcome.Reasons,
            Model = result.Model,
            ContractId = result.ContractId,
            ContractVersion = result.ContractVersion,
            PolicyVersion = result.PolicyVersion,
            DurationMilliseconds = result.Duration.TotalMilliseconds,
            InputTokens = result.Usage.InputTokens,
            OutputTokens = result.Usage.OutputTokens
        });
    }

    private static void Require(
        Dictionary<string, string[]> errors,
        string name,
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[name] = [$"{name} is required."];
        }
        else if (value.Length > maximumLength)
        {
            errors[name] = [$"{name} cannot exceed {maximumLength:N0} characters."];
        }
    }
}
