using Microsoft.AspNetCore.Identity;
using TSIC.Application.Services.Auth;
using TSIC.Contracts.Dtos.Mobile;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.API.Services.Auth;

/// <summary>Why an ownership-teams lookup did not return a list.</summary>
public enum OwnershipTeamsOutcome
{
    Ok,

    /// <summary>The regId is not a registration this user owns. Renders as 403.</summary>
    NotYours,

    /// <summary>The regId is a roster seat, which has no teams to choose between. Renders as 400.</summary>
    NotAnOwnership
}

public sealed record OwnershipTeamsResult
{
    public required OwnershipTeamsOutcome Outcome { get; init; }
    public IReadOnlyList<MobileOwnershipTeamDto> Teams { get; init; } = [];
}

public interface IMobileAuthService
{
    /// <summary>Null means invalid credentials. Everything else is a 200, including "nothing here for you".</summary>
    Task<MobileLoginResponse?> LoginAsync(string username, string password, CancellationToken ct = default);

    Task<OwnershipTeamsResult> GetOwnershipTeamsAsync(string userId, Guid regId, CancellationToken ct = default);
}

public sealed class MobileAuthService : IMobileAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthCredentialService _credentials;
    private readonly IRegistrationSelectionService _selection;
    private readonly IRegistrationRepository _registrationRepository;
    private readonly IUserRepository _userRepository;
    private readonly ITokenService _tokenService;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly IConfiguration _configuration;

    public MobileAuthService(
        UserManager<ApplicationUser> userManager,
        IAuthCredentialService credentials,
        IRegistrationSelectionService selection,
        IRegistrationRepository registrationRepository,
        IUserRepository userRepository,
        ITokenService tokenService,
        IRefreshTokenService refreshTokenService,
        IConfiguration configuration)
    {
        _userManager = userManager;
        _credentials = credentials;
        _selection = selection;
        _registrationRepository = registrationRepository;
        _userRepository = userRepository;
        _tokenService = tokenService;
        _refreshTokenService = refreshTokenService;
        _configuration = configuration;
    }

    public async Task<MobileLoginResponse?> LoginAsync(
        string username,
        string password,
        CancellationToken ct = default)
    {
        var user = await _userManager.FindByNameAsync(username);
        if (user == null || !await _credentials.IsPasswordValidAsync(user, password))
        {
            return null;
        }

        // Sequential awaits, never Task.WhenAll — these share one scoped DbContext.
        var requiresTos = await _userRepository.RequiresTosSignatureAsync(username);
        var contexts = await _registrationRepository.GetMobileContextsAsync(user.Id, ct);
        var ownerships = await _registrationRepository.GetMobileOwnershipsAsync(user.Id, ct);
        var hasExpired = await _registrationRepository.HasExpiredMobileRegistrationsAsync(user.Id, ct);

        var expirationMinutes = int.Parse(_configuration["JwtSettings:ExpirationMinutes"] ?? "60");
        var refreshToken = _refreshTokenService.GenerateRefreshToken(user.Id);

        // Auto-resolve. Requires the single context to be OPENABLE, not merely present: an
        // unplaced or Teams-disabled row is returned by design, and resolving into one would
        // mint a session pointed at a team that is not there.
        var soleOpenable = ownerships.Count == 0 && contexts.Count == 1 && contexts[0].IsOpenable
            ? contexts[0]
            : null;

        if (soleOpenable != null)
        {
            var selected = await _selection.SelectAsync(user, soleOpenable.RegId);
            if (selected.Succeeded)
            {
                return new MobileLoginResponse
                {
                    AccessToken = selected.AccessToken!,
                    RefreshToken = refreshToken,
                    ExpiresIn = selected.ExpiresInSeconds,
                    RequiresTosSignature = requiresTos,
                    AutoResolved = true,
                    HasExpiredRegistrations = hasExpired,
                    Contexts = contexts,
                    Ownerships = ownerships
                };
            }
            // Selection failing here would mean the registration vanished between two queries
            // in the same request. Fall through to the minimal token rather than 500 — the
            // client can still show the picker.
        }

        return new MobileLoginResponse
        {
            AccessToken = _tokenService.GenerateMinimalJwtToken(user),
            RefreshToken = refreshToken,
            ExpiresIn = expirationMinutes * 60,
            RequiresTosSignature = requiresTos,
            AutoResolved = false,
            HasExpiredRegistrations = hasExpired,
            Contexts = contexts,
            Ownerships = ownerships
        };
    }

    public async Task<OwnershipTeamsResult> GetOwnershipTeamsAsync(
        string userId,
        Guid regId,
        CancellationToken ct = default)
    {
        var teams = await _registrationRepository.GetMobileOwnershipTeamsAsync(userId, regId, ct);
        if (teams != null)
        {
            return new OwnershipTeamsResult { Outcome = OwnershipTeamsOutcome.Ok, Teams = teams };
        }

        // The repository answers "not an ownership registration of yours" with a single null.
        // Splitting that into 400 and 403 matters to the client: 400 means the app sent a
        // roster regId to an ownership endpoint and has a bug, 403 means the regId is not this
        // user at all. Only reached on the failure path, so the extra query costs nothing
        // in the normal case.
        var contexts = await _registrationRepository.GetMobileContextsAsync(userId, ct);
        var isRosterSeat = contexts.Exists(c =>
            string.Equals(c.RegId, regId.ToString(), StringComparison.OrdinalIgnoreCase));

        return new OwnershipTeamsResult
        {
            Outcome = isRosterSeat ? OwnershipTeamsOutcome.NotAnOwnership : OwnershipTeamsOutcome.NotYours
        };
    }
}
