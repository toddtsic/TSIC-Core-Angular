namespace TSIC.Contracts.Dtos.Ladt;

public record DivisionDetailDto
{
    public required Guid DivId { get; init; }
    public required Guid AgegroupId { get; init; }
    public string? DivName { get; init; }
    public int? MaxRoundNumberToShow { get; init; }
}

public record CreateDivisionRequest
{
    public required Guid AgegroupId { get; init; }
    public required string DivName { get; init; }
    public int? MaxRoundNumberToShow { get; init; }
}

public record UpdateDivisionRequest
{
    public string? DivName { get; init; }
    public int? MaxRoundNumberToShow { get; init; }
}


// ── Common Divisions (Theme Division Names dialog) ──

/// <summary>
/// One division name as it exists across the job's age groups. The dialog works on names,
/// not on individual divisions — adding a name creates it in every age group that lacks it.
/// </summary>
public record CommonDivisionDto
{
    public required string DivName { get; init; }

    /// <summary>How many age groups currently carry a division with this name.</summary>
    public required int AgegroupCount { get; init; }

    /// <summary>Total age groups in scope, so the UI can show a name that is not yet universal.</summary>
    public required int AgegroupTotal { get; init; }

    /// <summary>True when no division with this name holds a team anywhere — only then is removal offered.</summary>
    public required bool CanRemove { get; init; }
}

public record CommonDivisionNameRequest
{
    public required string DivName { get; init; }
}

public record CommonDivisionMutationResult
{
    public required int AgegroupsAffected { get; init; }
    public required List<string> Errors { get; init; }
}
