namespace TSIC.Domain.Constants;

/// <summary>
/// Job type identifiers from the <c>reference.JobTypes</c> table.
/// Used to gate feature availability (team registration, adult role resolution, etc.).
/// </summary>
public static class JobConstants
{
    /// <summary>Root/system container — not a real job.</summary>
    public const int JobTypeRoot = 0;

    /// <summary>Club sport — season-long club management with director-vetted staff.</summary>
    public const int JobTypeClub = 1;

    /// <summary>Tournament event — supports team registration and coach self-rostering.</summary>
    public const int JobTypeTournament = 2;

    /// <summary>League — season-long league registration.</summary>
    public const int JobTypeLeague = 3;

    /// <summary>Camp / clinic event — no team registration.</summary>
    public const int JobTypeCamp = 4;

    /// <summary>Sales / retail event — no team registration.</summary>
    public const int JobTypeSales = 5;
}
