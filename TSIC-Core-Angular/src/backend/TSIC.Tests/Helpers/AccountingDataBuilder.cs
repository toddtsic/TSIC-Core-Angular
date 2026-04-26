using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Tests.Helpers;

/// <summary>
/// Seeds test data for accounting / payment tests.
///
/// Usage:
///   var ctx = DbContextFactory.Create();
///   var b = new AccountingDataBuilder(ctx);
///   var job = b.AddJob(processingFeePercent: 3.5m, bAddProcessingFees: true);
///   var reg = b.AddPlayerRegistration(job.JobId, feeBase: 100, feeProcessing: 3.50m);
///   await b.SaveAsync();
/// </summary>
public class AccountingDataBuilder
{
    private readonly SqlDbContext _ctx;

    // Well-known payment method IDs (match production seed data)
    public static readonly Guid CcPaymentMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D");
    public static readonly Guid CcCreditMethodId = Guid.Parse("31ECA575-A268-E111-9D56-F04DA202060D");
    public static readonly Guid CheckMethodId = Guid.Parse("32ECA575-A268-E111-9D56-F04DA202060D");
    public static readonly Guid CorrectionMethodId = Guid.Parse("33ECA575-A268-E111-9D56-F04DA202060D");
    public static readonly Guid EcheckMethodId = Guid.Parse("2EECA575-A268-E111-9D56-F04DA202060D");

    public AccountingDataBuilder(SqlDbContext ctx)
    {
        _ctx = ctx;
        SeedPaymentMethods();
    }

    /// <summary>Seed the standard payment methods (required for joins in accounting queries).</summary>
    private void SeedPaymentMethods()
    {
        _ctx.AccountingPaymentMethods.AddRange(
            new AccountingPaymentMethods { PaymentMethodId = CcPaymentMethodId, PaymentMethod = "Credit Card Payment By Client", Modified = DateTime.UtcNow },
            new AccountingPaymentMethods { PaymentMethodId = CcCreditMethodId, PaymentMethod = "Credit Card Credit", Modified = DateTime.UtcNow },
            new AccountingPaymentMethods { PaymentMethodId = CheckMethodId, PaymentMethod = "Check Payment By Client", Modified = DateTime.UtcNow },
            new AccountingPaymentMethods { PaymentMethodId = CorrectionMethodId, PaymentMethod = "Correction", Modified = DateTime.UtcNow },
            new AccountingPaymentMethods { PaymentMethodId = EcheckMethodId, PaymentMethod = "E-Check Payment", Modified = DateTime.UtcNow }
        );
    }

    // ── Job ──

    public Jobs AddJob(
        decimal? processingFeePercent = null,
        bool bAddProcessingFees = false,
        bool bApplyProcessingFeesToTeamDeposit = false,
        bool bTeamsFullPaymentRequired = false,
        bool bPlayersFullPaymentRequired = false)
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
            BApplyProcessingFeesToTeamDeposit = bApplyProcessingFeesToTeamDeposit,
            BTeamsFullPaymentRequired = bTeamsFullPaymentRequired,
            BPlayersFullPaymentRequired = bPlayersFullPaymentRequired,
            Modified = DateTime.UtcNow
        };
        _ctx.Jobs.Add(job);
        return job;
    }

    // ── Customer (for invoice generation) ──

    public Customers AddCustomer(int customerAi = 1)
    {
        var customer = new Customers
        {
            CustomerId = Guid.NewGuid(),
            CustomerAi = customerAi,
            Modified = DateTime.UtcNow
        };
        _ctx.Customers.Add(customer);
        return customer;
    }

    // ── League + Agegroup (for team tests) ──

    public Leagues AddLeague(Guid jobId)
    {
        var league = new Leagues
        {
            LeagueId = Guid.NewGuid(),
            SportId = Guid.NewGuid(),
            Modified = DateTime.UtcNow
        };
        _ctx.Leagues.Add(league);

        // Link league to job via JobLeagues junction
        _ctx.JobLeagues.Add(new JobLeagues
        {
            JobId = jobId,
            LeagueId = league.LeagueId,
            Modified = DateTime.UtcNow
        });
        return league;
    }

    public Agegroups AddAgegroup(Guid leagueId, string name = "2027 AA",
        decimal rosterFee = 0, decimal teamFee = 0)
    {
        var ag = new Agegroups
        {
            AgegroupId = Guid.NewGuid(),
            LeagueId = leagueId,
            AgegroupName = name,
            RosterFee = rosterFee,
            TeamFee = teamFee,
            Modified = DateTime.UtcNow
        };
        _ctx.Agegroups.Add(ag);
        return ag;
    }

    public Divisions AddDivision(Guid agegroupId, string name = "Unassigned")
    {
        var div = new Divisions
        {
            DivId = Guid.NewGuid(),
            AgegroupId = agegroupId,
            DivName = name,
            Modified = DateTime.UtcNow
        };
        _ctx.Divisions.Add(div);
        return div;
    }

    // ── Player Registration (for Level 1a tests) ──

    public Registrations AddPlayerRegistration(
        Guid jobId,
        decimal feeBase = 100m,
        decimal feeProcessing = 0m,
        decimal feeDiscount = 0m,
        decimal paidTotal = 0m)
    {
        var feeTotal = feeBase + feeProcessing - feeDiscount;
        var reg = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = jobId,
            UserId = Guid.NewGuid().ToString(),
            FeeBase = feeBase,
            FeeProcessing = feeProcessing,
            FeeDiscount = feeDiscount,
            FeeDonation = 0,
            FeeLatefee = 0,
            FeeTotal = feeTotal,
            PaidTotal = paidTotal,
            OwedTotal = feeTotal - paidTotal,
            BActive = true,
            Modified = DateTime.UtcNow
        };
        _ctx.Registrations.Add(reg);
        return reg;
    }

    // ── Club Rep Registration + Teams (for Level 1b / Level 2 tests) ──

    public Registrations AddClubRepRegistration(
        Guid jobId,
        string clubName = "Test Club",
        decimal feeBase = 0m,
        decimal feeProcessing = 0m,
        decimal feeTotal = 0m,
        decimal paidTotal = 0m,
        decimal owedTotal = 0m)
    {
        var reg = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = jobId,
            UserId = Guid.NewGuid().ToString(),
            ClubName = clubName,
            FeeBase = feeBase,
            FeeProcessing = feeProcessing,
            FeeDiscount = 0,
            FeeDonation = 0,
            FeeLatefee = 0,
            FeeTotal = feeTotal,
            PaidTotal = paidTotal,
            OwedTotal = owedTotal,
            BActive = true,
            Modified = DateTime.UtcNow
        };
        _ctx.Registrations.Add(reg);
        return reg;
    }

    public Teams AddTeam(
        Guid jobId,
        Guid agegroupId,
        Guid? clubRepRegistrationId = null,
        string teamName = "Test Team",
        decimal feeBase = 500m,
        decimal feeProcessing = 0m,
        decimal feeDiscount = 0m,
        decimal paidTotal = 0m,
        bool active = true,
        Guid? divId = null)
    {
        var feeTotal = feeBase + feeProcessing - feeDiscount;
        var team = new Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = jobId,
            AgegroupId = agegroupId,
            DivId = divId,
            ClubrepRegistrationid = clubRepRegistrationId,
            TeamName = teamName,
            FeeBase = feeBase,
            FeeProcessing = feeProcessing,
            FeeDiscount = feeDiscount,
            FeeDonation = 0,
            FeeLatefee = 0,
            FeeTotal = feeTotal,
            PaidTotal = paidTotal,
            OwedTotal = feeTotal - paidTotal,
            Active = active,
            Modified = DateTime.UtcNow
        };
        _ctx.Teams.Add(team);
        return team;
    }

    public async Task SaveAsync() => await _ctx.SaveChangesAsync();
}
