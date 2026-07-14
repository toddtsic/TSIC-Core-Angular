using Microsoft.Extensions.DependencyInjection;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Shared.Email;

/// <summary>
/// The receipt an admin gets when a mass email finishes, copied to the job's "always copy" list.
///
/// That list is the oversight trail a director configures once and forgets: the league office sees what
/// went out, to how many people, and what failed — without being CC'd on the blast itself. 97% of jobs
/// have one set. It had no reader in this app at all, so those copies silently stopped: a director could
/// fill the field in, watch it save, and never learn that nobody downstream was being told anything.
///
/// Sent through the engine's OnCompleteAsync hook (see <see cref="EmailBatchPlan{TItem}"/>), which fires
/// once on a fresh DI scope after the pipeline drains.
/// </summary>
public static class BatchCompletionReceipt
{
    /// <summary>
    /// Mails the completion receipt: To the sending admin, CC the job's always-copy addresses, body
    /// carrying the outcome counts, any failures, and the blast content itself.
    /// Never throws — the engine treats a failed hook as non-fatal, and a receipt must never be able to
    /// retroactively "fail" a batch whose messages have already gone out.
    /// </summary>
    public static async Task SendAsync(
        EmailBatchJobStatus status,
        IServiceProvider sp,
        Guid jobId,
        string? senderEmail,
        string? fromName,
        string subject,
        string messageHtml,
        CancellationToken ct)
    {
        var jobs = sp.GetRequiredService<IJobRepository>();

        var alwaysCopy = EmailAddressRules.ParseDelimitedList(await jobs.GetAlwaysCopyEmailsAsync(jobId, ct));

        // The sender is already the To: — copying them again would just duplicate their own receipt.
        if (!string.IsNullOrWhiteSpace(senderEmail))
        {
            alwaysCopy.RemoveAll(a => string.Equals(a, senderEmail, StringComparison.OrdinalIgnoreCase));
        }

        // Fall back to addressing the always-copy list directly when there is no sender to receive it,
        // so a configured oversight list still gets its copy rather than the receipt being dropped.
        var hasSender = EmailAddressRules.IsSendable(senderEmail);
        var to = hasSender ? new List<string> { senderEmail!.Trim() } : alwaysCopy;
        var cc = hasSender ? alwaysCopy : new List<string>();

        if (to.Count == 0) return;

        var failed = status.FailedAddresses.Count > 0
            ? $"<br /><strong>Emails NOT sent:</strong> {string.Join("; ", status.FailedAddresses)}"
            : "";

        var body = $@"Batch Email Complete
            <br /><strong>Subject:</strong> {System.Net.WebUtility.HtmlEncode(subject)}
            <br /><strong>#Recipients:</strong> {status.TotalRecipients}
            <br /><strong>#Sent:</strong> {status.Sent}
            <br /><strong>#Failed:</strong> {status.Failed}
            <br /><strong>#Opted out:</strong> {status.OptedOut}{failed}
            <hr />{messageHtml}";

        var email = sp.GetRequiredService<IEmailService>();
        await email.SendAsync(new EmailMessageDto
        {
            FromName = string.IsNullOrWhiteSpace(fromName) ? "TEAMSPORTSINFO.COM" : fromName,
            ToAddresses = to,
            CcAddresses = cc,
            Subject = $"Batch Email Complete — {status.Sent} sent: {subject}",
            HtmlBody = body
        }, cancellationToken: ct);
    }
}
