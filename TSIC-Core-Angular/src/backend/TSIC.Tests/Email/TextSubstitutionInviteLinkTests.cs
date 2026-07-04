using FluentAssertions;
using Moq;
using TSIC.API.Services.Shared.TextSubstitution;
using TSIC.API.Services.Shared.Utilities;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Email;

/// <summary>
/// The invite link (!INVITE_LINK / !CLUBREP_INVITE_LINK) carries a per-recipient signed token whose
/// subject MUST be the LOGIN account the wizard chokepoints validate against (the JWT sub). That
/// account is role-specific and the two identity columns are NOT interchangeable:
///   • Player  — FamilyUserId is the parent who logs in; UserId is the child's own record.
///   • ClubRep — FamilyUserId is EMPTY; UserId IS the login.
///
/// Binding unconditionally to FamilyUserId (the player fix) minted an empty subject for club reps,
/// which failed the canBuildInvite check and rendered the "[…INVITE LINK — target job not configured]"
/// fallback instead of a link. These tests pin the correct role-aware subject selection.
/// </summary>
public class TextSubstitutionInviteLinkTests
{
    private const string ClubRepBody = "Register: !CLUBREP_INVITE_LINK";
    private const string PlayerBody = "Register: !INVITE_LINK";

    private static (TextSubstitutionService svc, List<string> capturedSubjects) BuildService(
        Infrastructure.Data.SqlDbContext.SqlDbContext ctx)
    {
        var captured = new List<string>();
        var tokens = new Mock<TSIC.API.Services.Invites.IInviteTokenService>();
        tokens.Setup(t => t.Create(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>()))
            .Callback<Guid, string, DateTime>((_, invitedUserId, _) => captured.Add(invitedUserId))
            .Returns("TOK-SIGNED");

        var repo = new TextSubstitutionRepository(ctx);
        var svc = new TextSubstitutionService(
            repo, new Mock<IDiscountCodeEvaluator>().Object, new Mock<IFeeResolutionService>().Object,
            tokens.Object,
            Microsoft.Extensions.Options.Options.Create(
                new TSIC.API.Configuration.FrontendSettings { BaseUrl = "https://dev.teamsportsinfo.com" }));
        return (svc, captured);
    }

    private static Jobs SeedJob(SearchDataBuilder b, Infrastructure.Data.SqlDbContext.SqlDbContext ctx)
    {
        var customerId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        ctx.Customers.Add(new Customers { CustomerId = customerId, CustomerName = "Acme Sports" });
        ctx.Sports.Add(new Sports { SportId = sportId, SportName = "Lacrosse" });

        var job = b.AddJob();
        job.CustomerId = customerId;
        job.SportId = sportId;
        job.JobName = "Spring Cup";
        job.Season = "Spring 2026";
        job.JobCode = "SC26";
        ctx.JobDisplayOptions.Add(new JobDisplayOptions
        {
            JobId = job.JobId, LogoHeader = "logo.png", LebUserId = "seed", Modified = DateTime.UtcNow
        });
        return job;
    }

    [Fact(DisplayName = "ClubRep invite (empty FamilyUserId) renders a real link, token bound to UserId")]
    public async Task ClubRep_EmptyFamilyUserId_BindsTokenToUserId_AndRendersLink()
    {
        var ctx = DbContextFactory.Create();
        var b = new SearchDataBuilder(ctx);
        var job = SeedJob(b, ctx);
        var role = b.AddRole(RoleConstants.ClubRep, "Club Rep");
        var clubRepUser = b.AddUser("Tierney", "Ahearn", email: "rep@x.com");

        // Real club-rep shape: FamilyUserId is NEVER set (defaults null). UserId is the login.
        var reg = b.AddRegistration(job.JobId, clubRepUser.Id, role.Id);
        reg.FamilyUserId.Should().BeNull("club-rep registrations carry no FamilyUserId — that's the whole bug");
        await b.SaveAsync();

        var (svc, captured) = BuildService(ctx);
        var jobFields = await svc.LoadJobInvariantFieldsAsync(job.JobId);

        // Batch path: familyUserId="" (template has no !F- token), invite target fully configured.
        var result = await svc.SubstituteSubjectAndBodyAsync(
            "seg", job.JobId, Guid.NewGuid(), reg.RegistrationId, "", "S", ClubRepBody,
            inviteTargetJobPath: "clubcup", inviteTargetJobName: "Club Cup",
            inviteTargetJobId: Guid.NewGuid(), inviteExpires: DateTime.Now.AddHours(24), jobFields: jobFields);

        result.Body.Should().Contain("/clubcup/registration/team?invite=TOK-SIGNED",
            "a valid link must render, not the target-not-configured fallback");
        result.Body.Should().NotContain("target job not configured");
        captured.Should().ContainSingle().Which.Should().Be(clubRepUser.Id,
            "the token subject must be the club rep's UserId — the JWT sub the team wizard validates");
    }

    [Fact(DisplayName = "Player invite still binds the token to FamilyUserId (the parent login)")]
    public async Task Player_BindsTokenToFamilyUserId()
    {
        var ctx = DbContextFactory.Create();
        var b = new SearchDataBuilder(ctx);
        var job = SeedJob(b, ctx);
        var role = b.AddRole(RoleConstants.Player, "Player");
        var parent = b.AddUser("Fam", "Account", email: "fam@x.com");
        var child = b.AddUser("Kid", "Account", email: "kid@x.com");

        var reg = b.AddRegistration(job.JobId, child.Id, role.Id);
        reg.FamilyUserId = parent.Id; // parent logs in and clicks the link
        await b.SaveAsync();

        var (svc, captured) = BuildService(ctx);
        var jobFields = await svc.LoadJobInvariantFieldsAsync(job.JobId);

        var result = await svc.SubstituteSubjectAndBodyAsync(
            "seg", job.JobId, Guid.NewGuid(), reg.RegistrationId, "", "S", PlayerBody,
            inviteTargetJobPath: "clubcup", inviteTargetJobName: "Club Cup",
            inviteTargetJobId: Guid.NewGuid(), inviteExpires: DateTime.Now.AddHours(24), jobFields: jobFields);

        result.Body.Should().Contain("/clubcup/registration/player?invite=TOK-SIGNED");
        captured.Should().ContainSingle().Which.Should().Be(parent.Id,
            "for a player the token subject is the parent (FamilyUserId), not the child's UserId");
    }
}
