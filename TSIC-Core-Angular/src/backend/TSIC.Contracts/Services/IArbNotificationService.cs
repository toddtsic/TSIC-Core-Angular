using TSIC.Contracts.Dtos.Arb;

namespace TSIC.Contracts.Services;

/// <summary>
/// Family-facing notification for ARB failures found by the daily sweep.
///
/// Deliberately separate from <c>IArbDefensiveService</c>, which serves a director clicking Send on
/// the ARB Health screen. That path's text is an editable draft a human reviews before it goes; this
/// path's text is fixed and unattended. Sharing one source would let an edit to a director's draft
/// silently change what goes to thousands of families at 4 AM.
/// </summary>
public interface IArbNotificationService
{
    /// <summary>
    /// Emails the families behind failed ARB drafts. Never throws: every per-registration failure is
    /// caught and returned as a skip, because the caller runs this AFTER the sweep's proven steps and
    /// its outcome must not be able to change the sweep's verdict.
    /// </summary>
    Task<ArbNotifyResultDto> NotifyFailedDraftsAsync(
        IReadOnlyList<ArbFailedDraftDto> failures, CancellationToken ct = default);

    /// <summary>
    /// Emails every family whose card expires this month, across all jobs holding a live subscription.
    /// Runs on the 2nd and the 15th: the 1st belongs exclusively to the month-end close, and sharing
    /// that morning would put a second unattended send behind the close's IIF gate.
    /// Sends its own summary to support -- it is a separate operation on a separate schedule, not a
    /// section of a digest that was already mailed hours earlier.
    /// </summary>
    Task<ArbNotifyResultDto> NotifyExpiringCardsAsync(CancellationToken ct = default);
}
