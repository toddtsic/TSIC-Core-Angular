namespace TSIC.API.Configuration;

/// <summary>
/// AWS credential configuration for local/secret storage. Prefer IAM roles in production when running on AWS.
/// </summary>
public sealed class AwsSettings
{
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public string? Region { get; set; }
}
