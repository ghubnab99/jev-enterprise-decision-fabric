using DecisionFabric.Policy;

namespace DecisionFabric.PaymentDisputes.Api;

public enum DecisionAuthorizationState
{
    NotAuthorized,
    AwaitingConfirmation,
    AuthorizedByPolicy,
    AuthorizedByConfirmation
}

public enum PaymentDisputeNextAction
{
    NoAction,
    RequestCustomerConfirmation,
    BlockCard
}

public sealed record TriagePaymentDisputeRequest
{
    public required string CustomerMessage { get; init; }
}

public sealed record ConfirmPaymentDisputeRequest
{
    public required string ConfirmationReference { get; init; }
}

public sealed record PaymentDisputeEvidence
{
    public required double BlockCardRequested { get; init; }
    public required string PrimaryIntent { get; init; }
    public required double PrimaryIntentConfidence { get; init; }
    public required double Urgency { get; init; }
    public required double UrgencyConfidence { get; init; }
}

public sealed record PaymentDisputeDecisionResponse
{
    public required string DecisionId { get; init; }
    public required DestructiveActionDisposition PolicyDisposition { get; init; }
    public required DecisionAuthorizationState AuthorizationState { get; init; }
    public required PaymentDisputeNextAction NextAction { get; init; }
    public required PaymentDisputeEvidence Evidence { get; init; }
    public required IReadOnlyList<string> RiskSignals { get; init; }
    public required IReadOnlyList<string> PolicyReasons { get; init; }
    public required string Model { get; init; }
    public required string ContractId { get; init; }
    public required string ContractVersion { get; init; }
    public required double DurationMilliseconds { get; init; }
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ConfirmedAt { get; init; }
}

internal sealed record StoredPaymentDisputeDecision(
    PaymentDisputeDecisionResponse Response,
    string InputSha256);

internal enum ConfirmationStatus
{
    Confirmed,
    AlreadyConfirmed,
    NotFound,
    NotAwaitingConfirmation
}

internal sealed record ConfirmationResult(
    ConfirmationStatus Status,
    PaymentDisputeDecisionResponse? Decision);
