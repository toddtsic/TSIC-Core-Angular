namespace TSIC.Contracts.Dtos.Bulletin;

/// <summary>
/// Request body for POST /api/Bulletins/preview. Resolves {{TOKEN}} markers
/// in arbitrary HTML using either the real job pulse or a SuperUser-supplied
/// override (to simulate future states like "after registration closes").
/// </summary>
public sealed record BulletinPreviewRequest
{
    public required string Html { get; init; }
    public required string JobPath { get; init; }

    /// <summary>
    /// Optional pulse override. When null, the real job pulse is used.
    /// When provided, each of its flags replaces the real value.
    /// </summary>
    public JobPulseDto? PulseOverride { get; init; }
}

public sealed record BulletinPreviewResponse
{
    public required string Html { get; init; }
}
