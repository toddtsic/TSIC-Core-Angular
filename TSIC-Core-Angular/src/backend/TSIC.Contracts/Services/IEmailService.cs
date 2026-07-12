namespace TSIC.Contracts.Services;

/// <summary>
/// MIME-agnostic email send interface for cross-layer use.
/// Implementations can translate to concrete email formats (e.g., MimeKit) internally.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends a single email message. Returns true if transmitted (or short-circuited as enabled=false), false on failure.
    /// </summary>
    Task<bool> SendAsync(EmailMessageDto message, bool sendInDevelopment = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a batch of messages, returning addresses attempted and those that failed.
    /// </summary>
    Task<EmailBatchSendResult> SendBatchAsync(IEnumerable<EmailMessageDto> messages, CancellationToken cancellationToken = default);
}

/// <summary>
/// Simple transport DTO for email messages.
/// </summary>
public sealed class EmailMessageDto
{
    /// <summary>
    /// Display name for the From header. The From ADDRESS is not caller-settable — it is always forced
    /// to the SES-verified identity (support@teamsportsinfo.com) at the send chokepoint. Put the real
    /// human/contact on <see cref="ReplyToAddress"/>.
    /// </summary>
    public string? FromName { get; set; }
    /// <summary>
    /// Display name for the Reply-To header. When <see cref="ReplyToAddress"/> is set, replies route
    /// to that address (e.g. the sending admin) instead of the verified From identity.
    /// </summary>
    public string? ReplyToName { get; set; }
    /// <summary>
    /// Reply-To address — the real human/contact behind a message sent under the verified From identity.
    /// Ignored if blank or unparseable (Reply-To then falls back to the From identity).
    /// </summary>
    public string? ReplyToAddress { get; set; }
    public List<string> ToAddresses { get; set; } = new();
    public List<string> CcAddresses { get; set; } = new();
    public List<string> BccAddresses { get; set; } = new();
    public string? Subject { get; set; }
    public string? HtmlBody { get; set; }
    public string? TextBody { get; set; }
    /// <summary>
    /// Files to attach. The send path is already raw-MIME (SES <c>SendRawEmail</c>), so these ride the
    /// same message as the body. SES caps the whole raw message at 10 MB after base64 encoding — keep
    /// attachments well under that.
    /// </summary>
    public List<EmailAttachmentDto> Attachments { get; set; } = new();
}

/// <summary>A single email attachment: the bytes, the filename the recipient sees, and its MIME type.</summary>
public sealed class EmailAttachmentDto
{
    public required string FileName { get; init; }
    public required byte[] Content { get; init; }
    public required string ContentType { get; init; }
}

public sealed class EmailBatchSendResult
{
    public List<string> AllAddresses { get; } = new();
    public List<string> FailedAddresses { get; } = new();
}
