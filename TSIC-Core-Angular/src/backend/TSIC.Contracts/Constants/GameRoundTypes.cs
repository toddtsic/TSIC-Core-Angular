namespace TSIC.Contracts.Constants;

/// <summary>
/// Slot types on Leagues.schedule (T1_Type / T2_Type). Both slots of a game carry the
/// same type.
///
/// "Not round-robin" is NOT the same as "bracket". A consolation game
/// (<see cref="Consolation"/>) is a standalone placement game — 5th seed vs 6th seed.
/// It is seeded from pool standings exactly as a bracket leaf is, but it never feeds
/// another game, never advances a winner, and never appears in the ladder. Legacy spelled
/// this out as <c>T1Type != "T" &amp;&amp; T1Type != "C"</c>. Classifying on <c>!= "T"</c>
/// alone drags consolation games into the bracket projection, where a 5v6 and a 5v7 collide
/// on the min-label that is supposed to identify exactly one ladder game — and, because a
/// consolation game is a normal game, wrongly forbids it from ending in a tie.
/// </summary>
public static class GameRoundTypes
{
    /// <summary>Round-robin / pool game. The only type that feeds standings.</summary>
    public const string RoundRobin = "T";

    /// <summary>Consolation / placement game: seeded, never advanced, never in the ladder.</summary>
    public const string Consolation = "C";

    /// <summary>Bronze (3rd-place) game — fed by two Loser routes, so it is a bracket game, but not a ladder round.</summary>
    public const string Bronze = "B";

    /// <summary>Single-elimination ladder round → number of teams entering that round.</summary>
    public static readonly IReadOnlyDictionary<string, int> LadderRoundSize =
        new Dictionary<string, int>
        {
            ["Z"] = 64, ["Y"] = 32, ["X"] = 16, ["Q"] = 8, ["S"] = 4, ["F"] = 2
        };

    /// <summary>
    /// Every slot type that takes part in the bracket topology: the ladder rounds plus bronze.
    /// This is the allow-list for seeds, feeds and BracketInstances — never test <c>!= "T"</c>.
    /// </summary>
    public static readonly string[] Bracket = [.. LadderRoundSize.Keys, Bronze];

    /// <summary>True when <paramref name="slotType"/> takes part in the bracket topology.</summary>
    public static bool IsBracket(string? slotType) =>
        slotType is not null && Array.IndexOf(Bracket, slotType) >= 0;
}
