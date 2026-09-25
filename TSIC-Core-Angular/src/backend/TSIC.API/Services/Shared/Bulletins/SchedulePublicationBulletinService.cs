using TSIC.Contracts.Repositories;
// This namespace is itself called Bulletins, which shadows the entity type — same alias
// BulletinService uses.
using BulletinEntity = TSIC.Domain.Entities.Bulletins;

namespace TSIC.API.Services.Shared.Bulletins;

/// <summary>
/// Releasing a schedule used to make an announcement appear by itself — the Smart Bulletins
/// band raised a "Schedule Links" card (and the countdown clock) the instant
/// <c>bScheduleAllowPublicAccess</c> went true. That broke a workflow directors rely on:
/// they open the schedule and email their club reps to review it, the reps forward that mail
/// to their coaches, and nobody wants the general public looking yet. An auto-announcement
/// turns a quiet review window into a public launch.
///
/// So the auto-DISPLAY is gone and this auto-DATA takes its place. Releasing the schedule
/// now writes an INACTIVE bulletin the director activates when the review is done — the same
/// act they have always performed, on the surface they already use. Nothing this service does
/// is visible to a visitor until a human clicks Active.
///
/// It also removes the superseded legacy bulletin, which is what the Smart Bulletins card had
/// been built to automate away in the first place.
/// </summary>
public sealed class SchedulePublicationBulletinService : ISchedulePublicationBulletinService
{
    /// <summary>
    /// Title of the seeded bulletin, and — deliberately — the create-once key. Existence of a
    /// bulletin by this title is what stops a second one being seeded, so no schema is needed
    /// to remember that the job has already been served. A director who deletes it and later
    /// re-releases the schedule gets a fresh one, which is the forgiving behaviour.
    /// </summary>
    public const string SeededTitle = "Schedules Available!";

    private readonly IBulletinRepository _bulletins;
    private readonly ILogger<SchedulePublicationBulletinService> _logger;

    public SchedulePublicationBulletinService(
        IBulletinRepository bulletins,
        ILogger<SchedulePublicationBulletinService> logger)
    {
        _bulletins = bulletins;
        _logger = logger;
    }

    public async Task OnSchedulePublishedAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var existing = await _bulletins.GetAllForJobTrackedAsync(jobId, cancellationToken);

        // Matched on the BODY, not the title — the legacy schedule bulletin ships under at
        // least four different titles across historic jobs, and the body fragment catches
        // every one of them. Removed whether or not it is currently Active: its link points
        // at the retired MVC route, so an active one is a broken bulletin on a live page.
        var superseded = existing
            .Where(b => LegacyBulletinPatterns.HasLegacyScheduleLink(b.Text))
            .ToList();

        foreach (var legacy in superseded)
        {
            _bulletins.Remove(legacy);
        }

        var alreadySeeded = existing
            .Except(superseded)
            .Any(b => string.Equals(b.Title?.Trim(), SeededTitle, StringComparison.OrdinalIgnoreCase));

        if (!alreadySeeded)
        {
            _bulletins.Add(BuildSeed(jobId));
        }

        if (superseded.Count > 0 || !alreadySeeded)
        {
            await _bulletins.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Schedule released for job {JobId}: removed {RemovedCount} legacy schedule bulletin(s), seeded={Seeded}.",
            jobId, superseded.Count, !alreadySeeded);
    }

    /// <summary>
    /// The seeded row. INACTIVE by design — the director owns the announcement moment.
    ///
    /// StartDate is set to now and NOT left null: the public fetch filters on
    /// <c>StartDate != null AND StartDate &lt;= now</c>, so a null-dated bulletin would stay
    /// invisible even after a director activated it, which would read as a broken toggle.
    ///
    /// The body is ordinary editable HTML using the project's !TOKEN vocabulary, so a director
    /// can rewrite it freely. !SCHEDULE resolves to the View Schedule button and blanks itself
    /// if the schedule is ever unpublished; !JOBNAME resolves to the event name.
    /// </summary>
    private static BulletinEntity BuildSeed(Guid jobId) => new()
    {
        BulletinId = Guid.NewGuid(),
        JobId = jobId,
        Title = SeededTitle,
        Text = SeedBody,
        Active = false,
        StartDate = DateTime.Now,
        EndDate = null,
        // System-authored: no human typed this, so it carries no lebUserID rather than
        // crediting whichever admin happened to flip the flag.
        LebUserId = null,
        CreateDate = DateTime.Now,
        Modified = DateTime.Now
    };

    /// <summary>
    /// Seed body. The store badges are the same artwork, at the same statics URLs, that the
    /// hand-authored bulletin this replaces has carried for years — a text link where a badge
    /// belongs is what a director would notice first. Hosted (not embedded): bulletin images
    /// are link-only by project rule.
    /// </summary>
    private const string SeedBody = """
        <p>Game schedules for !JOBNAME are now available.</p>
        <p>!SCHEDULE</p>
        <p>Scores, standings and brackets are also in the free TSIC-Events app:</p>
        <ul>
            <li><strong>iOS: TSIC-EVENTS</strong>&nbsp;&nbsp;<a href="https://itunes.apple.com/app/id1550380490" target="_blank" rel="noopener noreferrer"><img src="https://statics.teamsportsinfo.com/mobile/images/appstore.jpg" alt="Download on the App Store" /></a></li>
            <li><strong>Android: TSIC-EVENTS</strong>&nbsp;&nbsp;<a href="https://play.google.com/store/apps/details?id=com.teamsportsinfo.tsicevents" target="_blank" rel="noopener noreferrer"><img src="https://statics.teamsportsinfo.com/mobile/images/playstore.jpg" alt="Get it on Google Play" /></a></li>
        </ul>
        """;
}
