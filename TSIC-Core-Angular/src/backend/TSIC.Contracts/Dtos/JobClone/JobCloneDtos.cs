namespace TSIC.Contracts.Dtos.JobClone;

// ══════════════════════════════════════
// Request
// ══════════════════════════════════════

public record JobCloneRequest
{
    public required Guid SourceJobId { get; init; }

    // Target identity
    public required string JobPathTarget { get; init; }
    public required string JobNameTarget { get; init; }
    public required string YearTarget { get; init; }
    public required string SeasonTarget { get; init; }
    public required string DisplayName { get; init; }
    public required string LeagueNameTarget { get; init; }

    // Target dates
    public required DateTime ExpiryAdmin { get; init; }
    public required DateTime ExpiryUsers { get; init; }

    // Email
    public string? RegFormFrom { get; init; }

    // Flags
    public bool UpAgegroupNamesByOne { get; init; }
    public bool SetDirectorsToInactive { get; init; }
    public bool NoParallaxSlide1 { get; init; }
}

// ══════════════════════════════════════
// Response
// ══════════════════════════════════════

public record JobCloneResponse
{
    public required Guid NewJobId { get; init; }
    public required string NewJobPath { get; init; }
    public required string NewJobName { get; init; }
    public required CloneSummary Summary { get; init; }
}

public record CloneSummary
{
    public int BulletinsCloned { get; init; }
    public int AgeRangesCloned { get; init; }
    public int MenusCloned { get; init; }
    public int MenuItemsCloned { get; init; }
    public int AdminRegistrationsCloned { get; init; }
    public int LeaguesCloned { get; init; }
    public int AgegroupsCloned { get; init; }
    public int DivisionsCloned { get; init; }
}

// ══════════════════════════════════════
// Source picker (for frontend dropdown)
// ══════════════════════════════════════

public record JobCloneSourceDto
{
    public required Guid JobId { get; init; }
    public required string JobPath { get; init; }
    public required string JobName { get; init; }
    public string? Year { get; init; }
    public string? Season { get; init; }
    public string? DisplayName { get; init; }
    public required Guid CustomerId { get; init; }
}
