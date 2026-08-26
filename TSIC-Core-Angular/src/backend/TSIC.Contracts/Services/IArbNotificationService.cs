using TSIC.Contracts.Dtos.Arb;

namespace TSIC.Contracts.Services;

/// <summary>
/// Family-facing ARB notification that runs unattended, on a schedule.
///
/// Deliberately separate from <c>IArbDefensiveService</c>, which serves a director clicking Send on
/// the ARB Health screen. That path's text is an editable draft a human reviews before it goes; this
/// path's text is fixed and unattended. Sharing one source would let an edit to a director's draft
/// silently change what goes to thousands of families.
///
/// The failed-draft notice that used to live here is GONE (2026-08-26). The 4 AM sweep no longer
/// writes to the families behind failed drafts — it reports them in the digest and stops. A dunning
/// notice is a deliberate act a director takes from the ARB Health screen, not an overnight one.
/// The expiring-card notice below stays: it is a heads-up before a card fails, not a demand after.
/// </summary>
public interface IArbNotificationService
{
    /// <summary>
    /// Emails every family whose card expires this month, across all jobs holding a live subscription.
    /// Runs on the 2nd and the 15th: the 1st belongs exclusively to the month-end close, and sharing
    /// that morning would put a second unattended send behind the close's IIF gate.
    /// Sends its own summary to support -- it is a separate operation on a separate schedule, not a
    /// section of a digest that was already mailed hours earlier.
    /// </summary>
    Task<ArbNotifyResultDto> NotifyExpiringCardsAsync(CancellationToken ct = default);
}
