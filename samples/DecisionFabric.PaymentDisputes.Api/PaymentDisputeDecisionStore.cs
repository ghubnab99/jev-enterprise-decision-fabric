namespace DecisionFabric.PaymentDisputes.Api;

internal sealed class PaymentDisputeDecisionStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, StoredPaymentDisputeDecision> _decisions =
        new(StringComparer.Ordinal);

    public void Add(StoredPaymentDisputeDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        lock (_gate)
        {
            _decisions.Add(decision.Response.DecisionId, decision);
        }
    }

    public StoredPaymentDisputeDecision? Get(string decisionId)
    {
        lock (_gate)
        {
            return _decisions.GetValueOrDefault(decisionId);
        }
    }

    public ConfirmationResult Confirm(string decisionId, DateTimeOffset confirmedAt)
    {
        lock (_gate)
        {
            if (!_decisions.TryGetValue(decisionId, out var stored))
            {
                return new ConfirmationResult(ConfirmationStatus.NotFound, null);
            }

            if (stored.Response.AuthorizationState == DecisionAuthorizationState.AuthorizedByConfirmation)
            {
                return new ConfirmationResult(ConfirmationStatus.AlreadyConfirmed, stored.Response);
            }

            if (stored.Response.AuthorizationState != DecisionAuthorizationState.AwaitingConfirmation)
            {
                return new ConfirmationResult(ConfirmationStatus.NotAwaitingConfirmation, stored.Response);
            }

            var confirmed = stored with
            {
                Response = stored.Response with
                {
                    AuthorizationState = DecisionAuthorizationState.AuthorizedByConfirmation,
                    NextAction = PaymentDisputeNextAction.BlockCard,
                    ConfirmedAt = confirmedAt
                }
            };
            _decisions[decisionId] = confirmed;
            return new ConfirmationResult(ConfirmationStatus.Confirmed, confirmed.Response);
        }
    }
}
