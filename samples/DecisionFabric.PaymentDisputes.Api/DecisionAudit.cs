using DecisionFabric.Policy;

namespace DecisionFabric.PaymentDisputes.Api;

internal sealed record DecisionAuditEvent(
    string EventType,
    string DecisionId,
    string InputSha256,
    string Model,
    string ContractVersion,
    string PolicyVersion,
    DestructiveActionDisposition PolicyDisposition,
    DecisionAuthorizationState AuthorizationState,
    string? ConfirmationReference,
    DateTimeOffset OccurredAt);

internal interface IDecisionAuditSink
{
    ValueTask WriteAsync(
        DecisionAuditEvent auditEvent,
        CancellationToken cancellationToken = default);
}

internal sealed class LoggingDecisionAuditSink(ILogger<LoggingDecisionAuditSink> logger)
    : IDecisionAuditSink
{
    public ValueTask WriteAsync(
        DecisionAuditEvent auditEvent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DecisionAuditLog.Recorded(
            logger,
            auditEvent.EventType,
            auditEvent.DecisionId,
            auditEvent.InputSha256,
            auditEvent.Model,
            auditEvent.ContractVersion,
            auditEvent.PolicyVersion,
            auditEvent.PolicyDisposition,
            auditEvent.AuthorizationState,
            auditEvent.ConfirmationReference);
        return ValueTask.CompletedTask;
    }
}

internal static partial class DecisionAuditLog
{
    [LoggerMessage(
        EventId = 4100,
        Level = LogLevel.Information,
        Message = "Decision audit {EventType}: DecisionId={DecisionId}, InputSha256={InputSha256}, Model={Model}, ContractVersion={ContractVersion}, PolicyVersion={PolicyVersion}, PolicyDisposition={PolicyDisposition}, AuthorizationState={AuthorizationState}, ConfirmationReference={ConfirmationReference}")]
    public static partial void Recorded(
        ILogger logger,
        string eventType,
        string decisionId,
        string inputSha256,
        string model,
        string contractVersion,
        string policyVersion,
        DestructiveActionDisposition policyDisposition,
        DecisionAuthorizationState authorizationState,
        string? confirmationReference);
}
