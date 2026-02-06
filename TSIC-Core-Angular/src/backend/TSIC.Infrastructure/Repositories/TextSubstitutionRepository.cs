using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Implementation of text substitution repository.
/// </summary>
public sealed class TextSubstitutionRepository : ITextSubstitutionRepository
{
    private readonly SqlDbContext _context;

    public TextSubstitutionRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<JobTokenInfo?> GetJobTokenInfoAsync(string jobPath, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobPath == jobPath)
            .Select(j => new JobTokenInfo
            {
                JobName = j.JobName ?? string.Empty,
                UslaxNumberValidThroughDate = j.UslaxNumberValidThroughDate
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<FixedFieldsData>> LoadFixedFieldsByRegistrationAsync(Guid registrationId, CancellationToken cancellationToken = default)
    {
        return await (from r in _context.Registrations
                      join u in _context.AspNetUsers on r.UserId equals u.Id
                      join roles in _context.AspNetRoles on r.RoleId equals roles.Id
                      join j in _context.Jobs on r.JobId equals j.JobId
                      join jdo in _context.JobDisplayOptions on j.JobId equals jdo.JobId
                      join c in _context.Customers on j.CustomerId equals c.CustomerId
                      join s in _context.Sports on j.SportId equals s.SportId
                      where r.RegistrationId == registrationId
                      select new FixedFieldsData
                      {
                          RegistrationId = r.RegistrationId,
                          JobId = j.JobId,
                          FamilyUserId = r.FamilyUserId,
                          Person = u.FirstName + " " + u.LastName,
                          Assignment = r.Assignment,
                          UserName = u.UserName,
                          FeeTotal = r.FeeTotal,
                          PaidTotal = r.PaidTotal,
                          OwedTotal = r.OwedTotal,
                          RegistrationCategory = r.RegistrationCategory,
                          ClubName = r.ClubName,
                          CustomerName = c.CustomerName,
                          Email = u.Email,
                          JobDescription = j.JobDescription,
                          JobName = j.JobName ?? string.Empty,
                          JobPath = j.JobPath,
                          MailTo = j.MailTo,
                          PayTo = j.PayTo,
                          RoleName = roles.Name,
                          Season = j.Season,
                          SportName = s.SportName,
                          AssignedTeamId = r.AssignedTeamId,
                          Active = r.BActive,
                          Volposition = r.Volposition,
                          UniformNo = r.UniformNo,
                          DayGroup = r.DayGroup,
                          JerseySize = r.JerseySize ?? "?",
                          ShortsSize = r.ShortsSize ?? "?",
                          TShirtSize = r.TShirt ?? "?",
                          AdnArb = j.AdnArb ?? false,
                          AdnSubscriptionId = r.AdnSubscriptionId,
                          AdnSubscriptionStatus = r.AdnSubscriptionStatus,
                          AdnSubscriptionBillingOccurences = r.AdnSubscriptionBillingOccurences,
                          AdnSubscriptionAmountPerOccurence = r.AdnSubscriptionAmountPerOccurence,
                          AdnSubscriptionStartDate = r.AdnSubscriptionStartDate,
                          AdnSubscriptionIntervalLength = r.AdnSubscriptionIntervalLength,
                          JobLogoHeader = jdo.LogoHeader,
                          JobCode = j.JobCode ?? "?",
                          UslaxNumberValidThroughDate = j.UslaxNumberValidThroughDate
                      }).ToListAsync(cancellationToken);
    }

    public async Task<List<FixedFieldsData>> LoadFixedFieldsByFamilyAsync(Guid jobId, string familyUserId, CancellationToken cancellationToken = default)
    {
        return await (from r in _context.Registrations
                      join u in _context.AspNetUsers on r.UserId equals u.Id
                      join roles in _context.AspNetRoles on r.RoleId equals roles.Id
                      join j in _context.Jobs on r.JobId equals j.JobId
                      join jdo in _context.JobDisplayOptions on j.JobId equals jdo.JobId
                      join c in _context.Customers on j.CustomerId equals c.CustomerId
                      join s in _context.Sports on j.SportId equals s.SportId
                      where r.JobId == jobId && r.FamilyUserId == familyUserId
                      orderby r.RegistrationAi
                      select new FixedFieldsData
                      {
                          RegistrationId = r.RegistrationId,
                          JobId = j.JobId,
                          FamilyUserId = r.FamilyUserId,
                          Person = u.FirstName + " " + u.LastName,
                          Assignment = r.Assignment,
                          UserName = u.UserName,
                          FeeTotal = r.FeeTotal,
                          PaidTotal = r.PaidTotal,
                          OwedTotal = r.OwedTotal,
                          RegistrationCategory = r.RegistrationCategory,
                          ClubName = r.ClubName,
                          CustomerName = c.CustomerName,
                          Email = u.Email,
                          JobDescription = j.JobDescription,
                          JobName = j.JobName ?? string.Empty,
                          JobPath = j.JobPath,
                          MailTo = j.MailTo,
                          PayTo = j.PayTo,
                          RoleName = roles.Name,
                          Season = j.Season,
                          SportName = s.SportName,
                          AssignedTeamId = r.AssignedTeamId,
                          Active = r.BActive,
                          Volposition = r.Volposition,
                          UniformNo = r.UniformNo,
                          DayGroup = r.DayGroup,
                          JerseySize = r.JerseySize ?? "?",
                          ShortsSize = r.ShortsSize ?? "?",
                          TShirtSize = r.TShirt ?? "?",
                          AdnArb = j.AdnArb ?? false,
                          AdnSubscriptionId = r.AdnSubscriptionId,
                          AdnSubscriptionStatus = r.AdnSubscriptionStatus,
                          AdnSubscriptionBillingOccurences = r.AdnSubscriptionBillingOccurences,
                          AdnSubscriptionAmountPerOccurence = r.AdnSubscriptionAmountPerOccurence,
                          AdnSubscriptionStartDate = r.AdnSubscriptionStartDate,
                          AdnSubscriptionIntervalLength = r.AdnSubscriptionIntervalLength,
                          JobLogoHeader = jdo.LogoHeader,
                          JobCode = j.JobCode ?? "?",
                          UslaxNumberValidThroughDate = j.UslaxNumberValidThroughDate
                      }).ToListAsync(cancellationToken);
    }

    public async Task<List<AccountingTransactionRow>> GetAccountingTransactionsAsync(Guid registrationId, CancellationToken cancellationToken = default)
    {
        return await (from ra in _context.RegistrationAccounting
                      join r in _context.Registrations on ra.RegistrationId equals r.RegistrationId
                      join u in _context.AspNetUsers on r.UserId equals u.Id
                      join pm in _context.AccountingPaymentMethods on ra.PaymentMethodId equals pm.PaymentMethodId
                      where ra.RegistrationId == registrationId && ra.Active == true
                      orderby ra.AId
                      select new AccountingTransactionRow
                      {
                          AId = ra.AId,
                          RegistrantName = u.FirstName + " " + u.LastName,
                          PaymentMethod = pm.PaymentMethod,
                          Createdate = ra.Createdate,
                          Payamt = ra.Payamt,
                          Dueamt = ra.Dueamt,
                          DiscountCodeAi = ra.DiscountCodeAi,
                          PaymentMethodId = ra.PaymentMethodId,
                          Comment = ra.Comment
                      }).ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<Guid, string>> GetTeamClubNamesAsync(List<Guid> registrationIds, CancellationToken cancellationToken = default)
    {
        var results = await (from r in _context.Registrations
                             join t in _context.Teams on r.AssignedTeamId equals t.TeamId
                             join rCR in _context.Registrations on t.ClubrepRegistrationid equals rCR.RegistrationId
                             where registrationIds.Contains(r.RegistrationId)
                             select new { r.RegistrationId, rCR.ClubName }).ToListAsync(cancellationToken);
        return results.ToDictionary(x => x.RegistrationId, x => x.ClubName ?? string.Empty);
    }

    public async Task<DirectorContactData?> GetDirectorContactAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await (from r in _context.Registrations
                      join roles in _context.AspNetRoles on r.RoleId equals roles.Id
                      join u in _context.AspNetUsers on r.UserId equals u.Id
                      where r.JobId == jobId && r.BActive == true && roles.Name == "Director"
                      orderby r.RegistrationTs
                      select new DirectorContactData
                      {
                          Name = u.FirstName + " " + u.LastName,
                          Email = u.Email ?? string.Empty
                      }).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> GetFamilyUserNameAsync(string familyUserId, CancellationToken cancellationToken = default)
    {
        return await _context.AspNetUsers
            .Where(u => u.Id == familyUserId)
            .Select(u => u.UserName)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> GetTeamNameWithClubAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        var result = await (from t in _context.Teams
                            join j in _context.Jobs on t.JobId equals j.JobId
                            join c in _context.Customers on j.CustomerId equals c.CustomerId
                            where t.TeamId == teamId
                            select new { Team = t.TeamFullName ?? t.TeamName, Club = c.CustomerName }).SingleOrDefaultAsync(cancellationToken);
        if (result == null) return null;
        return $"{result.Club}:{result.Team}";
    }

    public async Task<string?> GetAgeGroupPlusTeamNameAsync(Guid registrationId, CancellationToken cancellationToken = default)
    {
        var result = await (from r in _context.Registrations
                            join t in _context.Teams on r.AssignedTeamId equals t.TeamId
                            join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                            where r.RegistrationId == registrationId
                            select new { ag.AgegroupName, t.TeamName }).SingleOrDefaultAsync(cancellationToken);
        if (result == null) return null;
        return $"{result.AgegroupName}:{result.TeamName}";
    }

    public async Task<string?> GetAgeGroupNameAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await (from t in _context.Teams
                      join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                      where t.TeamId == teamId
                      select ag.AgegroupName).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> GetLeagueNameAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await (from t in _context.Teams
                      join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                      join l in _context.Leagues on ag.LeagueId equals l.LeagueId
                      where t.TeamId == teamId
                      select l.LeagueName).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> GetClubNameAsync(Guid registrationId, CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .Where(r => r.RegistrationId == registrationId)
            .Select(r => r.ClubName)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<List<ClubTeamInfo>> GetClubTeamsAsync(Guid clubRepRegistrationId, CancellationToken cancellationToken = default)
    {
        return await (from t in _context.Teams
                      join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                      where t.ClubrepRegistrationid == clubRepRegistrationId
                            && ag.AgegroupName != "Dropped Teams"
                            && t.TeamName != "Club Teams"
                      select new ClubTeamInfo
                      {
                          TeamId = t.TeamId,
                          TeamName = ag.AgegroupName + " " + t.TeamName
                      }).ToListAsync(cancellationToken);
    }

    public async Task<List<TeamAccountingRow>> GetTeamAccountingTransactionsAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await (from ra in _context.RegistrationAccounting
                      join tm in _context.Teams on ra.TeamId equals tm.TeamId
                      join r in _context.Registrations on tm.ClubrepRegistrationid equals r.RegistrationId
                      join u in _context.AspNetUsers on r.UserId equals u.Id
                      join pm in _context.AccountingPaymentMethods on ra.PaymentMethodId equals pm.PaymentMethodId
                      where ra.TeamId == teamId
                      orderby ra.AId
                      select new TeamAccountingRow
                      {
                          Active = ra.Active,
                          AId = ra.AId,
                          PaymentMethod = pm.PaymentMethod,
                          Dueamt = ra.Dueamt,
                          Payamt = ra.Payamt,
                          Createdate = ra.Createdate,
                          Comment = ra.Comment,
                          DiscountCodeAi = ra.DiscountCodeAi,
                          PaymentMethodId = ra.PaymentMethodId
                      }).ToListAsync(cancellationToken);
    }

    public async Task<List<TeamSummaryRow>> GetTeamsSummaryAsync(Guid clubRepRegistrationId, CancellationToken cancellationToken = default)
    {
        return await (from t in _context.Teams
                      join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                      join r in _context.Registrations on t.ClubrepRegistrationid equals r.RegistrationId
                      where t.ClubrepRegistrationid == clubRepRegistrationId
                            && ag.AgegroupName != "Dropped Teams"
                            && t.TeamName != "Club Teams"
                      select new TeamSummaryRow
                      {
                          TeamName = ag.AgegroupName + " " + t.TeamName,
                          FeeTotal = t.FeeTotal,
                          PaidTotal = t.PaidTotal,
                          OwedTotal = t.OwedTotal,
                          Dow = t.Dow,
                          ProcessingFees = t.FeeProcessing,
                          RosterFee = ag.RosterFee,
                          AdditionalFees = ag.TeamFee,
                          ClubName = r.ClubName
                      }).ToListAsync(cancellationToken);
    }

    public async Task<List<SimpleTeamRow>> GetSimpleTeamsAsync(Guid clubRepRegistrationId, CancellationToken cancellationToken = default)
    {
        return await (from t in _context.Teams
                      join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                      join r in _context.Registrations on t.ClubrepRegistrationid equals r.RegistrationId
                      where t.ClubrepRegistrationid == clubRepRegistrationId
                      select new SimpleTeamRow
                      {
                          TeamName = ag.AgegroupName + " " + t.TeamName,
                          ClubName = r.ClubName
                      }).ToListAsync(cancellationToken);
    }

    public async Task<JobWaiverData?> GetJobWaiversAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .Where(j => j.JobId == jobId)
            .Select(j => new JobWaiverData
            {
                PlayerRegRefundPolicy = j.PlayerRegRefundPolicy,
                PlayerRegReleaseOfLiability = j.PlayerRegReleaseOfLiability,
                AdultRegReleaseOfLiability = j.AdultRegReleaseOfLiability,
                PlayerRegCodeOfConduct = j.PlayerRegCodeOfConduct,
                PlayerRegCovid19Waiver = j.PlayerRegCovid19Waiver
            }).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<StaffRegistrationInfo?> GetStaffInfoAsync(Guid registrationId, CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .Where(r => r.RegistrationId == registrationId)
            .Select(r => new StaffRegistrationInfo
            {
                UserId = r.UserId,
                JobId = r.JobId,
                SpecialRequests = r.SpecialRequests
            }).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<List<CoachTeamChoice>> GetCoachTeamChoicesAsync(Guid registrationId, CancellationToken cancellationToken = default)
    {
        var keys = await _context.Registrations
            .Where(r => r.RegistrationId == registrationId)
            .Select(r => new { r.UserId, r.JobId })
            .SingleOrDefaultAsync(cancellationToken);

        if (keys == null) return new List<CoachTeamChoice>();

        return await (from r in _context.Registrations
                      join t in _context.Teams on r.AssignedTeamId equals t.TeamId
                      join rCR in _context.Registrations on t.ClubrepRegistrationid equals rCR.RegistrationId
                      join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                      join roles in _context.AspNetRoles on r.RoleId equals roles.Id
                      where r.JobId == keys.JobId && r.UserId == keys.UserId && roles.Name == "Staff"
                      orderby ag.AgegroupName, t.TeamName
                      select new CoachTeamChoice
                      {
                          Club = rCR.ClubName,
                          Age = ag.AgegroupName,
                          Team = t.TeamName
                      }).ToListAsync(cancellationToken);
    }
}
