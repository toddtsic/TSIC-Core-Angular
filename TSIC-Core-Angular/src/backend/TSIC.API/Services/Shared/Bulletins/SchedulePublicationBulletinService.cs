using System.Text.RegularExpressions;
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
public sealed partial class SchedulePublicationBulletinService : ISchedulePublicationBulletinService
{
    /// <summary>
    /// Title of the seeded bulletin. NOT the create-once key on its own — see
    /// <see cref="PointsAtSchedule"/>; it is one of two signals, kept because a title match is
    /// free and can only reduce duplicates.
    /// </summary>
    public const string SeededTitle = "Schedules Available!";

    /// <summary>
    /// True if this bulletin ALREADY points at the current schedule — the real create-once key.
    ///
    /// Keying create-once on the title alone was wrong, and inconsistent with the delete side of
    /// this very method, which matches the legacy bulletin on its BODY precisely because titles
    /// drift ("Schedules Are Posted!", "SCHEDULES ARE LIVE", "Schedule is UP!"). A director
    /// renames the seeded bulletin to "2026 Schedules Are Up" — which they will, it is their
    /// event — the rename clones forward, and next season's flag flip seeds a second one.
    ///
    /// So: old URL means remove, new URL means do not seed. Symmetric, and rename-proof.
    ///
    /// Matches the !SCHEDULE token (which also covers !SCHEDULELINK — both are schedule links)
    /// or an authored link ending in <c>/{jobPath}/schedule</c>. It CAN skip seeding for a job
    /// whose director hand-wrote their own schedule bulletin; that is correct, they already have
    /// the announcement and a second one is noise.
    /// </summary>
    private static bool PointsAtSchedule(string? text) =>
        !string.IsNullOrEmpty(text)
        && (text.Contains("!SCHEDULE", StringComparison.OrdinalIgnoreCase)
            || CurrentScheduleLink().IsMatch(text));

    /// <summary>
    /// An authored link at the Angular schedule route: <c>/{jobPath}/schedule</c>, with the
    /// jobPath segment left open so this needs no per-job plumbing. The trailing lookahead is
    /// what keeps the LEGACY route out — "/!JSEG/Schedules/Index" has an "s" after "schedule",
    /// so it cannot match, and the legacy rows are being deleted in the same pass anyway.
    /// </summary>
    [GeneratedRegex("""/[^/"'\s>]+/schedule(?=["'?#/>]|$)""", RegexOptions.IgnoreCase)]
    private static partial Regex CurrentScheduleLink();

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

        // Either signal counts. Both mean "this job already has a schedule bulletin", and
        // matching on either can only reduce duplicates.
        var alreadySeeded = existing
            .Except(superseded)
            .Any(b => PointsAtSchedule(b.Text)
                      || string.Equals(b.Title?.Trim(), SeededTitle, StringComparison.OrdinalIgnoreCase));

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
    /// Seed body: one row, no prose. The bulletin TITLE already says "Schedules Available!",
    /// so a lede sentence would only say it again — the body's whole job is the actions.
    /// Primary action first, the two app links beside it as quieter peers.
    ///
    /// The store links keep their two-word labels rather than going glyph-only (Todd,
    /// 2026-09-25). A bare Apple mark next to a play triangle reads as a share target, not
    /// an app download — and that is the one thing the title does NOT already convey. The
    /// visible text is also the accessible name: the sanitizer strips <c>aria-*</c>, so an
    /// unlabelled icon link would reach a screen reader as its bare href.
    ///
    /// The <c>sched-*</c> classes are styled globally under <c>.bulletin-body</c> (see
    /// styles/_component-overrides.scss) and survive the rich-text sanitizer, which keeps
    /// <c>class</c>. They degrade to plain links if a director rewrites the body.
    ///
    /// No QR codes: the panel generated those per viewer in the browser, and static bulletin
    /// HTML would need them hosted as files.
    /// </summary>
    private const string SeedBody = """
        <div class="sched-row">
            !SCHEDULE
            <a class="sched-links__store" href="https://itunes.apple.com/app/id1550380490" target="_blank" rel="noopener noreferrer"><i class="bi bi-apple sched-links__glyph"></i><span class="sched-links__name">App Store</span></a>
            <a class="sched-links__store" href="https://play.google.com/store/apps/details?id=com.teamsportsinfo.tsicevents" target="_blank" rel="noopener noreferrer"><i class="bi bi-google-play sched-links__glyph"></i><span class="sched-links__name">Google Play</span></a>
        </div>
        """;
}
