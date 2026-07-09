namespace TSIC.Infrastructure.Utilities;

/// <summary>
/// The display label for an UNOCCUPIED bracket slot — the director's seed intent
/// ("X1 (Pool 01#1)"), rendered from BracketSeeds into Schedule.T1Name/T2Name.
///
/// A bracket slot is minted with no team and no name. It gains this label when the
/// director seeds it, and loses it when seed resolution stamps the real team. So the
/// label is derived state: single-sourced here so the write side (BracketSeedService)
/// and the restore side (ScheduleRepository) can never drift.
/// </summary>
public static class BracketSlotLabel
{
    public static string Format(string? slotType, int? slotNo, string divName, int seedRank) =>
        $"{slotType}{slotNo} ({divName}#{seedRank})";
}
