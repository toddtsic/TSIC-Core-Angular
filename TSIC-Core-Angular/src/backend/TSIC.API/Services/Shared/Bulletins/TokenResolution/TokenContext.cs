using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Shared.Bulletins.TokenResolution;

/// <summary>
/// Everything a bulletin token resolver needs to emit its final HTML.
/// Bulletins are public-facing — viewer identity (role/auth) is NOT in scope
/// and deliberately absent from this type. Personalized tokens belong in the
/// email/!TOKEN channel handled by TextSubstitutionService.
/// </summary>
public sealed record TokenContext
{
    public required string JobPath { get; init; }
    public required JobMetadataDto Job { get; init; }
    public required JobPulseDto Pulse { get; init; }
}
