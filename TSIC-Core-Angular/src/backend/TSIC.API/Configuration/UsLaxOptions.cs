namespace TSIC.API.Configuration;

/// <summary>
/// Strongly typed options for USA Lacrosse integration.
/// Populate via appsettings ("UsLax" section) + environment overrides.
/// Do NOT store real secrets in source control; use user-secrets or platform secret store.
/// </summary>
public class UsLaxOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public string ApiBase { get; set; } = "https://api.usalacrosse.com/";
}