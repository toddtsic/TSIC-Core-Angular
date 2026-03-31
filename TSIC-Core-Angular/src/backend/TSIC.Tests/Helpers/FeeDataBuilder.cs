using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Tests.Helpers;

/// <summary>
/// Seeds test data for fee modifier tests (early bird discounts, late fees).
///
/// Usage:
///   var ctx = DbContextFactory.Create();
///   var b = new FeeDataBuilder(ctx);
///   var job = b.AddJob();
///   var ag = b.AddAgegroup(job.JobId, "U12");
///   var team = b.AddTeam(job.JobId, ag.AgegroupId, "Hawks");
///   var jobFee = b.AddJobFee(job.JobId, RoleConstants.Player, balanceDue: 200m);
///   b.AddModifier(jobFee.JobFeeId, "EarlyBird", 25m,
///       startDate: new DateTime(2026, 1, 1), endDate: new DateTime(2026, 2, 15));
///   await b.SaveAsync();
/// </summary>
public class FeeDataBuilder
{
    private readonly SqlDbContext _ctx;

    public FeeDataBuilder(SqlDbContext ctx)
    {
        _ctx = ctx;
    }

    // ── Job ──

    public Jobs AddJob(
        decimal? processingFeePercent = 3.5m,
        bool bAddProcessingFees = true)
    {
        var job = new Jobs
        {
            JobId = Guid.NewGuid(),
            JobPath = $"test-job-{Guid.NewGuid():N}"[..20],
            RegformNamePlayer = "Player",
            RegformNameTeam = "Team",
            RegformNameCoach = "Coach",
            RegformNameClubRep = "Club Rep",
            ProcessingFeePercent = processingFeePercent,
            BAddProcessingFees = bAddProcessingFees,
            Modified = DateTime.UtcNow
        };
        _ctx.Jobs.Add(job);
        return job;
    }

    // ── Agegroup ──

    /// <summary>
    /// Creates an agegroup entity. Requires a LeagueId (Agegroups belong to Leagues, not Jobs directly).
    /// For fee tests, you can pass any GUID — the fee cascade only uses AgegroupId as a key in JobFees.
    /// </summary>
    public Agegroups AddAgegroup(Guid leagueId, string name = "U12")
    {
        var ag = new Agegroups
        {
            AgegroupId = Guid.NewGuid(),
            LeagueId = leagueId,
            AgegroupName = name,
            Modified = DateTime.UtcNow
        };
        _ctx.Agegroups.Add(ag);
        return ag;
    }

    // ── League (needed to satisfy Agegroup FK) ──

    public Leagues AddLeague(Guid jobId)
    {
        var league = new Leagues
        {
            LeagueId = Guid.NewGuid(),
            Modified = DateTime.UtcNow
        };
        _ctx.Leagues.Add(league);
        _ctx.JobLeagues.Add(new JobLeagues
        {
            JobId = jobId,
            LeagueId = league.LeagueId,
            Modified = DateTime.UtcNow
        });
        return league;
    }

    // ── Team ──

    public Teams AddTeam(Guid jobId, Guid agegroupId, string name = "Hawks")
    {
        var team = new Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = jobId,
            AgegroupId = agegroupId,
            TeamName = name,
            Modified = DateTime.UtcNow
        };
        _ctx.Teams.Add(team);
        return team;
    }

    // ── JobFees ──

    /// <summary>
    /// Adds a fee row at the specified scope.
    /// Job-level: agegroupId=null, teamId=null.
    /// Agegroup-level: teamId=null.
    /// Team-level: both set.
    /// </summary>
    public JobFees AddJobFee(
        Guid jobId,
        string roleId,
        Guid? agegroupId = null,
        Guid? teamId = null,
        decimal? deposit = null,
        decimal? balanceDue = null)
    {
        var jf = new JobFees
        {
            JobFeeId = Guid.NewGuid(),
            JobId = jobId,
            RoleId = roleId,
            AgegroupId = agegroupId,
            TeamId = teamId,
            Deposit = deposit,
            BalanceDue = balanceDue,
            Modified = DateTime.UtcNow
        };
        _ctx.JobFees.Add(jf);
        return jf;
    }

    // ── Fee Modifiers ──

    /// <summary>
    /// Adds a fee modifier (EarlyBird, LateFee, or Discount).
    /// NULL dates = unbounded (always active).
    /// </summary>
    public FeeModifiers AddModifier(
        Guid jobFeeId,
        string modifierType,
        decimal amount,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var mod = new FeeModifiers
        {
            FeeModifierId = Guid.NewGuid(),
            JobFeeId = jobFeeId,
            ModifierType = modifierType,
            Amount = amount,
            StartDate = startDate,
            EndDate = endDate,
            Modified = DateTime.UtcNow
        };
        _ctx.FeeModifiers.Add(mod);
        return mod;
    }

    // ── Registration (for swap tests) ──

    public Registrations AddRegistration(
        Guid jobId,
        Guid teamId,
        decimal feeBase = 0m,
        decimal feeDiscount = 0m,
        decimal feeLatefee = 0m,
        decimal feeProcessing = 0m,
        decimal feeDonation = 0m,
        decimal paidTotal = 0m)
    {
        var feeTotal = feeBase + feeProcessing - feeDiscount + feeDonation + feeLatefee;

        var reg = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = jobId,
            UserId = $"user-{Guid.NewGuid():N}"[..20],
            FamilyUserId = $"family-{Guid.NewGuid():N}"[..20],
            RoleId = RoleConstants.Player,
            AssignedTeamId = teamId,
            FeeBase = feeBase,
            FeeDiscount = feeDiscount,
            FeeLatefee = feeLatefee,
            FeeProcessing = feeProcessing,
            FeeDonation = feeDonation,
            FeeTotal = feeTotal,
            PaidTotal = paidTotal,
            OwedTotal = feeTotal - paidTotal,
            BActive = true,
            Modified = DateTime.UtcNow
        };
        _ctx.Registrations.Add(reg);
        return reg;
    }

    public async Task SaveAsync() => await _ctx.SaveChangesAsync();
}
