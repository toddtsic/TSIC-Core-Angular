using TSIC.Application.Services.Users;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.API.Services.Auth;

/// <summary>
/// Outcome of resolving a regId to an enriched token. Failure is a domain answer, not an
/// exception: the caller decides whether it renders as 400 (web) or 403 (mobile).
/// </summary>
public sealed record RegistrationSelectionResult
{
    public required bool Succeeded { get; init; }
    public string? AccessToken { get; init; }
    public int ExpiresInSeconds { get; init; }
    public string? RoleName { get; init; }
    public string? JobPath { get; init; }

    public static RegistrationSelectionResult NotAvailable() => new() { Succeeded = false };
}

/// <summary>
/// Phase 2 of the two-phase auth flow: prove the registration belongs to the caller, then
/// mint the enriched token. Shared verbatim by the web and mobile select-registration
/// endpoints — mobile gets its own ROUTE so the flow reads in one file, never its own copy
/// of this logic.
/// </summary>
public interface IRegistrationSelectionService
{
    Task<RegistrationSelectionResult> SelectAsync(ApplicationUser user, string regId);
}

public sealed class RegistrationSelectionService : IRegistrationSelectionService
{
    private readonly IRoleLookupService _roleLookupService;
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _configuration;

    public RegistrationSelectionService(
        IRoleLookupService roleLookupService,
        ITokenService tokenService,
        IConfiguration configuration)
    {
        _roleLookupService = roleLookupService;
        _tokenService = tokenService;
        _configuration = configuration;
    }

    public async Task<RegistrationSelectionResult> SelectAsync(ApplicationUser user, string regId)
    {
        if (string.IsNullOrWhiteSpace(regId))
        {
            return RegistrationSelectionResult.NotAvailable();
        }

        // The registration must be one this user actually owns. This is the check that keeps
        // a caller from minting a token for somebody else regId.
        //
        // GUID equality is case-insensitive by definition; storage casing is not stable (EF
        // projects via SQL CAST which preserves DB casing, while System.Text.Json serializes
        // new Guids lowercase) so always compare with OrdinalIgnoreCase.
        var registrations = await _roleLookupService.GetRegistrationsForUserAsync(user.Id);

        var selectedReg = registrations
            .SelectMany(r => r.RoleRegistrations)
            .ToList()
            .Find(reg => string.Equals(reg.RegId, regId, StringComparison.OrdinalIgnoreCase));

        if (selectedReg == null)
        {
            return RegistrationSelectionResult.NotAvailable();
        }

        var registrationRole = registrations
            .ToList()
            .Find(r => r.RoleRegistrations.Exists(reg => string.Equals(reg.RegId, regId, StringComparison.OrdinalIgnoreCase)));

        var roleName = registrationRole?.RoleName ?? "User";

        // Jobs.jobPath is non-nullable in the schema and carries a unique index, so the null
        // branch here is unreachable in practice. It is left as a hard failure rather than the
        // old fallback, which substituted an Angular ROUTE for a job path and produced a
        // jobPath claim that could never match a real job.
        if (string.IsNullOrWhiteSpace(selectedReg.JobPath))
        {
            return RegistrationSelectionResult.NotAvailable();
        }

        var expirationMinutes = int.Parse(_configuration["JwtSettings:ExpirationMinutes"] ?? "60");

        var token = _tokenService.GenerateEnrichedJwtToken(
            user, regId, selectedReg.JobPath, selectedReg.JobLogo, roleName);

        return new RegistrationSelectionResult
        {
            Succeeded = true,
            AccessToken = token,
            ExpiresInSeconds = expirationMinutes * 60,
            RoleName = roleName,
            JobPath = selectedReg.JobPath
        };
    }
}
