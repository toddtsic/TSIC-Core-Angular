namespace TSIC.Contracts.Dtos.Scheduling;

/// <summary>
/// A game placement refused because its job + field + G_Date slot already holds a game.
/// Exists so the controller's 409 branch catches ONLY this deliberate refusal — the same
/// reason <see cref="TSIC.Contracts.Dtos.JobClone.CloneConflictException"/> exists. PlaceGameAsync can also throw
/// a bare InvalidOperationException after the insert ("Failed to read back placed game"),
/// which must stay a 500, not be reported to a director as "slot taken".
/// </summary>
public sealed class ScheduleSlotTakenException : InvalidOperationException
{
    public ScheduleSlotTakenException(string message) : base(message)
    {
    }
}
