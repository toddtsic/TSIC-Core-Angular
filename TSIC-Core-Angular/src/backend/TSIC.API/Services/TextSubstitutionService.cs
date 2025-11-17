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
        public decimal? FeeTotal { get; init; }
        public decimal? PaidTotal { get; init; }
        public decimal? OwedTotal { get; init; }
        public string? RegistrationCategory { get; init; }
        public string? ClubName { get; init; }
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

        var job = await _context.Jobs.AsNoTracking().Where(j => j.JobPath == jobSegment)
            .Select(j => new { j.JobId, j.JobName }).SingleOrDefaultAsync();
        if (job == null) return template; // Unknown job segment

        var fixedFieldList = await LoadFixedFieldsAsync(_context, job.JobId, registrationId, familyUserId);
        if (fixedFieldList.Count == 0) return template; // No data to substitute

        var first = fixedFieldList[0];

        // Build token dictionary (simple tokens + complex HTML sections)
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddSimpleTokens(tokens, first, jobSegment);
        await AddComplexTokensAsync(tokens, fixedFieldList, paymentMethodCreditCardId, registrationId, template);

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
                              Assignment = r.Assignment,
                              FeeTotal = r.FeeTotal,
                              OwedTotal = r.OwedTotal,
                              PaidTotal = r.PaidTotal,
                              RegistrationCategory = r.RegistrationCategory,
                              ClubName = r.ClubName,
                              Email = u.Email,
                              JobDescription = j.JobDescription,
                              JobName = j.JobName,
                              JobPath = j.JobPath,
                              MailTo = j.MailTo,
                              PayTo = j.PayTo,
                              RoleName = roles.Name,
                              Season = j.Season,
                              SportName = s.SportName,
                              AssignedTeamId = r.AssignedTeamId,
                              Active = r.BActive,
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
                          Assignment = r.Assignment,
                          FeeTotal = r.FeeTotal,
                          OwedTotal = r.OwedTotal,
                          PaidTotal = r.PaidTotal,
                          RegistrationCategory = r.RegistrationCategory,
                          ClubName = r.ClubName,
                          Email = u.Email,
                          JobDescription = j.JobDescription,
                          JobName = j.JobName,
                          JobPath = j.JobPath,
                          MailTo = j.MailTo,
                          PayTo = j.PayTo,
                          RoleName = roles.Name,
                          Season = j.Season,
                          SportName = s.SportName,
                          AssignedTeamId = r.AssignedTeamId,
                          Active = r.BActive,
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
        tokens["!CUSTOMERNAME"] = f.ClubName ?? string.Empty; // Legacy mapping
        tokens["!ROLENAME"] = f.RoleName ?? string.Empty;
        tokens["!PERSON"] = f.Person ?? string.Empty;
        tokens["!TRAININGGROUP"] = f.DayGroup ?? string.Empty;
        tokens["!EMAIL"] = f.Email ?? string.Empty;
        tokens["!AMTFEES"] = f.FeeTotal?.ToString("C") ?? "$0.00";
        tokens["!AMTPAID"] = f.PaidTotal?.ToString("C") ?? "$0.00";
        tokens["!AMTOWED"] = f.OwedTotal?.ToString("C") ?? "$0.00";
        tokens["!REGISTRANT_ISACTIVEORDROPPED"] = (f.Active == true) ? "ACTIVE" : "INACTIVE";
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
        string template)
    {
        if (template.Contains("!F-DISPLAYINACTIVEPLAYERS", StringComparison.OrdinalIgnoreCase))
            tokens["!F-DISPLAYINACTIVEPLAYERS"] = BuildInactivePlayersHtml(list);

        if (template.Contains("!F-PLAYERS", StringComparison.OrdinalIgnoreCase))
            tokens["!F-PLAYERS"] = BuildPlayersTableHtml(list);

        if (template.Contains("!F-ADN-ARB", StringComparison.OrdinalIgnoreCase))
            tokens["!F-ADN-ARB"] = BuildArbTableHtml(list);

        if (template.Contains("!F-ACCOUNTING", StringComparison.OrdinalIgnoreCase) && registrationId.HasValue)
            tokens["!F-ACCOUNTING"] = await BuildAccountingTableHtmlAsync(registrationId.Value, paymentMethodCreditCardId);
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

    private static string BuildPlayersTableHtml(List<FixedFields> list)
    {
        if (list.Count == 0) return string.Empty;
        var html = new StringBuilder("<table style='border:1px solid;border-collapse:separate;border-spacing:10px'>")
            .Append("<caption style='caption-side:top;'>Family Players</caption>")
            .Append("<tr><th>Player</th><th>Status</th><th>Assignment</th><th>Fees$</th><th>Paid$</th><th>Owes$</th></tr>");
        foreach (var q in list)
        {
            var status = (string.IsNullOrEmpty(q.AdnSubscriptionId) && q.Active != true) ? "INACTIVE" : "ACTIVE";
            html.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td>{5}</td></tr>",
                WebUtility.HtmlEncode(q.Person), status, WebUtility.HtmlEncode(q.Assignment), q.FeeTotal?.ToString("C"), q.PaidTotal?.ToString("C"), q.OwedTotal?.ToString("C"));
        }
        html.Append("</table>");
        return html.ToString();
    }

    private static string BuildArbTableHtml(List<FixedFields> list)
    {
        if (list.Count == 0) return string.Empty;
        var first = list[0];
        if (first.AdnSubscriptionAmountPerOccurence is not > 0) return string.Empty;
        var html = new StringBuilder("<table style='border:1px solid;border-collapse:separate;border-spacing:10px'>")
            .Append("<caption style='caption-side:top;'>Automated Recurring Billing</caption>")
            .Append("<tr><th>Player</th><th>Sub. Id</th><th>Status</th><th>Starting</th><th>#Billings</th><th>Frequency</th><th>Charge/Billing</th><th>Total Charges</th></tr>");
        foreach (var q in list)
        {
            var intervalLabel = (q.AdnSubscriptionIntervalLength ?? 0) > 1 ? "months" : "month";
            var totalCharges = (q.AdnSubscriptionAmountPerOccurence ?? 0m) * (q.AdnSubscriptionBillingOccurences ?? 0);
            html.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td>every {5} {6}</td><td>{7}</td><td>{8}</td></tr>",
                WebUtility.HtmlEncode(q.Person), q.AdnSubscriptionId, q.AdnSubscriptionStatus,
                q.AdnSubscriptionStartDate?.ToString("d"), q.AdnSubscriptionBillingOccurences,
                q.AdnSubscriptionIntervalLength, intervalLabel,
                q.AdnSubscriptionAmountPerOccurence?.ToString("C"), totalCharges.ToString("C"));
        }
        html.Append("</table>");
        return html.ToString();
    }

    private async Task<string> BuildAccountingTableHtmlAsync(Guid registrationId, Guid paymentMethodCreditCardId)
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
        var html = new StringBuilder()
            .Append("<table style='border:1px solid;border-collapse:separate;border-spacing:10px'>")
            .Append("<caption style='caption-side:top;'>Most Recent Transaction(s)</caption>")
            .Append("<tr><th>ID</th><th>Player</th><th>Method</th><th>Date</th><th>Paid$</th></tr>");
        foreach (var row in rows)
        {
            if (row.Payamt.HasValue && row.Payamt > 0 && row.PaymentMethodId == paymentMethodCreditCardId && row.DiscountCodeAi.HasValue)
            {
                _ = await _discountEvaluator.EvaluateAsync(row.DiscountCodeAi.Value, row.Payamt.Value);
            }
            html.AppendFormat("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3:g}</td><td>{4}</td></tr>", row.AId, WebUtility.HtmlEncode(row.RegistrantName), row.PaymentMethod, row.Createdate, row.Payamt?.ToString("C"));
        }
        html.Append("</table>");
        return html.ToString();
    }
}
