using TSIC.Domain.Entities;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Pure topology math over a bracket <see cref="Templates"/> (its
/// <see cref="TemplateGames"/> + <see cref="AdvancementRoutes"/>): the
/// carry-forward "label" each game and slot resolves to.
///
/// A LEAF slot's label is its seed position. A FED slot's label is the label of
/// the game feeding it. A game's label is the min of its two slot labels — the
/// same identity the engine advances winners on and matches placed rows against.
///
/// Single-sourced here so bracket GENERATION (emit pairing rows from a template)
/// and PROJECTION (match placed rows back onto the template) compute identical
/// numbers by construction — they cannot drift.
/// </summary>
public static class BracketTemplateTopology
{
    /// <summary>
    /// Label for each (TemplateGameId, slot) where slot ∈ {1,2}. Leaf slot →
    /// seed position; fed slot → the feeding game's min-label.
    /// </summary>
    public static IReadOnlyDictionary<(int TemplateGameId, int Slot), int> ComputeSlotLabels(
        IReadOnlyCollection<TemplateGames> games,
        IReadOnlyCollection<AdvancementRoutes> routes)
    {
        var gamesById = games.ToDictionary(g => g.TemplateGameId);
        var sourceOfTargetSlot = routes.ToDictionary(
            r => (r.TargetTemplateGameId, (int)r.TargetSlot), r => r.SourceTemplateGameId);

        var minLabelMemo = new Dictionary<int, int>();
        var slotLabels = new Dictionary<(int, int), int>();

        int Label(int templateGameId)
        {
            if (minLabelMemo.TryGetValue(templateGameId, out var cached)) return cached;
            var g = gamesById[templateGameId];
            var label = Math.Min(SlotLabel(g, 1), SlotLabel(g, 2));
            minLabelMemo[templateGameId] = label;
            return label;
        }

        int SlotLabel(TemplateGames g, int slot)
        {
            if (slotLabels.TryGetValue((g.TemplateGameId, slot), out var cached)) return cached;
            var seed = slot == 1 ? g.Slot1Seed : g.Slot2Seed;
            var label = seed ?? Label(sourceOfTargetSlot[(g.TemplateGameId, slot)]);
            slotLabels[(g.TemplateGameId, slot)] = label;
            return label;
        }

        foreach (var g in games)
        {
            SlotLabel(g, 1);
            SlotLabel(g, 2);
        }

        return slotLabels;
    }

    /// <summary>
    /// Min-label per TemplateGameId — the stable identity a placed bracket row
    /// (min of its two slot numbers) matches against.
    /// </summary>
    public static IReadOnlyDictionary<int, int> ComputeMinLabels(
        IReadOnlyCollection<TemplateGames> games,
        IReadOnlyCollection<AdvancementRoutes> routes)
    {
        var slotLabels = ComputeSlotLabels(games, routes);
        var minLabels = new Dictionary<int, int>(games.Count);
        foreach (var g in games)
        {
            minLabels[g.TemplateGameId] = Math.Min(
                slotLabels[(g.TemplateGameId, 1)], slotLabels[(g.TemplateGameId, 2)]);
        }
        return minLabels;
    }
}
