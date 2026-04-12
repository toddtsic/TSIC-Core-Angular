using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Shared.Bulletins.TokenResolution;

/// <summary>
/// Everything a bulletin token resolver needs to emit its final HTML.
/// Built once per bulletin-fetch request by BulletinService.
/// </summary>
public sealed record TokenContext
{
    public required string JobPath { get; init; }
    public required JobMetadataDto Job { get; init; }
    public required JobPulseDto Pulse { get; init; }

    /// <summary>Auth state of the caller. Public endpoint, so often false.</summary>
    public required bool IsAuthenticated { get; init; }

    /// <summary>Role of the caller if authenticated (e.g. "Player", "ClubRep"). Null when anonymous.</summary>
    public string? Role { get; init; }
}
