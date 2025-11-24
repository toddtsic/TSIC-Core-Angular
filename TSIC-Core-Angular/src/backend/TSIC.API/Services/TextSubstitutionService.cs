using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.API.Services;

public interface ITextSubstitutionService
{
    Task<string> SubstituteAsync(
        string jobSegment,
        Guid paymentMethodCreditCardId,
        Guid? registrationId,
        string familyUserId,
        string template);
}

/// <summary>
/// Rewritten version of legacy TextSubstitutionService.
/// Responsibilities:
///  * Load fixed field data either for a single registration or an entire family for a job.
///  * Build dynamic HTML fragments for special tokens (!F-PLAYERS, !F-ACCOUNTING, etc.).
///  * Replace tokens in the input template.
/// Design goals:
///  * Keep EF queries localized (small, intention-revealing methods).
///  * Avoid deeply nested loops; prefer LINQ + helper builders.
///  * Make adding new tokens simple via the token handler dictionary.
/// </summary>
public sealed class TextSubstitutionService : ITextSubstitutionService
{
    // Internal projection representing many fixed fields needed for substitution.
    private sealed class FixedFields
    {
        public Guid RegistrationId { get; init; }
        public Guid JobId { get; init; }
        public string? FamilyUserId { get; init; }
        public string? Person { get; init; }
        public string? Assignment { get; init; }
        public string? UserName { get; init; }
        public decimal? FeeTotal { get; init; }
        public decimal? PaidTotal { get; init; }
        public decimal? OwedTotal { get; init; }
        public string? RegistrationCategory { get; init; }
        public string? ClubName { get; init; }
        public string? CustomerName { get; init; }
        public string? Email { get; init; }
        public string? JobDescription { get; init; }
        public string JobName { get; init; } = string.Empty;
        public string JobPath { get; init; } = string.Empty;
        public string? MailTo { get; init; }
        public string? PayTo { get; init; }
        public string? RoleName { get; init; }
        public string? Season { get; init; }
        public string? SportName { get; init; }
        public Guid? AssignedTeamId { get; init; }
        public bool? Active { get; init; }
        public string? Volposition { get; init; }
        public string? UniformNo { get; init; }
        public string? DayGroup { get; init; }
        public string? JerseySize { get; init; }
        public string? ShortsSize { get; init; }
        public string? TShirtSize { get; init; }
        public bool AdnArb { get; init; }
        public string? AdnSubscriptionId { get; init; }
        public string? AdnSubscriptionStatus { get; init; }
        public int? AdnSubscriptionBillingOccurences { get; init; }
        public decimal? AdnSubscriptionAmountPerOccurence { get; init; }
        public DateTime? AdnSubscriptionStartDate { get; init; }
        public int? AdnSubscriptionIntervalLength { get; init; }
        public string? JobLogoHeader { get; init; }
        public string? JobCode { get; init; }
        public DateTime? UslaxNumberValidThroughDate { get; init; }
    }

    private readonly SqlDbContext _context;
    private readonly IDiscountCodeEvaluator _discountEvaluator;

    public TextSubstitutionService(SqlDbContext context, IDiscountCodeEvaluator discountEvaluator)
    {
        _context = context;
        _discountEvaluator = discountEvaluator;
    }

    public async Task<string> SubstituteAsync(
        string jobSegment,
        Guid paymentMethodCreditCardId,
        Guid? registrationId,
        string familyUserId,
        string template)
    {
        if (string.IsNullOrWhiteSpace(template)) return template;

        // Detect email mode token. If present, strip it and render inline email-safe tables.
        var emailMode = template.Contains("!EMAILMODE", StringComparison.OrdinalIgnoreCase);
        if (emailMode)
        {
            template = template.Replace("!EMAILMODE", string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        var job = await _context.Jobs.AsNoTracking().Where(j => j.JobPath == jobSegment)
            .Select(j => new { j.JobId, j.JobName }).SingleOrDefaultAsync();
        if (job == null) return template; // Unknown job segment

        var fixedFieldList = await LoadFixedFieldsAsync(_context, job.JobId, registrationId, familyUserId);
        if (fixedFieldList.Count == 0) return template; // No data to substitute

        var first = fixedFieldList[0];

        // Build token dictionary (simple tokens + complex HTML sections)
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddSimpleTokens(tokens, first, jobSegment);
        await AddComplexTokensAsync(tokens, fixedFieldList, paymentMethodCreditCardId, registrationId, template, emailMode);

        // Perform replacements
        var result = ReplaceTokens(template, tokens);
        return result;
    }

    private static string ReplaceTokens(string template, Dictionary<string, string> tokens)
    {
        var sb = new StringBuilder(template);
        foreach (var kvp in tokens)
        {
            sb.Replace(kvp.Key, kvp.Value);
        }
        return sb.ToString();
    }

    private async Task<List<FixedFields>> LoadFixedFieldsAsync(SqlDbContext ctx, Guid jobId, Guid? registrationId, string familyUserId)
    {
        // Path A: single registration (familyUserId may be empty)
        if (string.IsNullOrEmpty(familyUserId) && registrationId.HasValue)
        {
            return await (from r in ctx.Registrations
                          join u in ctx.AspNetUsers on r.UserId equals u.Id
                          join roles in ctx.AspNetRoles on r.RoleId equals roles.Id
                          join j in ctx.Jobs on r.JobId equals j.JobId
                          join jdo in ctx.JobDisplayOptions on j.JobId equals jdo.JobId
                          join c in ctx.Customers on j.CustomerId equals c.CustomerId
                          join s in ctx.Sports on j.SportId equals s.SportId
                          where r.RegistrationId == registrationId
                          select new FixedFields
                          {
                              RegistrationId = r.RegistrationId,
                              JobId = j.JobId,
                              FamilyUserId = r.FamilyUserId,
                              Person = u.FirstName + " " + u.LastName,
                              UserName = u.UserName,
                              Assignment = r.Assignment,
                              FeeTotal = r.FeeTotal,
                              OwedTotal = r.OwedTotal,
                              PaidTotal = r.PaidTotal,
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
                          }).ToListAsync();
        }

        // Path B: family across job
        return await (from r in ctx.Registrations
                      join u in ctx.AspNetUsers on r.UserId equals u.Id
                      join roles in ctx.AspNetRoles on r.RoleId equals roles.Id
                      join j in ctx.Jobs on r.JobId equals j.JobId
                      join jdo in ctx.JobDisplayOptions on j.JobId equals jdo.JobId
                      join c in ctx.Customers on j.CustomerId equals c.CustomerId
                      join s in ctx.Sports on j.SportId equals s.SportId
                      where r.JobId == jobId && r.FamilyUserId == familyUserId
                      orderby r.RegistrationAi
                      select new FixedFields
                      {
                          RegistrationId = r.RegistrationId,
                          JobId = j.JobId,
                          FamilyUserId = r.FamilyUserId,
                          Person = u.FirstName + " " + u.LastName,
                          UserName = u.UserName,
                          Assignment = r.Assignment,
                          FeeTotal = r.FeeTotal,
                          OwedTotal = r.OwedTotal,
                          PaidTotal = r.PaidTotal,
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
                      }).ToListAsync();
    }

    private static void AddSimpleTokens(Dictionary<string, string> tokens, FixedFields f, string jobSegment)
    {
        string Img(string? logo) => string.IsNullOrEmpty(logo) ? string.Empty : $"<img src='https://statics.teamsportsinfo.com/BannerFiles/{logo}' alt='Job Logo'>";
        tokens["!JSEG"] = jobSegment;
        tokens["!JOBNAME"] = f.JobName ?? string.Empty;
        tokens["!JOBCODE"] = f.JobCode ?? string.Empty;
        tokens["!JOBPATH"] = f.JobPath ?? string.Empty;
        tokens["!JOBURL"] = $"https://www.teamsportsinfo.com/{f.JobPath}/home";
        tokens["!JOBDESCRIPTION"] = f.JobDescription ?? string.Empty;
        tokens["!JOBLOGO"] = Img(f.JobLogoHeader);
        tokens["!PAYTO"] = f.PayTo ?? string.Empty;
        tokens["!MAILTO"] = f.MailTo ?? string.Empty;
        tokens["!CUSTOMERNAME"] = f.CustomerName ?? f.ClubName ?? string.Empty;
        tokens["!CLUBNAME"] = f.ClubName ?? string.Empty;
        tokens["!ROLENAME"] = f.RoleName ?? string.Empty;
        tokens["!PERSON"] = f.Person ?? string.Empty;
        tokens["!USERNAME"] = f.UserName ?? string.Empty;
        tokens["!TRAININGGROUP"] = f.DayGroup ?? string.Empty;
        tokens["!EMAIL"] = f.Email ?? string.Empty;
        tokens["!AMTFEES"] = f.FeeTotal?.ToString("C") ?? "$0.00";
        tokens["!AMTPAID"] = f.PaidTotal?.ToString("C") ?? "$0.00";
        tokens["!AMTOWED"] = f.OwedTotal?.ToString("C") ?? "$0.00";
        tokens["!REGISTRANT_ISACTIVEORDROPPED"] = (f.Active == true) ? "ACTIVE" : "INACTIVE";
        tokens["!VOLPOSITION"] = f.Volposition ?? string.Empty;
        tokens["!UNIFORM_NO"] = f.UniformNo ?? string.Empty;
        tokens["!JERSEY_SIZE"] = f.JerseySize ?? "?";
        tokens["!SHORTS_SIZE"] = f.ShortsSize ?? "?";
        tokens["!TSHIRT_SIZE"] = f.TShirtSize ?? "?";
        tokens["!SEASON"] = f.Season ?? string.Empty;
        tokens["!SPORT"] = f.SportName ?? string.Empty;
        tokens["!APPLIEDTOCATEGORY"] = f.RegistrationCategory ?? string.Empty;
        tokens["!YEAR"] = f.UslaxNumberValidThroughDate?.Year.ToString() ?? DateTime.UtcNow.Year.ToString();
        tokens["!USLAXVALIDTHROUGHDATE"] = f.UslaxNumberValidThroughDate?.ToString("d") ?? string.Empty;
    }

    private async Task AddComplexTokensAsync(
        Dictionary<string, string> tokens,
        List<FixedFields> list,
        Guid paymentMethodCreditCardId,
        Guid? registrationId,
        string template,
        bool emailMode)
    {
        if (template.Contains("!F-DISPLAYINACTIVEPLAYERS", StringComparison.OrdinalIgnoreCase))
            tokens["!F-DISPLAYINACTIVEPLAYERS"] = BuildInactivePlayersHtml(list);

        if (template.Contains("!F-PLAYERS", StringComparison.OrdinalIgnoreCase))
            tokens["!F-PLAYERS"] = BuildPlayersTableHtml(list, emailMode);

        if (template.Contains("!F-ADN-ARB", StringComparison.OrdinalIgnoreCase))
            tokens["!F-ADN-ARB"] = BuildArbTableHtml(list, emailMode);

        if (template.Contains("!F-ACCOUNTING", StringComparison.OrdinalIgnoreCase))
        {
            if (registrationId.HasValue)
            {
                tokens["!F-ACCOUNTING"] = await BuildAccountingTableHtmlAsync(registrationId.Value, paymentMethodCreditCardId, emailMode);
            }
            else
            {
                // Ensure token removed from output when no registration context
                tokens["!F-ACCOUNTING"] = string.Empty;
            }
        }

        // Newly added tokens to match production parity
        var first = list[0];

        if (template.Contains("!F-NOACCOUNTINGPLAYERS", StringComparison.OrdinalIgnoreCase))
            tokens["!F-NOACCOUNTINGPLAYERS"] = await BuildNoAccountingPlayersAsync(list, emailMode);

        if (template.Contains("!J-CONTACTBLOCK", StringComparison.OrdinalIgnoreCase))
            tokens["!J-CONTACTBLOCK"] = await BuildContactBlockAsync(first.JobId, emailMode);

        if (template.Contains("!FAMILYUSERNAME", StringComparison.OrdinalIgnoreCase))
            tokens["!FAMILYUSERNAME"] = await ResolveFamilyUserNameAsync(first.FamilyUserId);

        if (template.Contains("!TEAMNAME", StringComparison.OrdinalIgnoreCase))
            tokens["!TEAMNAME"] = await ResolveTeamNameAsync(first.AssignedTeamId);

        if (template.Contains("!AGNPLUSTN", StringComparison.OrdinalIgnoreCase) && registrationId.HasValue)
            tokens["!AGNPLUSTN"] = await ResolveAgeGroupPlusTeamNameAsync(registrationId.Value);

        if (template.Contains("!AGEGROUPNAME", StringComparison.OrdinalIgnoreCase))
            tokens["!AGEGROUPNAME"] = await ResolveAgeGroupNameAsync(first.AssignedTeamId);

        if (template.Contains("!LEAGUENAME", StringComparison.OrdinalIgnoreCase))
            tokens["!LEAGUENAME"] = await ResolveLeagueNameAsync(first.AssignedTeamId);

        if (template.Contains("!A-ACCOUNTING", StringComparison.OrdinalIgnoreCase) && registrationId.HasValue)
            tokens["!A-ACCOUNTING"] = await BuildAccountingAHtmlAsync(registrationId.Value, paymentMethodCreditCardId, first.JobName, emailMode);

        if (template.Contains("!F-ACCOUNTING-TEAMS", StringComparison.OrdinalIgnoreCase) && registrationId.HasValue)
            tokens["!F-ACCOUNTING-TEAMS"] = await BuildAccountingTeamsHtmlAsync(registrationId.Value, paymentMethodCreditCardId, emailMode);

        if (template.Contains("!F-TEAMS", StringComparison.OrdinalIgnoreCase) && registrationId.HasValue)
            tokens["!F-TEAMS"] = await BuildTeamsSummaryHtmlAsync(registrationId.Value, emailMode);

        if (template.Contains("!F-NO-MONEY-TEAMS", StringComparison.OrdinalIgnoreCase) && registrationId.HasValue)
            tokens["!F-NO-MONEY-TEAMS"] = await BuildNoMoneyTeamsHtmlAsync(registrationId.Value, emailMode);

        if (template.Contains("!F-REFUND-PLAYER-WAIVER", StringComparison.OrdinalIgnoreCase))
            tokens["!F-REFUND-PLAYER-WAIVER"] = await BuildWaiverHtmlAsync(first.JobId, first.JobName, first.CustomerName, j => j.PlayerRegRefundPolicy, "Refund Policy:");

        if (template.Contains("!F-WAIVER-PLAYER", StringComparison.OrdinalIgnoreCase))
            tokens["!F-WAIVER-PLAYER"] = await BuildWaiverHtmlAsync(first.JobId, first.JobName, first.CustomerName, j => j.PlayerRegReleaseOfLiability, "Waiver:");

        if (template.Contains("!F-WAIVER-ADULT", StringComparison.OrdinalIgnoreCase))
            tokens["!F-WAIVER-ADULT"] = await BuildWaiverHtmlAsync(first.JobId, first.JobName, first.CustomerName, j => j.AdultRegReleaseOfLiability, "Waiver:");

        if (template.Contains("!F-COC-WAIVER-PLAYER", StringComparison.OrdinalIgnoreCase))
            tokens["!F-COC-WAIVER-PLAYER"] = await BuildWaiverHtmlAsync(first.JobId, first.JobName, first.CustomerName, j => j.PlayerRegCodeOfConduct, "Code of Conduct:");

        if (template.Contains("!F-COVID-WAIVER-PLAYER", StringComparison.OrdinalIgnoreCase))
            tokens["!F-COVID-WAIVER-PLAYER"] = await BuildWaiverHtmlAsync(first.JobId, first.JobName, first.CustomerName, j => j.PlayerRegCovid19Waiver, "COVID-19 Waiver:");

        if (template.Contains("!F-STAFFCHOICES", StringComparison.OrdinalIgnoreCase) && registrationId.HasValue)
            tokens["!F-STAFFCHOICES"] = await BuildStaffChoicesAsync(registrationId.Value);

        if (template.Contains("!F-COACHFULLTEAMNAMECHOICES", StringComparison.OrdinalIgnoreCase) && registrationId.HasValue)
            tokens["!F-COACHFULLTEAMNAMECHOICES"] = await BuildCoachFullTeamNameChoicesAsync(registrationId.Value, emailMode);
    }

    private static string BuildInactivePlayersHtml(List<FixedFields> list)
    {
        var inactive = list.Where(q => q.Active != true && string.IsNullOrEmpty(q.AdnSubscriptionId)).ToList();
        if (inactive.Count == 0) return string.Empty;
        var html = new StringBuilder()
            .Append("<div style='padding:4px;border:1px solid black;background-color:yellow;font-weight:bold;font-size:large'>")
            .Append("<p>The following players are INACTIVE and are considered NOT REGISTERED</p><ul>");
        foreach (var i in inactive)
            html.Append($"<li>{WebUtility.HtmlEncode(i.Person)} ({WebUtility.HtmlEncode(i.Assignment)})</li>");
        html.Append("</ul><p>The player(s) above will be considered registered ONLY after they are PAID IN FULL.</p>")
            .Append("<p>Unpaid players are subject to being dropped by the program director.</p></div>");
        return html.ToString();
    }

    private static string BuildPlayersTableHtml(List<FixedFields> list, bool emailMode)
    {
        if (list.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        StartTable(sb, emailMode);
        AddCaption(sb, "Family Players", emailMode);
        StartHead(sb);
        AddHeaderRow(sb, "Player", "Status", "Assignment", "Fees$", "Paid$", "Owes$");
        EndHeadStartBody(sb);
        decimal feesSum = 0m, paidSum = 0m, owesSum = 0m;
        foreach (var q in list)
        {
            var status = (string.IsNullOrEmpty(q.AdnSubscriptionId) && q.Active != true) ? "INACTIVE" : "ACTIVE";
            var fees = q.FeeTotal ?? 0m;
            var paid = q.PaidTotal ?? 0m;
            var owes = q.OwedTotal ?? 0m;
            feesSum += fees; paidSum += paid; owesSum += owes;
            AddRow(sb,
                WebUtility.HtmlEncode(q.Person ?? string.Empty),
                status,
                WebUtility.HtmlEncode(q.Assignment ?? string.Empty),
                FormatCurrency(fees),
                FormatCurrency(paid),
                FormatCurrency(owes));
        }
        EndBodyStartFoot(sb);
        AddFooterRow(sb, "Totals", string.Empty, string.Empty, FormatCurrency(feesSum), FormatCurrency(paidSum), FormatCurrency(owesSum));
        EndFootEndTable(sb);
        return sb.ToString();
    }

    private static string BuildArbTableHtml(List<FixedFields> list, bool emailMode)
    {
        if (list.Count == 0) return string.Empty;
        var first = list[0];
        if (first.AdnSubscriptionAmountPerOccurence is not > 0) return string.Empty;
        var sb = new StringBuilder();
        StartTable(sb, emailMode);
        AddCaption(sb, "Automated Recurring Billing", emailMode);
        StartHead(sb);
        AddHeaderRow(sb, "Player", "Sub. Id", "Status", "Starting", "#Billings", "Frequency", "Charge/Billing", "Total Charges");
        EndHeadStartBody(sb);
        decimal totalAll = 0m;
        foreach (var q in list)
        {
            var intervalLabel = (q.AdnSubscriptionIntervalLength ?? 0) > 1 ? "months" : "month";
            var totalCharges = (q.AdnSubscriptionAmountPerOccurence ?? 0m) * (q.AdnSubscriptionBillingOccurences ?? 0);
            totalAll += totalCharges;
            AddRow(sb,
                WebUtility.HtmlEncode(q.Person ?? string.Empty),
                q.AdnSubscriptionId ?? string.Empty,
                q.AdnSubscriptionStatus ?? string.Empty,
                q.AdnSubscriptionStartDate?.ToString("d") ?? string.Empty,
                (q.AdnSubscriptionBillingOccurences ?? 0).ToString(),
                $"every {q.AdnSubscriptionIntervalLength} {intervalLabel}",
                FormatCurrency(q.AdnSubscriptionAmountPerOccurence ?? 0m),
                FormatCurrency(totalCharges));
        }
        EndBodyStartFoot(sb);
        AddFooterRow(sb, "Total", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, FormatCurrency(totalAll));
        EndFootEndTable(sb);
        return sb.ToString();
    }

    private async Task<string> BuildAccountingTableHtmlAsync(Guid registrationId, Guid paymentMethodCreditCardId, bool emailMode)
    {
        var rows = await (from ra in _context.RegistrationAccounting
                          join r in _context.Registrations on ra.RegistrationId equals r.RegistrationId
                          join u in _context.AspNetUsers on r.UserId equals u.Id
                          join pm in _context.AccountingPaymentMethods on ra.PaymentMethodId equals pm.PaymentMethodId
                          where ra.RegistrationId == registrationId && ra.Active == true
                          orderby ra.AId
                          select new
                          {
                              ra.AId,
                              RegistrantName = u.FirstName + " " + u.LastName,
                              pm.PaymentMethod,
                              ra.Createdate,
                              ra.Payamt,
                              ra.DiscountCodeAi,
                              ra.PaymentMethodId
                          }).ToListAsync();
        if (rows.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        StartTable(sb, emailMode);
        AddCaption(sb, "Most Recent Transaction(s)", emailMode);
        StartHead(sb);
        AddHeaderRow(sb, "ID", "Player", "Method", "Date", "Paid$");
        EndHeadStartBody(sb);
        decimal paidSum = 0m;
        foreach (var row in rows)
        {
            if (row.Payamt.HasValue && row.Payamt > 0 && row.PaymentMethodId == paymentMethodCreditCardId && row.DiscountCodeAi.HasValue)
            {
                _ = await _discountEvaluator.EvaluateAsync(row.DiscountCodeAi.Value, row.Payamt.Value);
            }
            var paid = row.Payamt ?? 0m; paidSum += paid;
            AddRow(sb,
                row.AId.ToString(),
                WebUtility.HtmlEncode(row.RegistrantName ?? string.Empty),
                row.PaymentMethod ?? string.Empty,
                row.Createdate?.ToString("g") ?? string.Empty,
                FormatCurrency(paid));
        }
        EndBodyStartFoot(sb);
        AddFooterRow(sb, "Total", string.Empty, string.Empty, string.Empty, FormatCurrency(paidSum));
        EndFootEndTable(sb);
        return sb.ToString();
    }

    private async Task<string> BuildNoAccountingPlayersAsync(List<FixedFields> list, bool emailMode)
    {
        if (list.Count == 0) return string.Empty;
        var regIds = list.Select(x => x.RegistrationId).ToList();
        var teamClubNames = await (from r in _context.Registrations
                                   join t in _context.Teams on r.AssignedTeamId equals t.TeamId
                                   join rCR in _context.Registrations on t.ClubrepRegistrationid equals rCR.RegistrationId
                                   where regIds.Contains(r.RegistrationId)
                                   select new { r.RegistrationId, rCR.ClubName }).ToListAsync();
        var clubByReg = teamClubNames.ToDictionary(x => x.RegistrationId, x => x.ClubName);
        var sb = new StringBuilder();
        StartTable(sb, emailMode);
        AddCaption(sb, "Registered Family Players", emailMode);
        StartHead(sb);
        AddHeaderRow(sb, "Player", "Status", "Assignment");
        EndHeadStartBody(sb);
        foreach (var q in list)
        {
            var assignment = q.Assignment ?? string.Empty;
            if (clubByReg.TryGetValue(q.RegistrationId, out var club) && !string.IsNullOrEmpty(club))
            {
                assignment = $"{club}:{assignment}";
            }
            var status = (q.Active != true) ? "INACTIVE" : "ACTIVE";
            AddRow(sb,
                WebUtility.HtmlEncode(q.Person ?? string.Empty),
                status,
                WebUtility.HtmlEncode(assignment));
        }
        EndBodyOnly(sb);
        EndTableOnly(sb);
        return sb.ToString();
    }

    private async Task<string> BuildContactBlockAsync(Guid jobId, bool emailMode)
    {
        var director = await (from r in _context.Registrations
                              join roles in _context.AspNetRoles on r.RoleId equals roles.Id
                              join u in _context.AspNetUsers on r.UserId equals u.Id
                              where r.JobId == jobId && r.BActive == true && roles.Name == "Director"
                              orderby r.RegistrationTs
                              select new { Name = u.FirstName + " " + u.LastName, u.Email }).FirstOrDefaultAsync();
        if (director == null) return string.Empty;
        var sb = new StringBuilder();
        StartTable(sb, emailMode);
        AddCaption(sb, "Contacts", emailMode);
        StartHead(sb);
        AddHeaderRow(sb, "Role", "Contact");
        EndHeadStartBody(sb);
        AddRow(sb, "Main Contact", $"<a href='mailto:{director.Email}'>{WebUtility.HtmlEncode(director.Name)}: {director.Email}</a>");
        AddRow(sb, "Technical Support (software)", "<a href='mailto:support@teamsportsinfo.com'>support@teamsportsinfo.com</a>");
        EndBodyOnly(sb);
        EndTableOnly(sb);
        return sb.ToString();
    }

    private async Task<string> ResolveFamilyUserNameAsync(string? familyUserId)
    {
        if (string.IsNullOrEmpty(familyUserId)) return "THERE IS NO FAMILY ACCOUNT ATTACHED";
        var un = await _context.AspNetUsers.Where(u => u.Id == familyUserId).Select(u => u.UserName).SingleOrDefaultAsync();
        return un ?? string.Empty;
    }

    private async Task<string> ResolveTeamNameAsync(Guid? teamId)
    {
        if (!teamId.HasValue) return string.Empty;
        var rec = await (from t in _context.Teams
                         join j in _context.Jobs on t.JobId equals j.JobId
                         join c in _context.Customers on j.CustomerId equals c.CustomerId
                         where t.TeamId == teamId.Value
                         select new { Team = t.TeamFullName ?? t.TeamName, Club = c.CustomerName }).SingleOrDefaultAsync();
        if (rec == null) return string.Empty;
        return $"{rec.Club}:{rec.Team}";
    }

    private async Task<string> ResolveAgeGroupPlusTeamNameAsync(Guid registrationId)
    {
        var rec = await (from r in _context.Registrations
                         join t in _context.Teams on r.AssignedTeamId equals t.TeamId
                         join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                         where r.RegistrationId == registrationId
                         select new { ag.AgegroupName, t.TeamName }).SingleOrDefaultAsync();
        return rec == null ? string.Empty : $"{rec.AgegroupName}:{rec.TeamName}";
    }

    private async Task<string> ResolveAgeGroupNameAsync(Guid? teamId)
    {
        if (!teamId.HasValue) return string.Empty;
        var name = await (from t in _context.Teams
                          join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                          where t.TeamId == teamId.Value
                          select ag.AgegroupName).SingleOrDefaultAsync();
        return name ?? string.Empty;
    }

    private async Task<string> ResolveLeagueNameAsync(Guid? teamId)
    {
        if (!teamId.HasValue) return string.Empty;
        var name = await (from t in _context.Teams
                          join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                          join l in _context.Leagues on ag.LeagueId equals l.LeagueId
                          where t.TeamId == teamId.Value
                          select l.LeagueName).SingleOrDefaultAsync();
        return name ?? string.Empty;
    }

    private async Task<string> BuildAccountingAHtmlAsync(Guid registrationId, Guid paymentMethodCreditCardId, string jobName, bool emailMode)
    {
        var rows = await (from ra in _context.RegistrationAccounting
                          join r in _context.Registrations on ra.RegistrationId equals r.RegistrationId
                          join u in _context.AspNetUsers on r.UserId equals u.Id
                          join pm in _context.AccountingPaymentMethods on ra.PaymentMethodId equals pm.PaymentMethodId
                          where ra.RegistrationId == registrationId && ra.Active == true
                          orderby ra.AId
                          select new
                          {
                              ra.AId,
                              RegistrantName = u.FirstName + " " + u.LastName,
                              pm.PaymentMethod,
                              ra.Dueamt,
                              ra.Payamt,
                              ra.Createdate,
                              ra.Comment,
                              ra.DiscountCodeAi,
                              ra.PaymentMethodId
                          }).ToListAsync();
        var sb = new StringBuilder();
        StartTable(sb, emailMode);
        AddCaption(sb, $"{WebUtility.HtmlEncode(jobName)}:Most Recent Transaction(s)", emailMode);
        StartHead(sb);
        AddHeaderRow(sb, "ID", "Player", "Method", "Fees$", "Discount$", "Paid$", "Owes$");
        EndHeadStartBody(sb);
        decimal feesSum = 0m, discountSum = 0m, paidSum = 0m, owesSum = 0m;
        foreach (var r in rows)
        {
            decimal discount = 0m;
            if (r.Payamt.HasValue && r.Payamt > 0 && r.PaymentMethodId == paymentMethodCreditCardId && r.DiscountCodeAi.HasValue)
            {
                discount = await _discountEvaluator.EvaluateAsync(r.DiscountCodeAi.Value, r.Payamt.Value);
            }
            var owes = (r.Dueamt ?? 0m) - (r.Payamt ?? 0m);
            feesSum += (r.Dueamt ?? 0m); discountSum += discount; paidSum += (r.Payamt ?? 0m); owesSum += owes;
            AddRow(sb,
                r.AId.ToString(),
                WebUtility.HtmlEncode(r.RegistrantName ?? string.Empty),
                r.PaymentMethod ?? string.Empty,
                FormatCurrency(r.Dueamt ?? 0m),
                FormatCurrency(discount),
                FormatCurrency(r.Payamt ?? 0m),
                FormatCurrency(owes));
        }
        EndBodyStartFoot(sb);
        AddFooterRow(sb, "Totals", string.Empty, string.Empty, FormatCurrency(feesSum), FormatCurrency(discountSum), FormatCurrency(paidSum), FormatCurrency(owesSum));
        EndFootEndTable(sb);
        return sb.ToString();
    }

    private async Task<string> BuildAccountingTeamsHtmlAsync(Guid registrationId, Guid paymentMethodCreditCardId, bool emailMode)
    {
        var clubName = await _context.Registrations.Where(r => r.RegistrationId == registrationId).Select(r => r.ClubName).SingleOrDefaultAsync() ?? string.Empty;
        var teams = await (from t in _context.Teams
                           join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                           where t.ClubrepRegistrationid == registrationId && ag.AgegroupName != "Dropped Teams" && t.TeamName != "Club Teams"
                           select new { t.TeamId, TeamName = ag.AgegroupName + " " + t.TeamName }).ToListAsync();
        var sb = new StringBuilder();
        StartTable(sb, emailMode);
        AddCaption(sb, $"{WebUtility.HtmlEncode(clubName)}:Most Recent Transaction(s)", emailMode);
        StartHead(sb);
        AddHeaderRow(sb, "Active", "ID", "Team", "Method", "Fees$", "Paid$", "Date", "Owes$", "Comment");
        EndHeadStartBody(sb);
        foreach (var t in teams)
        {
            var rows = await (from ra in _context.RegistrationAccounting
                              join tm in _context.Teams on ra.TeamId equals tm.TeamId
                              join r in _context.Registrations on tm.ClubrepRegistrationid equals r.RegistrationId
                              join u in _context.AspNetUsers on r.UserId equals u.Id
                              join pm in _context.AccountingPaymentMethods on ra.PaymentMethodId equals pm.PaymentMethodId
                              where ra.TeamId == t.TeamId
                              orderby ra.AId
                              select new { ra.Active, ra.AId, pm.PaymentMethod, ra.Dueamt, ra.Payamt, ra.Createdate, ra.Comment, ra.DiscountCodeAi, ra.PaymentMethodId }).ToListAsync();
            foreach (var r in rows)
            {
                decimal discount = 0m;
                if (r.Payamt.HasValue && r.Payamt > 0 && r.PaymentMethodId == paymentMethodCreditCardId && r.DiscountCodeAi.HasValue)
                {
                    discount = await _discountEvaluator.EvaluateAsync(r.DiscountCodeAi.Value, r.Payamt.Value);
                }
                var owes = (r.Dueamt ?? 0m) - (r.Payamt ?? 0m);
                var activeChecked = (r.Active ?? false) ? "checked" : string.Empty;
                AddRow(sb,
                    $"<input type='checkbox' disabled {activeChecked}>",
                    r.AId.ToString(),
                    WebUtility.HtmlEncode(t.TeamName),
                    r.PaymentMethod ?? string.Empty,
                    FormatCurrency(r.Dueamt ?? 0m),
                    FormatCurrency(r.Payamt ?? 0m),
                    r.Createdate?.ToString("g") ?? string.Empty,
                    FormatCurrency(owes),
                    WebUtility.HtmlEncode(r.Comment ?? string.Empty));
            }
        }
        EndBodyOnly(sb);
        EndTableOnly(sb);
        return sb.ToString();
    }

    private async Task<string> BuildTeamsSummaryHtmlAsync(Guid registrationId, bool emailMode)
    {
        var teams = await (from t in _context.Teams
                           join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                           join r in _context.Registrations on t.ClubrepRegistrationid equals r.RegistrationId
                           where t.ClubrepRegistrationid == registrationId && ag.AgegroupName != "Dropped Teams" && t.TeamName != "Club Teams"
                           select new
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
                           }).ToListAsync();
        if (teams.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        StartTable(sb, emailMode);
        AddCaption(sb, $"{WebUtility.HtmlEncode(teams[0].ClubName ?? string.Empty)}:Registered Teams SUMMARY", emailMode);
        StartHead(sb);
        AddHeaderRow(sb, string.Empty, "Team", "Deposit Fee", "Additional Fees", "Processing Fee", "Paid$", "Owes$");
        EndHeadStartBody(sb);
        int i = 1;
        decimal sumOwed = 0m; decimal sumPaid = 0m; decimal sumDeposit = 0m; decimal sumAdditional = 0m; decimal sumProcessing = 0m;
        foreach (var t in teams)
        {
            var owedRow = (t.OwedTotal == 0 && (t.PaidTotal >= (t.RosterFee + t.AdditionalFees))) ? 0m : ((t.RosterFee ?? 0m) + (t.AdditionalFees ?? 0m) + (t.ProcessingFees ?? 0m) - (t.PaidTotal ?? 0m));
            sumOwed += owedRow;
            sumPaid += (t.PaidTotal ?? 0m);
            sumDeposit += (t.RosterFee ?? 0m);
            sumAdditional += (t.AdditionalFees ?? 0m);
            sumProcessing += (t.ProcessingFees ?? 0m);
            AddRow(sb,
                i++.ToString(),
                WebUtility.HtmlEncode(t.TeamName),
                FormatCurrency(t.RosterFee ?? 0m),
                FormatCurrency(t.AdditionalFees ?? 0m),
                FormatCurrency(t.ProcessingFees ?? 0m),
                FormatCurrency(t.PaidTotal ?? 0m),
                FormatCurrency(owedRow));
        }
        EndBodyStartFoot(sb);
        AddFooterRow(sb, "Totals", string.Empty, FormatCurrency(sumDeposit), FormatCurrency(sumAdditional), FormatCurrency(sumProcessing), FormatCurrency(sumPaid), FormatCurrency(sumOwed));
        EndFootEndTable(sb);
        return sb.ToString();
    }

    private async Task<string> BuildNoMoneyTeamsHtmlAsync(Guid registrationId, bool emailMode)
    {
        var teams = await (from t in _context.Teams
                           join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                           join r in _context.Registrations on t.ClubrepRegistrationid equals r.RegistrationId
                           where t.ClubrepRegistrationid == registrationId
                           select new { TeamName = ag.AgegroupName + " " + t.TeamName, ClubName = r.ClubName }).ToListAsync();
        if (teams.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        StartTable(sb, emailMode);
        AddCaption(sb, $"{WebUtility.HtmlEncode(teams[0].ClubName ?? string.Empty)}:Registered Teams SUMMARY", emailMode);
        StartHead(sb);
        AddHeaderRow(sb, string.Empty, "Club Team");
        EndHeadStartBody(sb);
        int i = 1;
        foreach (var t in teams)
        {
            AddRow(sb, i++.ToString(), WebUtility.HtmlEncode(t.TeamName));
        }
        EndBodyOnly(sb);
        EndTableOnly(sb);
        return sb.ToString();
    }

    private async Task<string> BuildWaiverHtmlAsync(Guid jobId, string jobName, string? customerName, Func<dynamic, string?> selector, string label)
    {
        // Use dynamic selector against anonymous projection to avoid building another model.
        var rec = await _context.Jobs.Where(j => j.JobId == jobId)
            .Select(j => new { j.PlayerRegRefundPolicy, j.PlayerRegReleaseOfLiability, j.AdultRegReleaseOfLiability, j.PlayerRegCodeOfConduct, j.PlayerRegCovid19Waiver })
            .SingleOrDefaultAsync();
        if (rec == null) return string.Empty;
        string? raw = selector(rec);
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var html = WebUtility.HtmlDecode(raw) ?? string.Empty;
        html = html.Replace("!JOBNAME", jobName ?? string.Empty).Replace("!CUSTOMERNAME", customerName ?? string.Empty);
        var safeLabel = string.IsNullOrWhiteSpace(label) ? "Waiver:" : label;
        return $"<div><strong>{WebUtility.HtmlEncode(safeLabel)}</strong> {html}</div>";
    }

    private async Task<string> BuildStaffChoicesAsync(Guid registrationId)
    {
        var keys = await _context.Registrations.Where(r => r.RegistrationId == registrationId)
            .Select(r => new { r.UserId, r.JobId, r.SpecialRequests }).SingleOrDefaultAsync();
        if (keys == null) return string.Empty;
        var sb = new StringBuilder("<ul>");
        sb.AppendFormat("<li>Coaching Requests: {0}</li>", WebUtility.HtmlEncode(keys.SpecialRequests ?? string.Empty));
        sb.Append("</ul>");
        return sb.ToString();
    }

    private async Task<string> BuildCoachFullTeamNameChoicesAsync(Guid registrationId, bool emailMode)
    {
        var keys = await _context.Registrations.Where(r => r.RegistrationId == registrationId)
            .Select(r => new { r.UserId, r.JobId }).SingleOrDefaultAsync();
        if (keys == null) return string.Empty;
        var choices = await (from r in _context.Registrations
                             join t in _context.Teams on r.AssignedTeamId equals t.TeamId
                             join rCR in _context.Registrations on t.ClubrepRegistrationid equals rCR.RegistrationId
                             join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                             join roles in _context.AspNetRoles on r.RoleId equals roles.Id
                             where r.JobId == keys.JobId && r.UserId == keys.UserId && roles.Name == "Staff"
                             orderby ag.AgegroupName, t.TeamName
                             select new { Club = rCR.ClubName, Age = ag.AgegroupName, Team = t.TeamName }).ToListAsync();
        var sb = new StringBuilder();
        StartTable(sb, emailMode);
        AddCaption(sb, "Coach Team Selections", emailMode);
        StartHead(sb);
        AddHeaderRow(sb, "Club", "Age Group", "Team");
        EndHeadStartBody(sb);
        foreach (var c in choices)
        {
            AddRow(sb,
                WebUtility.HtmlEncode(c.Club ?? string.Empty),
                WebUtility.HtmlEncode(c.Age ?? string.Empty),
                WebUtility.HtmlEncode(c.Team ?? string.Empty));
        }
        EndBodyOnly(sb);
        EndTableOnly(sb);
        return sb.ToString();
    }

    // Helper methods for uniform table construction.
    // Unified email-safe styling: rely purely on inline CSS (most widely preserved) for consistent
    // rendering across web UI and email clients. Use collapsed borders and per-cell borders for full grid.
    // Dual-mode helpers: email mode uses full inline styling; screen mode uses CSS classes.
    private static void StartTable(StringBuilder sb, bool emailMode)
    {
        if (emailMode)
            sb.Append("<table border='1' cellpadding='4' cellspacing='0' style='border:1px solid #000;border-collapse:collapse;width:100%;font-family:Arial,Helvetica,sans-serif;font-size:12px;' role='table'>");
        else
            sb.Append("<table class='tsic-grid' role='table'>");
    }
    private static void AddCaption(StringBuilder sb, string caption, bool emailMode)
    {
        var safe = WebUtility.HtmlEncode(caption);
        if (emailMode)
        {
            sb.AppendFormat("<div style='font-weight:600;margin:4px 0;'>{0}</div>", safe); // redundancy for email clients
            sb.AppendFormat("<caption style='caption-side:top;text-align:left;font-weight:600;padding:4px 6px;'>{0}</caption>", safe);
        }
        else
        {
            sb.AppendFormat("<caption class='tsic-caption'>{0}</caption>", safe);
        }
    }
    private static void StartHead(StringBuilder sb) => sb.Append("<thead>");
    private static void AddHeaderRow(StringBuilder sb, params string[] headers)
    {
        sb.Append("<tr>");
        foreach (var h in headers)
            sb.AppendFormat("<th scope='col' {0}>{1}</th>",
                // Apply inline style only when not using class-based mode (class-based mode handled by .tsic-grid CSS) - we detect by absence of tsic-grid earlier
                "class='tsic-grid-header'",
                WebUtility.HtmlEncode(h));
        sb.Append("</tr>");
    }
    private static void EndHeadStartBody(StringBuilder sb) => sb.Append("</thead><tbody>");
    private static void AddRow(StringBuilder sb, params string?[] cells)
    {
        sb.Append("<tr>");
        foreach (var c in cells)
            sb.AppendFormat("<td class='tsic-grid-cell'>{0}</td>", c ?? string.Empty);
        sb.Append("</tr>");
    }
    private static void EndBodyStartFoot(StringBuilder sb) => sb.Append("</tbody><tfoot>");
    private static void AddFooterRow(StringBuilder sb, params string[] cells)
    {
        if (cells.Length == 0) return;
        sb.Append("<tr>");
        sb.AppendFormat("<th scope='row' class='tsic-grid-footer-header'>{0}</th>", cells[0]);
        for (int i = 1; i < cells.Length; i++)
            sb.AppendFormat("<td class='tsic-grid-footer-cell'>{0}</td>", cells[i]);
        sb.Append("</tr>");
    }
    private static void EndFootEndTable(StringBuilder sb) => sb.Append("</tfoot></table>");
    private static void EndBodyOnly(StringBuilder sb) => sb.Append("</tbody>");
    private static void EndTableOnly(StringBuilder sb) => sb.Append("</table>");
    private static string FormatCurrency(decimal value) => value.ToString("C");
}
