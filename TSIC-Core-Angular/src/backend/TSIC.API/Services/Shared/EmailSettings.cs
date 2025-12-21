namespace TSIC.API.Services.Shared;

/// <summary>
/// Configuration for email sending. Only Amazon SES is supported; if EmailingEnabled is false
/// the system will short-circuit and report success without transmitting.
/// </summary>
public sealed class EmailSettings
{
    public string? SupportEmail { get; set; }
    public bool EmailingEnabled { get; set; } = true;
    /// <summary>
    /// Optional AWS region (e.g. us-east-1). If not supplied the default credentials/region chain is used.
    /// </summary>
    public string? AwsRegion { get; set; }
    /// <summary>
    /// If true, treat environment as SES sandbox mode: skip quota warnings and allow unverified recipient simulation.
    /// </summary>
    public bool SandboxMode { get; set; }
}
