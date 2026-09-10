namespace TSIC.Contracts.Dtos.Teams;

/// <summary>
/// What a rank change actually did. A rank IS the team's slot in the pairing matrix, so moving
/// one team's rank trades two teams' games — the director needs to be told that in those words,
/// not handed a silent 204.
/// </summary>
public record TeamSeatingResultDto
{
    /// <summary>The team whose rank the director edited.</summary>
    public required string TeamName { get; init; }

    /// <summary>
    /// The team that held the target rank and was pushed back into the edited team's old one.
    /// Null when the target rank was vacant — nothing traded, the team simply moved.
    /// </summary>
    public string? SwappedWithTeamName { get; init; }

    /// <summary>Games whose occupant changed as a result. Zero when the division has no schedule yet.</summary>
    public required int GamesReseated { get; init; }

    /// <summary>Director-facing sentence, composed server-side so every door says the same thing.</summary>
    public required string Message { get; init; }
}
