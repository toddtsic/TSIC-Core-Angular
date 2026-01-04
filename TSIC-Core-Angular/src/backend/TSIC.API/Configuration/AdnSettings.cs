namespace TSIC.API.Configuration;

/// <summary>
/// Configuration for Authorize.Net sandbox credentials.
/// Production credentials are loaded from database per customer/job.
/// </summary>
public class AdnSettings
{
    public string SandboxLoginId { get; set; } = string.Empty;
    public string SandboxTransactionKey { get; set; } = string.Empty;
}
