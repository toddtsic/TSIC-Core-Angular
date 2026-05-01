namespace TSIC.API.Extensions;

/// <summary>
/// Sandbox gate for external integrations that touch real money / PII / customer comms.
/// Real Email (SES), real VerticalInsure, and the daily ADN sweep are permitted ONLY when
/// the host is TSIC-PHOENIX AND ASPNETCORE_ENVIRONMENT is Production. Every other box —
/// Staging, Development, CI — is sandboxed regardless of any single misconfiguration.
/// </summary>
public static class HostEnvironmentExtensions
{
    private const string LiveProductionMachineName = "TSIC-PHOENIX";

    public static bool IsLiveProduction(this IHostEnvironment env) =>
        env.IsProduction()
        && string.Equals(
            System.Environment.MachineName,
            LiveProductionMachineName,
            StringComparison.OrdinalIgnoreCase);

    public static bool IsSandbox(this IHostEnvironment env) => !env.IsLiveProduction();
}
