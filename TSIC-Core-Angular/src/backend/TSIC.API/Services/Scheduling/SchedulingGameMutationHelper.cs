using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Shared move/swap game logic used by both ScheduleDivisionService and ReschedulerService.
/// </summary>
public static class SchedulingGameMutationHelper
{
    public static async Task MoveOrSwapGameAsync(
        MoveGameRequest request,
        string userId,
        IScheduleRepository scheduleRepo,
        IFieldRepository fieldRepo,
        ILogger logger,
        CancellationToken ct)
    {
        var gameA = await scheduleRepo.GetGameByIdAsync(request.Gid, ct)
            ?? throw new KeyNotFoundException($"Game {request.Gid} not found.");

        var gameB = await scheduleRepo.GetGameAtSlotAsync(request.TargetGDate, request.TargetFieldId, ct);

        if (gameB == null)
        {
            // Empty slot — simple move
            var field = await fieldRepo.GetFieldByIdAsync(request.TargetFieldId, ct);
            gameA.GDate = request.TargetGDate;
            gameA.FieldId = request.TargetFieldId;
            gameA.FName = field?.FName ?? "";
            gameA.RescheduleCount = (gameA.RescheduleCount ?? 0) + 1;
            gameA.Modified = DateTime.UtcNow;
            gameA.LebUserId = userId;

            logger.LogInformation("MoveGame: Gid={Gid} → {NewDate} field {FieldName}",
                request.Gid, request.TargetGDate, field?.FName);
        }
        else
        {
            // Occupied slot — swap
            var tempDate = gameA.GDate;
            var tempFieldId = gameA.FieldId;
            var tempFName = gameA.FName;

            gameA.GDate = gameB.GDate;
            gameA.FieldId = gameB.FieldId;
            gameA.FName = gameB.FName;
            gameA.RescheduleCount = (gameA.RescheduleCount ?? 0) + 1;
            gameA.Modified = DateTime.UtcNow;
            gameA.LebUserId = userId;

            gameB.GDate = tempDate;
            gameB.FieldId = tempFieldId;
            gameB.FName = tempFName;
            gameB.RescheduleCount = (gameB.RescheduleCount ?? 0) + 1;
            gameB.Modified = DateTime.UtcNow;
            gameB.LebUserId = userId;

            logger.LogInformation("SwapGames: Gid={GidA} ↔ Gid={GidB}", request.Gid, gameB.Gid);
        }

        await scheduleRepo.SaveChangesAsync(ct);
    }
}
