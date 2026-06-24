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
/// Render-win #2 correctness net: the lightweight per-recipient load (Registrations + Users + Roles)
/// merged with the once-per-batch job slice (Jobs + Customers + Sports + JobDisplayOptions) must
/// produce token output IDENTICAL to the original full 7-join load. Guards the optimization on the
/// production email-render path against any column drift between the split and the original query.
/// </summary>
public class TextSubstitutionRenderWin2Tests
{
    private const string Subject = "Hello !PERSON";
    private const string Body =
        "!PERSON <!EMAIL>, !ROLENAME for !JOBNAME / !SEASON / !SPORT / !CUSTOMERNAME / code !JOBCODE. You owe !AMTOWED.";

    private static TextSubstitutionService BuildService(Infrastructure.Data.SqlDbContext.SqlDbContext ctx)
    {
        var repo = new TextSubstitutionRepository(ctx);
        return new TextSubstitutionService(
            repo, new Mock<IDiscountCodeEvaluator>().Object, new Mock<IFeeResolutionService>().Object);
    }

    private static async Task<(Infrastructure.Data.SqlDbContext.SqlDbContext ctx, Guid jobId, Guid regId, string familyUserId)>
        SeedAsync()
    {
        var ctx = DbContextFactory.Create();
        var b = new SearchDataBuilder(ctx);

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
        job.JobDescription = "Spring tournament";
        job.MailTo = "mail@x.com";
        job.PayTo = "Acme";
        job.AdnArb = false;
        ctx.JobDisplayOptions.Add(new JobDisplayOptions { JobId = job.JobId, LogoHeader = "logo.png", LebUserId = "seed", Modified = DateTime.UtcNow });

        var role = b.AddRole(RoleConstants.Player, "Player");
        var familyUser = b.AddUser("Fam", "Account", email: "fam@x.com");
        var playerUser = b.AddUser("Jane", "Doe", email: "jane@x.com");
        var reg = b.AddRegistration(job.JobId, playerUser.Id, role.Id, feeTotal: 150m, paidTotal: 50m);
        reg.FamilyUserId = familyUser.Id;

        await b.SaveAsync();
        return (ctx, job.JobId, reg.RegistrationId, familyUser.Id);
    }

    [Fact(DisplayName = "Family path: light (hoisted job slice) renders identically to the full load")]
    public async Task FamilyPath_LightEqualsFull()
    {
        var (ctx, jobId, regId, familyUserId) = await SeedAsync();
        var svc = BuildService(ctx);
        var cc = Guid.NewGuid();

        var full = await svc.SubstituteSubjectAndBodyAsync("seg", jobId, cc, regId, familyUserId, Subject, Body);
        var jobFields = await svc.LoadJobInvariantFieldsAsync(jobId);
        var light = await svc.SubstituteSubjectAndBodyAsync("seg", jobId, cc, regId, familyUserId, Subject, Body, jobFields: jobFields);

        light.Should().Be(full);
        // Sanity: both job-invariant tokens AND per-recipient tokens actually resolved.
        light.Body.Should().Contain("Jane Doe").And.Contain("jane@x.com")
            .And.Contain("Spring Cup").And.Contain("Spring 2026").And.Contain("Lacrosse")
            .And.Contain("Acme Sports").And.Contain("SC26").And.Contain("Player");
    }

    [Fact(DisplayName = "Single-registration path: light renders identically to the full load")]
    public async Task SinglePath_LightEqualsFull()
    {
        var (ctx, jobId, regId, _) = await SeedAsync();
        var svc = BuildService(ctx);
        var cc = Guid.NewGuid();

        // Empty familyUserId -> single-registration load path on both sides.
        var full = await svc.SubstituteSubjectAndBodyAsync("seg", jobId, cc, regId, "", Subject, Body);
        var jobFields = await svc.LoadJobInvariantFieldsAsync(jobId);
        var light = await svc.SubstituteSubjectAndBodyAsync("seg", jobId, cc, regId, "", Subject, Body, jobFields: jobFields);

        light.Should().Be(full);
    }
}
