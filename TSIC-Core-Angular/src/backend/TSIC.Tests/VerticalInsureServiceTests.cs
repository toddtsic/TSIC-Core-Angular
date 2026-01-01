using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using TSIC.API.Services.Shared.VerticalInsure;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Repositories;
using TSIC.Infrastructure.Data.SqlDbContext;
using Xunit;

namespace TSIC.Tests;

public class VerticalInsureServiceTests
{
    private static SqlDbContext CreateDb(out DbContextOptions<SqlDbContext> opts)
    {
        var name = $"vi-tests-{Guid.NewGuid()}";
        opts = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new SqlDbContext(opts);
    }

    private static VerticalInsureService BuildService(SqlDbContext db, IHostEnvironment env)
    {
        var mockJobRepo = new Mock<IJobRepository>();
        var mockRegRepo = new Mock<IRegistrationRepository>();
        var mockFamilyRepo = new Mock<IFamilyRepository>();
        var logger = new Mock<ILogger<VerticalInsureService>>().Object;
        var teamLookup = new Mock<ITeamLookupService>();
        // Team lookup not used in purchase stub
        return new VerticalInsureService(mockJobRepo.Object, mockRegRepo.Object, mockFamilyRepo.Object, env, logger, teamLookup.Object);
    }

    [Fact]
    public async Task PurchasePolicies_Synthesizes_PolicyNumbers()
    {
        using var db = CreateDb(out _);
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns("Development");

        var jobId = Guid.NewGuid();
        var familyId = Guid.NewGuid().ToString();
        // Seed registrations
        for (int i = 0; i < 3; i++)
        {
            db.Registrations.Add(new TSIC.Domain.Entities.Registrations
            {
                RegistrationId = Guid.NewGuid(),
                JobId = jobId,
                FamilyUserId = familyId,
                UserId = Guid.NewGuid().ToString(),
                FeeTotal = 100,
                OwedTotal = 100,
                PaidTotal = 0,
                BActive = true,
                RegistrationTs = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();

        var regs = await db.Registrations.Where(r => r.JobId == jobId && r.FamilyUserId == familyId).ToListAsync();
        var registrationIds = regs.Select(r => r.RegistrationId).ToList();
        var quoteIds = registrationIds.Select(r => $"Q-{r.ToString("N").Substring(0, 6)}").ToList();

        var svc = BuildService(db, env.Object);
        var res = await svc.PurchasePoliciesAsync(jobId, familyId, registrationIds, quoteIds, token: null, card: null, ct: CancellationToken.None);

        res.Success.Should().BeTrue();
        res.Policies.Count.Should().Be(3);
        foreach (var kv in res.Policies)
        {
            kv.Value.Should().StartWith("POL-");
        }

        // Ensure persistence
        var persisted = await db.Registrations.AsNoTracking().Where(r => r.JobId == jobId).ToListAsync();
        // Use List.TrueForAll for analyzer preference (S6603)
        persisted.TrueForAll(r => !string.IsNullOrWhiteSpace(r.RegsaverPolicyId)).Should().BeTrue();
    }

    [Fact]
    public async Task PurchasePolicies_Fails_On_Count_Mismatch()
    {
        using var db = CreateDb(out _);
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns("Development");

        var jobId = Guid.NewGuid();
        var familyId = Guid.NewGuid().ToString();
        db.Registrations.Add(new TSIC.Domain.Entities.Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = jobId,
            FamilyUserId = familyId,
            UserId = Guid.NewGuid().ToString(),
            FeeTotal = 50,
            OwedTotal = 50,
            PaidTotal = 0,
            BActive = true,
            RegistrationTs = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var reg = await db.Registrations.SingleAsync();
        var svc = BuildService(db, env.Object);
        var res = await svc.PurchasePoliciesAsync(jobId, familyId, new[] { reg.RegistrationId }, new[] { "Q-ONLY", "Q-EXTRA" }, token: null, card: null, ct: CancellationToken.None);

        res.Success.Should().BeFalse();
        res.Error.Should().NotBeNull();
        res.Error!.Should().ContainEquivalentOf("mismatch");
        (await db.Registrations.AsNoTracking().SingleAsync()).RegsaverPolicyId.Should().BeNull();
    }
}