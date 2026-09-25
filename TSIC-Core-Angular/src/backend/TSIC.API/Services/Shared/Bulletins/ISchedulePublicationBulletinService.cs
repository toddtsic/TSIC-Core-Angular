namespace TSIC.API.Services.Shared.Bulletins;

/// <summary>
/// The data-side reaction to a job's schedule being released to the public
/// (<c>Jobs.bScheduleAllowPublicAccess</c> going false → true).
///
/// It writes rows. It never makes anything appear. See the implementation for why
/// that distinction is the whole point of this service.
/// </summary>
public interface ISchedulePublicationBulletinService
{
    /// <summary>
    /// Run the schedule-release bulletin housekeeping for one job: drop the superseded
    /// legacy schedule bulletin, and seed an INACTIVE "Schedules Available!" bulletin if
    /// the job has none. Idempotent — safe to call again, though callers should only call
    /// it on a real false → true transition.
    /// </summary>
    Task OnSchedulePublishedAsync(Guid jobId, CancellationToken cancellationToken = default);
}
