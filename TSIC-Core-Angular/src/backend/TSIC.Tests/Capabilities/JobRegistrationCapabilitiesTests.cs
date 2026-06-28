using FluentAssertions;
using Moq;
using TSIC.API.Services;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.Tests.Capabilities;

/// <summary>
/// CAPABILITY-AUTHORITY MATRIX TESTS
///
/// The authority composes one answer to "may this actor CREATE registration data right now?"
/// from <c>door(actor) AND toggle(c) AND precondition(c)</c>. These tests pin the full matrix:
///
///   • door — eventConcluded (incl. the lftc-summer-2025 shape: EventEndDate past, ExpiryUsers
///     future, no schedule — the case bare-ExpiryUsers let through) + supersession
///   • toggle — director BAllow* flags; ADMIN is exempt
///   • precondition — fees configured / teams exist; binds EVEN admins
///   • fail-closed — unknown job → everything denied
///
/// The authority reads <c>DateTime.Now</c> internally, so dates are expressed relative to today.
/// </summary>
public class JobRegistrationCapabilitiesTests
{
    private static readonly Guid JobId = Guid.NewGuid();

    private static readonly DateTime FarFuture = DateTime.Today.AddYears(1);
    private static readonly DateTime FutureDay = DateTime.Today.AddDays(30);
    private static readonly DateTime PastDay = DateTime.Today.AddDays(-30);

    /// <summary>A healthy, live, pre-event job: nothing concluded, every toggle on, every
    /// precondition met. Each test overrides only the facts it exercises.</summary>
    private static JobCapabilityFacts Healthy() => new()
    {
        SchedulePublished = false,
        LastGameDate = null,
        EventEndDate = FutureDay,
        ExpiryUsers = FarFuture,
        SupersededByLaterEvent = false,
        AllowPlayer = true,
        AllowTeam = true,
        AllowStaff = true,
        AllowReferee = true,
        AllowRecruiter = true,
        ClubRepAllowAdd = true,
        ClubRepAllowEdit = true,
        ClubRepAllowDelete = true,
        PlayerFeesConfigured = true,
        ClubRepFeesConfigured = true,
        TeamsExist = true,
    };

    private static IJobRegistrationCapabilities Authority(JobCapabilityFacts? facts)
    {
        var jobs = new Mock<IJobRepository>();
        jobs.Setup(j => j.GetCapabilityFactsAsync(JobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);
        return new JobRegistrationCapabilities(jobs.Object);
    }

    private static Task<JobCapabilitySet> Resolve(JobCapabilityFacts? facts, CapabilityActor actor) =>
        Authority(facts).ResolveAsync(JobId, actor);

    // ── Happy path ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "Live pre-event, all toggles on, fees+teams present → user can create everything")]
    public async Task LivePreEvent_User_AllOpen()
    {
        var set = await Resolve(Healthy(), CapabilityActor.User);

        set.CanRegisterPlayer.Should().BeTrue();
        set.CanRegisterStaff.Should().BeTrue();
        set.CanRegisterReferee.Should().BeTrue();
        set.CanRegisterRecruiter.Should().BeTrue();
        set.CanAddTeam.Should().BeTrue();
        set.CanRemoveTeam.Should().BeTrue();
        set.CanEditTeam.Should().BeTrue();
    }

    // ── The door: eventConcluded ────────────────────────────────────────────

    [Fact(DisplayName = "lftc shape (EventEndDate past, ExpiryUsers future, no schedule) → USER frozen on every create")]
    public async Task LftcShape_User_AllCreateDenied()
    {
        // The exact leak bare-ExpiryUsers missed: the event ended last year but the generous
        // user window is still open, and a stale toggle would otherwise resurrect registration.
        var facts = Healthy() with { EventEndDate = PastDay, ExpiryUsers = FarFuture, SchedulePublished = false };

        var set = await Resolve(facts, CapabilityActor.User);

        set.CanRegisterPlayer.Should().BeFalse();
        set.CanRegisterStaff.Should().BeFalse();
        set.CanRegisterReferee.Should().BeFalse();
        set.CanRegisterRecruiter.Should().BeFalse();
        set.CanAddTeam.Should().BeFalse();
        set.CanRemoveTeam.Should().BeFalse();
        // The concluded door is the higher-level gate: it removes edit even with ClubRepAllowEdit on.
        set.CanEditTeam.Should().BeFalse();
    }

    [Fact(DisplayName = "lftc shape → ADMIN unaffected (session proves ExpiryAdmin; preconditions still met)")]
    public async Task LftcShape_Admin_StillOpen()
    {
        var facts = Healthy() with { EventEndDate = PastDay, ExpiryUsers = FarFuture };

        var set = await Resolve(facts, CapabilityActor.Admin);

        set.CanRegisterPlayer.Should().BeTrue();
        set.CanAddTeam.Should().BeTrue();
        set.CanRegisterStaff.Should().BeTrue();
    }

    [Fact(DisplayName = "Published schedule, last game day PAST → user concluded (date signal beats toggles)")]
    public async Task SchedulePast_User_Concluded()
    {
        var facts = Healthy() with { SchedulePublished = true, LastGameDate = PastDay, EventEndDate = FutureDay };

        var set = await Resolve(facts, CapabilityActor.User);

        set.CanRegisterPlayer.Should().BeFalse();
        set.CanAddTeam.Should().BeFalse();
    }

    [Fact(DisplayName = "Published schedule, last game day FUTURE → not concluded even if EventEndDate is past (schedule wins)")]
    public async Task ScheduleFuture_BeatsEventEndDate_NotConcluded()
    {
        var facts = Healthy() with { SchedulePublished = true, LastGameDate = FutureDay, EventEndDate = PastDay };

        var set = await Resolve(facts, CapabilityActor.User);

        set.CanRegisterPlayer.Should().BeTrue();
    }

    [Fact(DisplayName = "No date signal at all (generous future ExpiryUsers) → not concluded; toggles/preconditions decide")]
    public async Task NoDateSignal_NotConcluded()
    {
        var facts = Healthy() with { SchedulePublished = false, LastGameDate = null, EventEndDate = null, ExpiryUsers = FarFuture };

        var set = await Resolve(facts, CapabilityActor.User);

        set.CanRegisterPlayer.Should().BeTrue();
    }

    [Fact(DisplayName = "No date signal, ExpiryUsers itself past → last-resort fallback concludes")]
    public async Task NoDateSignal_ExpiryPast_Concluded()
    {
        var facts = Healthy() with { SchedulePublished = false, LastGameDate = null, EventEndDate = null, ExpiryUsers = PastDay };

        var set = await Resolve(facts, CapabilityActor.User);

        set.CanRegisterPlayer.Should().BeFalse();
    }

    // ── The door: supersession ──────────────────────────────────────────────

    [Fact(DisplayName = "Superseded by a live later-year sibling → user frozen even though not concluded")]
    public async Task Superseded_User_Denied()
    {
        var facts = Healthy() with { SupersededByLaterEvent = true };

        var set = await Resolve(facts, CapabilityActor.User);

        set.CanRegisterPlayer.Should().BeFalse();
        set.CanAddTeam.Should().BeFalse();
    }

    [Fact(DisplayName = "Superseded → ADMIN unaffected (door is structurally true for admins)")]
    public async Task Superseded_Admin_Open()
    {
        var facts = Healthy() with { SupersededByLaterEvent = true };

        var set = await Resolve(facts, CapabilityActor.Admin);

        set.CanRegisterPlayer.Should().BeTrue();
        set.CanAddTeam.Should().BeTrue();
    }

    // ── Toggles (admin-exempt) ──────────────────────────────────────────────

    [Fact(DisplayName = "All toggles OFF → user denied every create")]
    public async Task TogglesOff_User_Denied()
    {
        var facts = Healthy() with
        {
            AllowPlayer = false,
            AllowTeam = false,
            AllowStaff = false,
            AllowReferee = false,
            AllowRecruiter = false,
            ClubRepAllowAdd = false,
            ClubRepAllowDelete = false,
        };

        var set = await Resolve(facts, CapabilityActor.User);

        set.CanRegisterPlayer.Should().BeFalse();
        set.CanRegisterStaff.Should().BeFalse();
        set.CanRegisterReferee.Should().BeFalse();
        set.CanRegisterRecruiter.Should().BeFalse();
        set.CanAddTeam.Should().BeFalse();
        set.CanRemoveTeam.Should().BeFalse();
    }

    [Fact(DisplayName = "All toggles OFF → ADMIN still allowed (exempt from toggles, preconditions met)")]
    public async Task TogglesOff_Admin_Allowed()
    {
        var facts = Healthy() with
        {
            AllowPlayer = false,
            AllowTeam = false,
            AllowStaff = false,
            AllowReferee = false,
            AllowRecruiter = false,
            ClubRepAllowAdd = false,
            ClubRepAllowDelete = false,
        };

        var set = await Resolve(facts, CapabilityActor.Admin);

        set.CanRegisterPlayer.Should().BeTrue();
        set.CanRegisterStaff.Should().BeTrue();
        set.CanRegisterReferee.Should().BeTrue();
        set.CanRegisterRecruiter.Should().BeTrue();
        set.CanAddTeam.Should().BeTrue();
        set.CanRemoveTeam.Should().BeTrue();
    }

    [Fact(DisplayName = "Per-surface toggle isolation: only ClubRepAllowDelete off → add allowed, remove denied")]
    public async Task DeleteToggleOff_AddSurvives_RemoveDenied()
    {
        var facts = Healthy() with { ClubRepAllowDelete = false };

        var set = await Resolve(facts, CapabilityActor.User);

        set.CanAddTeam.Should().BeTrue();
        set.CanRemoveTeam.Should().BeFalse();
    }

    [Fact(DisplayName = "Per-surface toggle isolation: only ClubRepAllowEdit off → add/remove allowed, edit denied")]
    public async Task EditToggleOff_AddRemoveSurvive_EditDenied()
    {
        var facts = Healthy() with { ClubRepAllowEdit = false };

        var set = await Resolve(facts, CapabilityActor.User);

        set.CanAddTeam.Should().BeTrue();
        set.CanRemoveTeam.Should().BeTrue();
        set.CanEditTeam.Should().BeFalse();
    }

    // ── Preconditions (bind even admins) ────────────────────────────────────

    [Fact(DisplayName = "No player fee row → EVEN ADMIN cannot register player (precondition binds)")]
    public async Task NoPlayerFees_Admin_Denied()
    {
        var facts = Healthy() with { PlayerFeesConfigured = false };

        var set = await Resolve(facts, CapabilityActor.Admin);

        set.CanRegisterPlayer.Should().BeFalse();
        set.CanAddTeam.Should().BeTrue(); // unrelated precondition intact
    }

    [Fact(DisplayName = "No clubRep fee row → EVEN ADMIN cannot add team; removal still allowed (no fee precondition)")]
    public async Task NoClubRepFees_Admin_AddDenied_RemoveAllowed()
    {
        var facts = Healthy() with { ClubRepFeesConfigured = false };

        var set = await Resolve(facts, CapabilityActor.Admin);

        set.CanAddTeam.Should().BeFalse();
        set.CanRemoveTeam.Should().BeTrue();
    }

    [Fact(DisplayName = "No teams exist → EVEN ADMIN cannot register staff (coach needs a team); referee/recruiter unaffected")]
    public async Task NoTeams_Admin_StaffDenied_OthersOpen()
    {
        var facts = Healthy() with { TeamsExist = false };

        var set = await Resolve(facts, CapabilityActor.Admin);

        set.CanRegisterStaff.Should().BeFalse();
        set.CanRegisterReferee.Should().BeTrue();
        set.CanRegisterRecruiter.Should().BeTrue();
    }

    // ── Fail-closed ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "Unknown job (null facts) → everything denied for USER")]
    public async Task UnknownJob_User_AllDenied()
    {
        var set = await Resolve(null, CapabilityActor.User);

        set.CanRegisterPlayer.Should().BeFalse();
        set.CanRegisterStaff.Should().BeFalse();
        set.CanRegisterReferee.Should().BeFalse();
        set.CanRegisterRecruiter.Should().BeFalse();
        set.CanAddTeam.Should().BeFalse();
        set.CanRemoveTeam.Should().BeFalse();
    }

    [Fact(DisplayName = "Unknown job (null facts) → everything denied even for ADMIN (fail closed)")]
    public async Task UnknownJob_Admin_AllDenied()
    {
        var set = await Resolve(null, CapabilityActor.Admin);

        set.CanRegisterPlayer.Should().BeFalse();
        set.CanAddTeam.Should().BeFalse();
        set.CanRemoveTeam.Should().BeFalse();
    }
}
