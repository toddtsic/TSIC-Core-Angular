using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Shared.Email;

/// <summary>
/// Stamps a registration confirmation with the job's configured copies: CC, BCC, and the Reply-To that
/// routes a registrant's reply to the club rather than to TSIC support.
///
/// Every confirmation send path calls this — player (submit, ARB, eCheck-pending), the family resend,
/// team/club-rep, and adult/staff. It is one function because the four of them had drifted into four
/// different behaviours: only the team path applied copies at all (splitting on the wrong delimiter),
/// player confirmations copied nobody, and the director's "turn off CC &amp; BCC" switch was honoured
/// nowhere. Anything the job's copy config should mean belongs here, not at a call site.
/// </summary>
public static class JobConfirmationCopies
{
    /// <summary>
    /// Applies the job's CC/BCC and Reply-To to a confirmation message.
    ///
    /// Copies are suppressed wholesale when the job sets <c>bDisallowCCPlayerConfirmations</c>. That
    /// switch kills CC and BCC on every confirmation regardless of role — matching legacy, and matching
    /// what a director ticking a box called "turn off the copies" plainly expects. It never suppresses
    /// the confirmation itself: the registrant is always mailed.
    /// </summary>
    public static void Apply(EmailMessageDto message, IJobConfirmationCopyConfig job)
        => Apply(message, job.RegFormFrom, job.RegFormCcs, job.RegFormBccs, job.BDisallowCcplayerConfirmations);

    /// <summary>
    /// Overload for the adult path, which holds the <c>Jobs</c> entity rather than a repository DTO.
    /// The entity is scaffolded and may not be hand-edited to carry the interface, so it passes the
    /// four fields through instead. Same rules — this is the only implementation.
    /// </summary>
    public static void Apply(
        EmailMessageDto message,
        string? regFormFrom,
        string? regFormCcs,
        string? regFormBccs,
        bool? disallowCopies)
    {
        // Reply-To is not a copy and is not gated: a reply must reach the club whether or not the
        // director wants the office CC'd. From stays the SES-verified identity, forced at the send
        // chokepoint, so this is the only lever that puts a human behind the message.
        if (!string.IsNullOrWhiteSpace(regFormFrom))
        {
            message.ReplyToAddress = regFormFrom;
        }

        if (disallowCopies ?? false) return;

        var ccs = EmailAddressRules.ParseDelimitedList(regFormCcs);
        if (ccs.Count > 0) message.CcAddresses = ccs;

        var bccs = EmailAddressRules.ParseDelimitedList(regFormBccs);
        if (bccs.Count > 0) message.BccAddresses = bccs;
    }
}
