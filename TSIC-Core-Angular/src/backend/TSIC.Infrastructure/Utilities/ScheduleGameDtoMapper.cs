using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Domain.Entities;

namespace TSIC.Infrastructure.Utilities;

/// <summary>
/// Shared mapper from Schedule entity → ScheduleGameDto.
/// Used by both ScheduleRepository (grid assembly) and ScheduleDivisionService (placement).
/// </summary>
public static class ScheduleGameDtoMapper
{
    public static ScheduleGameDto Map(
        Schedule game,
        string? overrideColor = null,
        bool isSlotCollision = false) => new()
    {
        Gid = game.Gid,
        GDate = game.GDate ?? DateTime.MinValue,
        FieldId = game.FieldId ?? Guid.Empty,
        FName = game.FName ?? "",
        Rnd = game.Rnd ?? 0,
        AgDivLabel = $"{game.AgegroupName}:{game.DivName}",
        T1Label = FormatTeamLabel(game.T1No, game.T1Name, game.T1Type),
        T2Label = FormatTeamLabel(game.T2No.HasValue ? (int)game.T2No.Value : null, game.T2Name, game.T2Type),
        Color = overrideColor ?? game.Agegroup?.Color,
        T1Type = game.T1Type ?? "T",
        T2Type = game.T2Type ?? "T",
        T1No = game.T1No,
        T2No = game.T2No,
        T1Id = game.T1Id,
        T2Id = game.T2Id,
        DivId = game.DivId,
        IsSlotCollision = isSlotCollision
    };

    public static string FormatTeamLabel(int? teamNo, string? teamName, string? teamType)
    {
        if (!string.IsNullOrEmpty(teamName))
            return teamName;
        return $"{teamType ?? "T"}{teamNo ?? 0}";
    }
}
