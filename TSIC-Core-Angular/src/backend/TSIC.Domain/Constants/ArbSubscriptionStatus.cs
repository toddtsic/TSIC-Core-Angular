namespace TSIC.Domain.Constants;

/// <summary>
/// The one rule for "can this Authorize.Net ARB subscription still draft the card".
///
/// Two callers, and they must never diverge: the write-side double-charge intercept
/// (PaymentService.PartitionArbEnrolled) and the read-side nav gate that decides whether a
/// registrant is offered "Pay Balance Due" (JobRepository.GetPulseUserContextAsync). A plan
/// that reads live on one side and dead on the other either strands a family who owes money
/// or invites a payment on top of an automatic draft.
///
/// Liveness is anchored on the SUBSCRIPTION ID, not the status: a registrant with no plan at
/// all carries a null status, and a status-only rule would read them as live and suppress the
/// nudge for every ordinary family who owes a balance.
/// </summary>
public static class ArbSubscriptionStatus
{
    /// <summary>
    /// Statuses that can no longer bill. Anything else — "active", "suspended", or a null
    /// status alongside a real subscription id — is live: a suspended subscription resumes on
    /// its own once the card clears, and an unrecognized status fails to the safe side.
    /// Note "expired" is what ADN writes when a plan FINISHES normally, so a dead status is
    /// not by itself a sign of trouble; pair it with a balance owed before nudging anyone.
    /// </summary>
    private static readonly string[] Dead = ["canceled", "terminated", "expired"];

    /// <summary>
    /// True when this subscription id exists and its status can still draft the card.
    /// </summary>
    public static bool IsLive(string? subscriptionId, string? status) =>
        !string.IsNullOrWhiteSpace(subscriptionId)
        && !Dead.Contains(status ?? string.Empty, StringComparer.OrdinalIgnoreCase);
}
