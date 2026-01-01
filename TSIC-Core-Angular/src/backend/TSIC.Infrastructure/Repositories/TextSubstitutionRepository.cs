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
            .Select(j => new JobTokenInfo(j.JobName ?? string.Empty, j.UslaxNumberValidThroughDate))
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
                      select new FixedFieldsData(
                          r.RegistrationId,
                          j.JobId,
                          r.FamilyUserId,
                          u.FirstName + " " + u.LastName,
                          r.Assignment,
                          u.UserName,
                          r.FeeTotal,
                          r.PaidTotal,
                          r.OwedTotal,
                          r.RegistrationCategory,
                          r.ClubName,
                          c.CustomerName,
                          u.Email,
                          j.JobDescription,
                          j.JobName ?? string.Empty,
                          j.JobPath,
                          j.MailTo,
                          j.PayTo,
                          roles.Name,
                          j.Season,
                          s.SportName,
                          r.AssignedTeamId,
                          r.BActive,
                          r.Volposition,
                          r.UniformNo,
                          r.DayGroup,
                          r.JerseySize ?? "?",
                          r.ShortsSize ?? "?",
                          r.TShirt ?? "?",
                          j.AdnArb ?? false,
                          r.AdnSubscriptionId,
                          r.AdnSubscriptionStatus,
                          r.AdnSubscriptionBillingOccurences,
                          r.AdnSubscriptionAmountPerOccurence,
                          r.AdnSubscriptionStartDate,
                          r.AdnSubscriptionIntervalLength,
                          jdo.LogoHeader,
                          j.JobCode ?? "?",
                          j.UslaxNumberValidThroughDate
                      )).ToListAsync(cancellationToken);
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
                      select new FixedFieldsData(
                          r.RegistrationId,
                          j.JobId,
                          r.FamilyUserId,
                          u.FirstName + " " + u.LastName,
                          r.Assignment,
                          u.UserName,
                          r.FeeTotal,
                          r.PaidTotal,
                          r.OwedTotal,
                          r.RegistrationCategory,
                          r.ClubName,
                          c.CustomerName,
                          u.Email,
                          j.JobDescription,
                          j.JobName ?? string.Empty,
                          j.JobPath,
                          j.MailTo,
                          j.PayTo,
                          roles.Name,
                          j.Season,
                          s.SportName,
                          r.AssignedTeamId,
                          r.BActive,
                          r.Volposition,
                          r.UniformNo,
                          r.DayGroup,
                          r.JerseySize ?? "?",
                          r.ShortsSize ?? "?",
                          r.TShirt ?? "?",
                          j.AdnArb ?? false,
                          r.AdnSubscriptionId,
                          r.AdnSubscriptionStatus,
                          r.AdnSubscriptionBillingOccurences,
                          r.AdnSubscriptionAmountPerOccurence,
                          r.AdnSubscriptionStartDate,
                          r.AdnSubscriptionIntervalLength,
                          jdo.LogoHeader,
                          j.JobCode ?? "?",
                          j.UslaxNumberValidThroughDate
                      )).ToListAsync(cancellationToken);
    }

    public async Task<List<AccountingTransactionRow>> GetAccountingTransactionsAsync(Guid registrationId, CancellationToken cancellationToken = default)
    {
        return await (from ra in _context.RegistrationAccounting
                      join r in _context.Registrations on ra.RegistrationId equals r.RegistrationId
                      join u in _context.AspNetUsers on r.UserId equals u.Id
                      join pm in _context.AccountingPaymentMethods on ra.PaymentMethodId equals pm.PaymentMethodId
                      where ra.RegistrationId == registrationId && ra.Active == true
                      orderby ra.AId
                      select new AccountingTransactionRow(
                          ra.AId,
                          u.FirstName + " " + u.LastName,
                          pm.PaymentMethod,
                          ra.Createdate,
                          ra.Payamt,
                          ra.Dueamt,
                          ra.DiscountCodeAi,
                          ra.PaymentMethodId,
                          ra.Comment
                      )).ToListAsync(cancellationToken);
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
                      select new DirectorContactData(
                          u.FirstName + " " + u.LastName,
                          u.Email ?? string.Empty
                      )).FirstOrDefaultAsync(cancellationToken);
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
                      select new ClubTeamInfo(
                          t.TeamId,
                          ag.AgegroupName + " " + t.TeamName
                      )).ToListAsync(cancellationToken);
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
                      select new TeamAccountingRow(
                          ra.Active,
                          ra.AId,
                          pm.PaymentMethod,
                          ra.Dueamt,
                          ra.Payamt,
                          ra.Createdate,
                          ra.Comment,
                          ra.DiscountCodeAi,
                          ra.PaymentMethodId
                      )).ToListAsync(cancellationToken);
    }

    public async Task<List<TeamSummaryRow>> GetTeamsSummaryAsync(Guid clubRepRegistrationId, CancellationToken cancellationToken = default)
    {
        return await (from t in _context.Teams
                      join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                      join r in _context.Registrations on t.ClubrepRegistrationid equals r.RegistrationId
                      where t.ClubrepRegistrationid == clubRepRegistrationId
                            && ag.AgegroupName != "Dropped Teams"
                            && t.TeamName != "Club Teams"
                      select new TeamSummaryRow(
                          ag.AgegroupName + " " + t.TeamName,
                          t.FeeTotal,
                          t.PaidTotal,
                          t.OwedTotal,
                          t.Dow,
                          t.FeeProcessing,
                          ag.RosterFee,
                          ag.TeamFee,
                          r.ClubName
                      )).ToListAsync(cancellationToken);
    }

    public async Task<List<SimpleTeamRow>> GetSimpleTeamsAsync(Guid clubRepRegistrationId, CancellationToken cancellationToken = default)
    {
        return await (from t in _context.Teams
                      join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                      join r in _context.Registrations on t.ClubrepRegistrationid equals r.RegistrationId
                      where t.ClubrepRegistrationid == clubRepRegistrationId
                      select new SimpleTeamRow(
                          ag.AgegroupName + " " + t.TeamName,
                          r.ClubName
                      )).ToListAsync(cancellationToken);
    }

    public async Task<JobWaiverData?> GetJobWaiversAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .Where(j => j.JobId == jobId)
            .Select(j => new JobWaiverData(
                j.PlayerRegRefundPolicy,
                j.PlayerRegReleaseOfLiability,
                j.AdultRegReleaseOfLiability,
                j.PlayerRegCodeOfConduct,
                j.PlayerRegCovid19Waiver
            )).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<StaffRegistrationInfo?> GetStaffInfoAsync(Guid registrationId, CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .Where(r => r.RegistrationId == registrationId)
            .Select(r => new StaffRegistrationInfo(
                r.UserId,
                r.JobId,
                r.SpecialRequests
            )).SingleOrDefaultAsync(cancellationToken);
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
                      select new CoachTeamChoice(
                          rCR.ClubName,
                          ag.AgegroupName,
                          t.TeamName
                      )).ToListAsync(cancellationToken);
    }
}
