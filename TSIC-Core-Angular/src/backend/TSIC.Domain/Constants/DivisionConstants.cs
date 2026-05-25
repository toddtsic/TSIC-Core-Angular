namespace TSIC.Domain.Constants;

/// <summary>
/// Well-known division names that carry behavior across the system.
/// </summary>
public static class DivisionConstants
{
    /// <summary>
    /// The holding division every agegroup must have. Newly registered teams land
    /// here until an admin assigns them to a real division; it is excluded from
    /// scheduling, brackets, and timeslots. Auto-created with each agegroup and
    /// cannot be renamed or deleted (see LadtService). Single source of truth —
    /// referenced by team placement, LADT, scheduling, and bracket-seed filters.
    /// </summary>
    public const string Unassigned = "Unassigned";
}
