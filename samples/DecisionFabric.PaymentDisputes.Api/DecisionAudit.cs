using DecisionFabric.Policy;

namespace DecisionFabric.PaymentDisputes.Api;

internal sealed record DecisionAuditEvent(
    string EventType,
    string DecisionId,
    string InputSha256,
    string Model,
    string ContractVersion,
    DestructiveActionDisposition PolicyDisposition,
    DecisionAuthorizationState AuthorizationState,
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
            auditEvent.PolicyDisposition,
            auditEvent.AuthorizationState);
        return ValueTask.CompletedTask;
    }
}

internal static partial class DecisionAuditLog
{
    [LoggerMessage(
        EventId = 4100,
        Level = LogLevel.Information,
        Message = "Decision audit {EventType}: DecisionId={DecisionId}, InputSha256={InputSha256}, Model={Model}, ContractVersion={ContractVersion}, PolicyDisposition={PolicyDisposition}, AuthorizationState={AuthorizationState}")]
    public static partial void Recorded(
        ILogger logger,
        string eventType,
        string decisionId,
        string inputSha256,
        string model,
        string contractVersion,
        DestructiveActionDisposition policyDisposition,
        DecisionAuthorizationState authorizationState);
}
