using MimeKit;

namespace TSIC.API.Services.Email;

public interface IEmailService
{
    /// <summary>
    /// Sends a single MIME email via Amazon SES. Returns true if transmitted (or short-circuited as enabled=false), false on failure.
    /// </summary>
    Task<bool> SendAsync(MimeMessage message, bool sendInDevelopment = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a batch of messages, returning addresses attempted and those that failed.
    /// </summary>
    Task<EmailBatchSendResult> SendBatchAsync(IEnumerable<MimeMessage> messages, CancellationToken cancellationToken = default);
}

public sealed class EmailBatchSendResult
{
    public List<string> AllAddresses { get; } = new();
    public List<string> FailedAddresses { get; } = new();
}
