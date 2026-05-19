namespace TSIC.API.Extensions;

/// <summary>
/// Sandbox gate for external integrations that touch real money / PII / customer comms.
/// Real Email (SES), real VerticalInsure, and the daily ADN sweep are permitted ONLY when
/// ASPNETCORE_ENVIRONMENT is Production. Every other env name (Staging, Development,
/// Testing, CI) is sandboxed. The deployment is responsible for setting the env var
/// correctly on each host; Program.cs refuses to start if it is unset.
/// </summary>
public static class HostEnvironmentExtensions
{
    public static bool IsLiveProduction(this IHostEnvironment env) => env.IsProduction();

    public static bool IsSandbox(this IHostEnvironment env) => !env.IsLiveProduction();
}
