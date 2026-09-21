namespace TSIC.Contracts.Dtos;

public record EmailLogSummaryDto
{
    public required int EmailId { get; init; }
    public required DateTime SendTs { get; init; }

    /// <summary>
    /// The label the SENDING PATH chose to record — what the recipient's inbox showed, roughly.
    /// Caller-supplied and inconsistent by design: the job's display name on batch email, the
    /// acting admin's address on USA Lacrosse / Rescheduler / Store, the club director's address
    /// on the ARB paths (deliberate — AR-068: a family sees one identity per club). It is NOT
    /// the sender: SES forces the SMTP From to support@teamsportsinfo.com on every send.
    /// Use <see cref="SenderEmail"/> to answer "who sent this".
    /// </summary>
    public string? SendFrom { get; init; }

    /// <summary>
    /// WHO INITIATED THE SEND, derived at read time from <c>emailLogs.senderUserID</c> (FK →
    /// AspNetUsers). Correct on every human path — each writer stamps it from the JWT
    /// NameIdentifier claim — and already populated on historical rows, so this reads correctly
    /// backwards through the whole log. Unattended sweeps and null resolve to the support address.
    /// </summary>
    public string? SenderEmail { get; init; }

    public int? Count { get; init; }
    public string? Subject { get; init; }
}

public record EmailLogDetailDto
{
    public required int EmailId { get; init; }
    public string? SendTo { get; init; }
    public string? Msg { get; init; }
}
