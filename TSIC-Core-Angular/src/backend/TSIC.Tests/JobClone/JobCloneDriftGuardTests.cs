using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using TSIC.API.Services.Admin;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.JobClone;

/// <summary>
/// The clone's drift defenses (approved exception to the no-new-tests default — these ARE
/// the mechanism Todd asked for):
///
///   D1 — NEW-FIELD TRIPWIRE. One property snapshot per cloned entity, enumerating the
///        exact scalar set the reflection copier copies. A schema regen that adds/removes
///        a column fails the snapshot with a diff, forcing one look at JobCloneResetRules
///        ("new field arrived: does it need a reset rule?") before the first clone runs —
///        not after an incident. Update = fix the snapshot file after the review.
///
///   D2 — NEW-TABLE DISPOSITION. Walks the EF model: every keyed entity with a Guid JobId
///        property must be either a clone-manifest type or a documented NotCloned entry
///        with a reason. A new job-scoped table turns this red until someone decides.
///        The same walk generates dev-undo's ancillary counts at runtime, so dev-undo can
///        never delete through a table it doesn't know about.
///
///   Manifest sanity — JobCloneStepOrder.Steps covers every step constant exactly once.
///   (Executor-handlers and dev-undo delete-actions are set-equality-checked against the
///   manifest at RUNTIME on every clone/undo; the service tests exercise those paths.)
/// </summary>
public class JobCloneDriftGuardTests
{
    // ══════════════════════════════════════════════════════════
    // D1 — property snapshots per cloned entity
    // ══════════════════════════════════════════════════════════

    /// <summary>Every entity type the reset rules run the scalar copier over.</summary>
    private static readonly Type[] ClonedEntityTypes =
    [
        typeof(Jobs),
        typeof(JobDisplayOptions),
        typeof(JobOwlImages),
        typeof(Bulletins),
        typeof(JobAgeRanges),
        typeof(JobMenus),
        typeof(JobMenuItems),
        typeof(JobReports),
        typeof(Nav),
        typeof(NavItem),
        typeof(Registrations),
        typeof(Leagues),
        typeof(JobLeagues),
        typeof(Agegroups),
        typeof(Divisions),
        typeof(Teams),
        typeof(JobFees),
        typeof(FeeModifiers),
    ];

    public static TheoryData<Type> ClonedEntityTypeData()
    {
        var data = new TheoryData<Type>();
        foreach (var t in ClonedEntityTypes) data.Add(t);
        return data;
    }

    [Theory]
    [MemberData(nameof(ClonedEntityTypeData))]
    public void D1_ScalarPropertySnapshot_MatchesEntity(Type entityType)
    {
        var current = JobCloneEntityCopier.GetScalarProperties(entityType)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var snapshotPath = Path.Combine(SnapshotDir(), $"{entityType.Name}.txt");

        if (!File.Exists(snapshotPath))
        {
            Directory.CreateDirectory(SnapshotDir());
            File.WriteAllLines(snapshotPath, current);
            Assert.Fail(
                $"D1 snapshot for {entityType.Name} did not exist — created it at "
                + $"{snapshotPath} with {current.Count} properties. REVIEW JobCloneResetRules "
                + "for this entity, then rerun (the test passes once the snapshot exists).");
        }

        var snapshot = File.ReadAllLines(snapshotPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .ToList();

        var added = current.Except(snapshot, StringComparer.Ordinal).ToList();
        var removed = snapshot.Except(current, StringComparer.Ordinal).ToList();

        if (added.Count > 0 || removed.Count > 0)
        {
            Assert.Fail(
                $"D1 TRIPWIRE — {entityType.Name}'s scalar property set changed.\n"
                + (added.Count > 0 ? $"  ADDED (copier now copies these): {string.Join(", ", added)}\n" : "")
                + (removed.Count > 0 ? $"  REMOVED: {string.Join(", ", removed)}\n" : "")
                + $"Decide whether each ADDED field needs a reset rule in JobCloneResetRules "
                + $"(identity/exposure/lifecycle → reset; config → copy is correct as-is), "
                + $"then update {Path.GetFileName(snapshotPath)}.");
        }
    }

    private static string SnapshotDir([CallerFilePath] string sourcePath = "")
        => Path.Combine(Path.GetDirectoryName(sourcePath)!, "Snapshots");

    // ══════════════════════════════════════════════════════════
    // D2 — every job-scoped entity has a disposition
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Entities with a Guid JobId property that the clone deliberately does NOT copy.
    /// Rows in these tables mean the job has been USED — dev-undo's ancillary walk counts
    /// them and refuses to delete when any exist. A new job-scoped table must be added
    /// here (with a reason) or to the clone manifest — the test fails until someone decides.
    /// </summary>
    private static readonly Dictionary<Type, string> NotClonedJobScopedEntities = new()
    {
        [typeof(BracketInstances)] = "Per-season schedule artifact — brackets are rebuilt each season, never cloned.",
        [typeof(CalendarEvents)] = "Runtime calendar content, tied to the season's actual events.",
        [typeof(DeviceJobs)] = "Runtime device linkage (mobile check-in) — devices re-associate per season.",
        [typeof(DivisionProcessingOrder)] = "Scheduling-run working state, meaningless without the season's schedule.",
        [typeof(EmailFailures)] = "Send history.",
        [typeof(EmailLast100)] = "Send history (rolling view/table).",
        [typeof(EmailLogs)] = "Send history.",
        [typeof(EventScheduleDefaults)] = "Scheduling defaults are re-established with each season's schedule build (flagged for future consideration — config-like but historically never cloned).",
        [typeof(GameClockParams)] = "Game-clock config bound to the season's schedule; re-created at schedule time.",
        [typeof(JobAdminCharges)] = "Billing history.",
        [typeof(JobCalendar)] = "Runtime calendar content.",
        [typeof(JobCustomers)] = "Cross-customer linkage rows — job/customer association is minted per job by other flows, never cloned.",
        [typeof(JobDiscountCodes)] = "Per-season codes — carrying them forward would silently re-honor last year's discounts.",
        [typeof(Jobinvoices)] = "Billing history.",
        [typeof(JobMessages)] = "Message history.",
        [typeof(JobPushNotificationsToAll)] = "Broadcast history.",
        [typeof(JobSmsbroadcasts)] = "Broadcast history.",
        [typeof(JobsToPurgeRemainingJobIds)] = "Maintenance/purge working table.",
        [typeof(JobWidget)] = "Legacy dashboard widget rows — dead in the new stack.",
        [typeof(Menus)] = "LEGACY menu system — JobMenus/JobMenuItems is the live one and IS cloned.",
        [typeof(MonthlyJobStats)] = "Stats history.",
        [typeof(PushSubscriptionJobs)] = "Runtime push subscriptions — users re-subscribe on the new job.",
        [typeof(RegForms)] = "DEAD in the new stack — registration forms are metadata JSON on Jobs (which is cloned).",
        [typeof(Schedule)] = "Per-season schedule — never cloned.",
        [typeof(Sliders)] = "Legacy display artifact — JobDisplayOptions is the live surface and IS cloned.",
        [typeof(Stores)] = "Store inventory never clones — StoreChoice only carries the enable flag.",
        [typeof(TeamDocs)] = "Team-uploaded runtime content.",
        [typeof(TeamEvents)] = "Team runtime events.",
        [typeof(VMonthlyJobStats)] = "View (keyless — skipped by the walk anyway).",
        [typeof(VTxs)] = "View (keyless — skipped by the walk anyway).",
        [typeof(Yn2023schedule)] = "Legacy one-off snapshot table.",
    };

    [Fact]
    public void D2_EveryJobScopedEntity_HasADisposition()
    {
        using var ctx = DbContextFactory.Create();

        var undecided = new List<string>();
        foreach (var entityType in ctx.Model.GetEntityTypes())
        {
            if (entityType.FindPrimaryKey() == null) continue;   // keyless views can't hold undo-blocking rows

            var jobIdProp = entityType.FindProperty("JobId");
            if (jobIdProp == null) continue;
            var clr = Nullable.GetUnderlyingType(jobIdProp.ClrType) ?? jobIdProp.ClrType;
            if (clr != typeof(Guid)) continue;

            var isManifest = JobCloneRepository.CloneManifestEntityTypes.Contains(entityType.ClrType);
            var isDocumented = NotClonedJobScopedEntities.ContainsKey(entityType.ClrType);

            if (!isManifest && !isDocumented)
                undecided.Add(entityType.ClrType.Name);
            if (isManifest && isDocumented)
                undecided.Add($"{entityType.ClrType.Name} (BOTH manifest and NotCloned — pick one)");
        }

        undecided.Should().BeEmpty(
            "every entity with a Guid JobId needs a clone disposition: either add it to the "
            + "clone manifest (JobCloneStepOrder + reset rules + repository manifest set) or "
            + "document why it is NOT cloned in NotClonedJobScopedEntities");
    }

    [Fact]
    public void D2_NotClonedList_HasNoStaleEntries()
    {
        using var ctx = DbContextFactory.Create();
        var modelTypes = ctx.Model.GetEntityTypes().Select(e => e.ClrType).ToHashSet();

        var stale = NotClonedJobScopedEntities.Keys
            .Where(t => !modelTypes.Contains(t))
            .Select(t => t.Name)
            .ToList();

        stale.Should().BeEmpty("these NotCloned entries no longer exist in the EF model — remove them");
    }

    // ══════════════════════════════════════════════════════════
    // Manifest sanity
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void Manifest_Steps_CoverEveryStepConstantExactlyOnce()
    {
        var constants = typeof(JobCloneStepOrder)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        JobCloneStepOrder.Steps.Should().OnlyHaveUniqueItems();
        JobCloneStepOrder.Steps.Should().BeEquivalentTo(constants,
            "every step constant must appear in the ordered Steps list exactly once");
    }

    [Fact]
    public void Manifest_RepositoryEntityTypeSet_MatchesClonedEntityTypes()
    {
        // The repository's manifest type set (drives the D2 ancillary walk) and the D1
        // snapshot list (drives copier tripwires) must be the same 18 entities.
        JobCloneRepository.CloneManifestEntityTypes.Should().BeEquivalentTo(ClonedEntityTypes);
    }
}
