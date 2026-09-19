using System.Text.Json;
using System.Text.Json.Serialization;
using DecisionFabric.Core;
using DecisionFabric.TypeSafe;

namespace DecisionFabric.PaymentDisputes.Api;

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
        builder.Services.AddSingleton<PaymentDisputeDecisionStore>();
        builder.Services.AddSingleton<IDecisionAuditSink, LoggingDecisionAuditSink>();
        builder.Services.AddSingleton<PaymentDisputeDecisionService>();
        AddDecisionProvider(builder);

        var app = builder.Build();
        app.UseExceptionHandler();
        app.MapOpenApi();

        app.MapGet("/", () => Results.Ok(new
        {
            name = "Jev Enterprise Decision Fabric — Payment Disputes",
            provider = app.Configuration["DecisionFabric:Provider"] ?? "Fixture",
            openApi = "/openapi/v1.json",
            triage = "/api/payment-disputes/triage"
        }));
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "healthy",
            provider = app.Configuration["DecisionFabric:Provider"] ?? "Fixture"
        }));

        var disputes = app.MapGroup("/api/payment-disputes")
            .WithTags("Payment disputes");
        disputes.MapPost("/triage", TriageAsync)
            .WithName("TriagePaymentDispute")
            .WithSummary("Evaluate payment-dispute intent and apply deterministic action policy.")
            .Produces<PaymentDisputeDecisionResponse>()
            .ProducesValidationProblem();
        disputes.MapGet("/decisions/{decisionId}", GetDecision)
            .WithName("GetPaymentDisputeDecision")
            .WithSummary("Retrieve an in-memory decision without the original customer message.")
            .Produces<PaymentDisputeDecisionResponse>()
            .Produces(StatusCodes.Status404NotFound);
        disputes.MapPost("/decisions/{decisionId}/confirm", ConfirmAsync)
            .WithName("ConfirmPaymentDisputeDecision")
            .WithSummary("Record trusted external confirmation for a decision awaiting confirmation.")
            .Produces<PaymentDisputeDecisionResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.Run();
    }

    private static async Task<IResult> TriageAsync(
        TriagePaymentDisputeRequest request,
        PaymentDisputeDecisionService service,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerMessage))
        {
            return ValidationError(
                nameof(request.CustomerMessage),
                "Customer message is required.");
        }

        if (request.CustomerMessage.Length > 4_000)
        {
            return ValidationError(
                nameof(request.CustomerMessage),
                "Customer message cannot exceed 4,000 characters.");
        }

        return Results.Ok(await service.TriageAsync(request.CustomerMessage, cancellationToken));
    }

    private static IResult GetDecision(
        string decisionId,
        PaymentDisputeDecisionService service)
    {
        var decision = service.Get(decisionId);
        return decision is null ? Results.NotFound() : Results.Ok(decision);
    }

    private static async Task<IResult> ConfirmAsync(
        string decisionId,
        ConfirmPaymentDisputeRequest request,
        PaymentDisputeDecisionService service,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConfirmationReference))
        {
            return ValidationError(
                nameof(request.ConfirmationReference),
                "A trusted external confirmation reference is required.");
        }

        if (request.ConfirmationReference.Length > 200)
        {
            return ValidationError(
                nameof(request.ConfirmationReference),
                "Confirmation reference cannot exceed 200 characters.");
        }

        var result = await service.ConfirmAsync(decisionId, cancellationToken);
        return result.Status switch
        {
            ConfirmationStatus.Confirmed or ConfirmationStatus.AlreadyConfirmed =>
                Results.Ok(result.Decision),
            ConfirmationStatus.NotFound => Results.NotFound(),
            ConfirmationStatus.NotAwaitingConfirmation => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Decision is not awaiting confirmation",
                detail: "Only a decision routed to confirmation can cross the confirmation boundary."),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }

    private static IResult ValidationError(string propertyName, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [propertyName] = [message]
        });

    private static void AddDecisionProvider(WebApplicationBuilder builder)
    {
        var mode = builder.Configuration["DecisionFabric:Provider"] ?? "Fixture";
        if (string.Equals(mode, "Fixture", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<IDecisionProvider, FixtureDecisionProvider>();
            return;
        }

        if (!string.Equals(mode, "TypeSafe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "DecisionFabric:Provider must be either 'Fixture' or 'TypeSafe'.");
        }

        var apiKey = builder.Configuration["DecisionFabric:TypeSafeApiKey"] ??
            Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "TypeSafe provider mode requires DecisionFabric:TypeSafeApiKey or TYPESAFE_API_KEY.");
        }

        builder.Services.AddHttpClient("typesafe");
        builder.Services.AddSingleton<IDecisionProvider>(services =>
            new TypeSafeDecisionProvider(
                services.GetRequiredService<IHttpClientFactory>().CreateClient("typesafe"),
                apiKey,
                new TypeSafeClientOptions
                {
                    Model = builder.Configuration["DecisionFabric:Model"] ?? "jev-1.13.0"
                }));
    }
}
