using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using TSIC.Contracts.Repositories;
using TSIC.Application.Services.Players;
using TSIC.Application.Services.Shared.Html;
using TSIC.Application.Services.Shared.Text;
using TSIC.API.Services.Shared.Utilities;

namespace TSIC.API.Services.Shared.TextSubstitution;

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

    private readonly ITextSubstitutionRepository _repo;
    private readonly IDiscountCodeEvaluator _discountEvaluator;

    public TextSubstitutionService(ITextSubstitutionRepository repo, IDiscountCodeEvaluator discountEvaluator)
    {
        _repo = repo;
        _discountEvaluator = discountEvaluator;
    }

    public async Task<string> SubstituteJobTokensAsync(string jobPath, string template)
    {
        if (string.IsNullOrWhiteSpace(template)) return template;

        var job = await _repo.GetJobTokenInfoAsync(jobPath);
        if (job == null) return template;

        var uslaxDate = job.UslaxNumberValidThroughDate?.ToString("M/d/yy") ?? string.Empty;

        return template
            .Replace("!JOBNAME", job.JobName, StringComparison.OrdinalIgnoreCase)
            .Replace("!USLAXVALIDTHROUGHDATE", uslaxDate, StringComparison.OrdinalIgnoreCase);
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

        var fixedFieldList = await LoadFixedFieldsAsync(jobSegment, registrationId, familyUserId);
        if (fixedFieldList.Count == 0) return template; // No data to substitute

        var first = fixedFieldList[0];

        // Build token dictionary (simple tokens + complex HTML sections)
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddSimpleTokens(tokens, first, jobSegment);
        await AddComplexTokensAsync(tokens, fixedFieldList, paymentMethodCreditCardId, registrationId, template, emailMode);

        // Perform replacements
        var result = TokenReplacer.ReplaceTokens(template, tokens);
        return result;
    }

    // Maps FixedFields (EF entity projection) to PlayerRegistrationData (use case DTO)
    private static List<PlayerHtmlGenerator.PlayerRegistrationData> MapToRegistrationData(List<FixedFields> fixedFields)
    {
        return fixedFields.Select(f => new PlayerHtmlGenerator.PlayerRegistrationData
        {
            Person = f.Person,
            Assignment = f.Assignment,
            Active = f.Active,
            FeeTotal = f.FeeTotal,
            PaidTotal = f.PaidTotal,
            OwedTotal = f.OwedTotal,
            AdnSubscriptionId = f.AdnSubscriptionId,
            AdnSubscriptionStatus = f.AdnSubscriptionStatus,
            AdnSubscriptionBillingOccurences = f.AdnSubscriptionBillingOccurences,
            AdnSubscriptionAmountPerOccurence = f.AdnSubscriptionAmountPerOccurence,
            AdnSubscriptionStartDate = f.AdnSubscriptionStartDate,
            AdnSubscriptionIntervalLength = f.AdnSubscriptionIntervalLength
        }).ToList();
    }

    // Maps repository FixedFieldsData to FixedFields for backward compatibility
    private static FixedFields MapToFixedFields(FixedFieldsData data)
    {
        return new FixedFields
        {
            RegistrationId = data.RegistrationId,
            JobId = data.JobId,
            FamilyUserId = data.FamilyUserId,
            Person = data.Person,
            Assignment = data.Assignment,
            UserName = data.UserName,
            FeeTotal = data.FeeTotal,
            PaidTotal = data.PaidTotal,
            OwedTotal = data.OwedTotal,
            RegistrationCategory = data.RegistrationCategory,
            ClubName = data.ClubName,
            CustomerName = data.CustomerName,
            Email = data.Email,
            JobDescription = data.JobDescription,
            JobName = data.JobName,
            JobPath = data.JobPath,
            MailTo = data.MailTo,
            PayTo = data.PayTo,
            RoleName = data.RoleName,
            Season = data.Season,
            SportName = data.SportName,
            AssignedTeamId = data.AssignedTeamId,
            Active = data.Active,
            Volposition = data.Volposition,
            UniformNo = data.UniformNo,
            DayGroup = data.DayGroup,
            JerseySize = data.JerseySize,
            ShortsSize = data.ShortsSize,
            TShirtSize = data.TShirtSize,
            AdnArb = data.AdnArb,
            AdnSubscriptionId = data.AdnSubscriptionId,
            AdnSubscriptionStatus = data.AdnSubscriptionStatus,
            AdnSubscriptionBillingOccurences = data.AdnSubscriptionBillingOccurences,
            AdnSubscriptionAmountPerOccurence = data.AdnSubscriptionAmountPerOccurence,
            AdnSubscriptionStartDate = data.AdnSubscriptionStartDate,
            AdnSubscriptionIntervalLength = data.AdnSubscriptionIntervalLength,
            JobLogoHeader = data.JobLogoHeader,
            JobCode = data.JobCode,
            UslaxNumberValidThroughDate = data.UslaxNumberValidThroughDate
        };
    }

    private async Task<List<FixedFields>> LoadFixedFieldsAsync(string jobPath, Guid? registrationId, string familyUserId)
    {
        List<FixedFieldsData> dataList;

        // Path A: single registration (familyUserId may be empty)
        if (string.IsNullOrEmpty(familyUserId) && registrationId.HasValue)
        {
            dataList = await _repo.LoadFixedFieldsByRegistrationAsync(registrationId.Value);
        }
        else
        {
            // Path B: family across job - need to resolve jobId from jobPath first
            var jobInfo = await _repo.GetJobTokenInfoAsync(jobPath);
            if (jobInfo == null) return new List<FixedFields>();

            // Get a temporary registration to find the JobId
            var tempList = await _repo.LoadFixedFieldsByFamilyAsync(Guid.Empty, familyUserId);
            if (tempList.Count == 0) return new List<FixedFields>();

            // Now get the full list with the correct jobId
            dataList = await _repo.LoadFixedFieldsByFamilyAsync(tempList[0].JobId, familyUserId);
        }

        return dataList.Select(MapToFixedFields).ToList();
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
            tokens["!F-DISPLAYINACTIVEPLAYERS"] = PlayerHtmlGenerator.BuildInactivePlayersHtml(MapToRegistrationData(list));

        if (template.Contains("!F-PLAYERS", StringComparison.OrdinalIgnoreCase))
            tokens["!F-PLAYERS"] = PlayerHtmlGenerator.BuildPlayersTableHtml(MapToRegistrationData(list), emailMode);

        if (template.Contains("!F-ADN-ARB", StringComparison.OrdinalIgnoreCase))
            tokens["!F-ADN-ARB"] = PlayerHtmlGenerator.BuildArbTableHtml(MapToRegistrationData(list), emailMode);

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

    private async Task<string> BuildAccountingTableHtmlAsync(Guid registrationId, Guid paymentMethodCreditCardId, bool emailMode)
    {
        var rows = await _repo.GetAccountingTransactionsAsync(registrationId);
        if (rows.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        HtmlTableBuilder.StartTable(sb, emailMode);
        HtmlTableBuilder.AddCaption(sb, "Most Recent Transaction(s)", emailMode);
        HtmlTableBuilder.StartHead(sb);
        HtmlTableBuilder.AddHeaderRow(sb, "ID", "Player", "Method", "Date", "Paid$");
        HtmlTableBuilder.EndHeadStartBody(sb);
        decimal paidSum = 0m;
        foreach (var row in rows)
        {
            if (row.Payamt.HasValue && row.Payamt > 0 && row.PaymentMethodId == paymentMethodCreditCardId && row.DiscountCodeAi.HasValue)
            {
                _ = await _discountEvaluator.EvaluateAsync(row.DiscountCodeAi.Value, row.Payamt.Value);
            }
            var paid = row.Payamt ?? 0m; paidSum += paid;
            HtmlTableBuilder.AddRow(sb,
                row.AId.ToString(),
                WebUtility.HtmlEncode(row.RegistrantName ?? string.Empty),
                row.PaymentMethod ?? string.Empty,
                row.Createdate?.ToString("g") ?? string.Empty,
                HtmlTableBuilder.FormatCurrency(paid));
        }
        HtmlTableBuilder.EndBodyStartFoot(sb);
        HtmlTableBuilder.AddFooterRow(sb, "Total", string.Empty, string.Empty, string.Empty, HtmlTableBuilder.FormatCurrency(paidSum));
        HtmlTableBuilder.EndFootEndTable(sb);
        return sb.ToString();
    }

    private async Task<string> BuildNoAccountingPlayersAsync(List<FixedFields> list, bool emailMode)
    {
        if (list.Count == 0) return string.Empty;
        var regIds = list.Select(x => x.RegistrationId).ToList();
        var clubByReg = await _repo.GetTeamClubNamesAsync(regIds);
        var sb = new StringBuilder();
        HtmlTableBuilder.StartTable(sb, emailMode);
        HtmlTableBuilder.AddCaption(sb, "Registered Family Players", emailMode);
        HtmlTableBuilder.StartHead(sb);
        HtmlTableBuilder.AddHeaderRow(sb, "Player", "Status", "Assignment");
        HtmlTableBuilder.EndHeadStartBody(sb);
        foreach (var q in list)
        {
            var assignment = q.Assignment ?? string.Empty;
            if (clubByReg.TryGetValue(q.RegistrationId, out var club) && !string.IsNullOrEmpty(club))
            {
                assignment = $"{club}:{assignment}";
            }
            var status = (q.Active != true) ? "INACTIVE" : "ACTIVE";
            HtmlTableBuilder.AddRow(sb,
                WebUtility.HtmlEncode(q.Person ?? string.Empty),
                status,
                WebUtility.HtmlEncode(assignment));
        }
        HtmlTableBuilder.EndBodyOnly(sb);
        HtmlTableBuilder.EndTableOnly(sb);
        return sb.ToString();
    }

    private async Task<string> BuildContactBlockAsync(Guid jobId, bool emailMode)
    {
        var director = await _repo.GetDirectorContactAsync(jobId);
        if (director == null) return string.Empty;
        var sb = new StringBuilder();
        HtmlTableBuilder.StartTable(sb, emailMode);
        HtmlTableBuilder.AddCaption(sb, "Contacts", emailMode);
        HtmlTableBuilder.StartHead(sb);
        HtmlTableBuilder.AddHeaderRow(sb, "Role", "Contact");
        HtmlTableBuilder.EndHeadStartBody(sb);
        HtmlTableBuilder.AddRow(sb, "Main Contact", $"<a href='mailto:{director.Email}'>{WebUtility.HtmlEncode(director.Name)}: {director.Email}</a>");
        HtmlTableBuilder.AddRow(sb, "Technical Support (software)", "<a href='mailto:support@teamsportsinfo.com'>support@teamsportsinfo.com</a>");
        HtmlTableBuilder.EndBodyOnly(sb);
        HtmlTableBuilder.EndTableOnly(sb);
        return sb.ToString();
    }

    private async Task<string> ResolveFamilyUserNameAsync(string? familyUserId)
    {
        if (string.IsNullOrEmpty(familyUserId)) return "THERE IS NO FAMILY ACCOUNT ATTACHED";
        var un = await _repo.GetFamilyUserNameAsync(familyUserId);
        return un ?? string.Empty;
    }

    private async Task<string> ResolveTeamNameAsync(Guid? teamId)
    {
        if (!teamId.HasValue) return string.Empty;
        return await _repo.GetTeamNameWithClubAsync(teamId.Value) ?? string.Empty;
    }

    private async Task<string> ResolveAgeGroupPlusTeamNameAsync(Guid registrationId)
    {
        return await _repo.GetAgeGroupPlusTeamNameAsync(registrationId) ?? string.Empty;
    }

    private async Task<string> ResolveAgeGroupNameAsync(Guid? teamId)
    {
        if (!teamId.HasValue) return string.Empty;
        return await _repo.GetAgeGroupNameAsync(teamId.Value) ?? string.Empty;
    }

    private async Task<string> ResolveLeagueNameAsync(Guid? teamId)
    {
        if (!teamId.HasValue) return string.Empty;
        return await _repo.GetLeagueNameAsync(teamId.Value) ?? string.Empty;
    }

    private async Task<string> BuildAccountingAHtmlAsync(Guid registrationId, Guid paymentMethodCreditCardId, string jobName, bool emailMode)
    {
        var rows = await _repo.GetAccountingTransactionsAsync(registrationId);
        var sb = new StringBuilder();
        HtmlTableBuilder.StartTable(sb, emailMode);
        HtmlTableBuilder.AddCaption(sb, $"{WebUtility.HtmlEncode(jobName)}:Most Recent Transaction(s)", emailMode);
        HtmlTableBuilder.StartHead(sb);
        HtmlTableBuilder.AddHeaderRow(sb, "ID", "Player", "Method", "Fees$", "Discount$", "Paid$", "Owes$");
        HtmlTableBuilder.EndHeadStartBody(sb);
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
            HtmlTableBuilder.AddRow(sb,
                r.AId.ToString(),
                WebUtility.HtmlEncode(r.RegistrantName ?? string.Empty),
                r.PaymentMethod ?? string.Empty,
                HtmlTableBuilder.FormatCurrency(r.Dueamt ?? 0m),
                HtmlTableBuilder.FormatCurrency(discount),
                HtmlTableBuilder.FormatCurrency(r.Payamt ?? 0m),
                HtmlTableBuilder.FormatCurrency(owes));
        }
        HtmlTableBuilder.EndBodyStartFoot(sb);
        HtmlTableBuilder.AddFooterRow(sb, "Totals", string.Empty, string.Empty, HtmlTableBuilder.FormatCurrency(feesSum), HtmlTableBuilder.FormatCurrency(discountSum), HtmlTableBuilder.FormatCurrency(paidSum), HtmlTableBuilder.FormatCurrency(owesSum));
        HtmlTableBuilder.EndFootEndTable(sb);
        return sb.ToString();
    }

    private async Task<string> BuildAccountingTeamsHtmlAsync(Guid registrationId, Guid paymentMethodCreditCardId, bool emailMode)
    {
        var clubName = await _repo.GetClubNameAsync(registrationId) ?? string.Empty;
        var teams = await _repo.GetClubTeamsAsync(registrationId);
        var sb = new StringBuilder();
        HtmlTableBuilder.StartTable(sb, emailMode);
        HtmlTableBuilder.AddCaption(sb, $"{WebUtility.HtmlEncode(clubName)}:Most Recent Transaction(s)", emailMode);
        HtmlTableBuilder.StartHead(sb);
        HtmlTableBuilder.AddHeaderRow(sb, "Active", "ID", "Team", "Method", "Fees$", "Paid$", "Date", "Owes$", "Comment");
        HtmlTableBuilder.EndHeadStartBody(sb);
        foreach (var t in teams)
        {
            var rows = await _repo.GetTeamAccountingTransactionsAsync(t.TeamId);
            foreach (var r in rows)
            {
                decimal discount = 0m;
                if (r.Payamt.HasValue && r.Payamt > 0 && r.PaymentMethodId == paymentMethodCreditCardId && r.DiscountCodeAi.HasValue)
                {
                    discount = await _discountEvaluator.EvaluateAsync(r.DiscountCodeAi.Value, r.Payamt.Value);
                }
                var owes = (r.Dueamt ?? 0m) - (r.Payamt ?? 0m);
                var activeChecked = (r.Active ?? false) ? "checked" : string.Empty;
                HtmlTableBuilder.AddRow(sb,
                    $"<input type='checkbox' disabled {activeChecked}>",
                    r.AId.ToString(),
                    WebUtility.HtmlEncode(t.TeamName),
                    r.PaymentMethod ?? string.Empty,
                    HtmlTableBuilder.FormatCurrency(r.Dueamt ?? 0m),
                    HtmlTableBuilder.FormatCurrency(r.Payamt ?? 0m),
                    r.Createdate?.ToString("g") ?? string.Empty,
                    HtmlTableBuilder.FormatCurrency(owes),
                    WebUtility.HtmlEncode(r.Comment ?? string.Empty));
            }
        }
        HtmlTableBuilder.EndBodyOnly(sb);
        HtmlTableBuilder.EndTableOnly(sb);
        return sb.ToString();
    }

    private async Task<string> BuildTeamsSummaryHtmlAsync(Guid registrationId, bool emailMode)
    {
        var teams = await _repo.GetTeamsSummaryAsync(registrationId);
        if (teams.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        HtmlTableBuilder.StartTable(sb, emailMode);
        HtmlTableBuilder.AddCaption(sb, $"{WebUtility.HtmlEncode(teams[0].ClubName ?? string.Empty)}:Registered Teams SUMMARY", emailMode);
        HtmlTableBuilder.StartHead(sb);
        HtmlTableBuilder.AddHeaderRow(sb, string.Empty, "Team", "Deposit Fee", "Additional Fees", "Processing Fee", "Paid$", "Owes$");
        HtmlTableBuilder.EndHeadStartBody(sb);
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
            HtmlTableBuilder.AddRow(sb,
                i++.ToString(),
                WebUtility.HtmlEncode(t.TeamName),
                HtmlTableBuilder.FormatCurrency(t.RosterFee ?? 0m),
                HtmlTableBuilder.FormatCurrency(t.AdditionalFees ?? 0m),
                HtmlTableBuilder.FormatCurrency(t.ProcessingFees ?? 0m),
                HtmlTableBuilder.FormatCurrency(t.PaidTotal ?? 0m),
                HtmlTableBuilder.FormatCurrency(owedRow));
        }
        HtmlTableBuilder.EndBodyStartFoot(sb);
        HtmlTableBuilder.AddFooterRow(sb, "Totals", string.Empty, HtmlTableBuilder.FormatCurrency(sumDeposit), HtmlTableBuilder.FormatCurrency(sumAdditional), HtmlTableBuilder.FormatCurrency(sumProcessing), HtmlTableBuilder.FormatCurrency(sumPaid), HtmlTableBuilder.FormatCurrency(sumOwed));
        HtmlTableBuilder.EndFootEndTable(sb);
        return sb.ToString();
    }

    private async Task<string> BuildNoMoneyTeamsHtmlAsync(Guid registrationId, bool emailMode)
    {
        var teams = await _repo.GetSimpleTeamsAsync(registrationId);
        if (teams.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        HtmlTableBuilder.StartTable(sb, emailMode);
        HtmlTableBuilder.AddCaption(sb, $"{WebUtility.HtmlEncode(teams[0].ClubName ?? string.Empty)}:Registered Teams SUMMARY", emailMode);
        HtmlTableBuilder.StartHead(sb);
        HtmlTableBuilder.AddHeaderRow(sb, string.Empty, "Club Team");
        HtmlTableBuilder.EndHeadStartBody(sb);
        int i = 1;
        foreach (var t in teams)
        {
            HtmlTableBuilder.AddRow(sb, i++.ToString(), WebUtility.HtmlEncode(t.TeamName));
        }
        HtmlTableBuilder.EndBodyOnly(sb);
        HtmlTableBuilder.EndTableOnly(sb);
        return sb.ToString();
    }

    private async Task<string> BuildWaiverHtmlAsync(Guid jobId, string jobName, string? customerName, Func<dynamic, string?> selector, string label)
    {
        var rec = await _repo.GetJobWaiversAsync(jobId);
        if (rec == null) return string.Empty;
        // Use dynamic casting to call selector with waiver data
        dynamic waiverData = new { rec.PlayerRegRefundPolicy, rec.PlayerRegReleaseOfLiability, rec.AdultRegReleaseOfLiability, rec.PlayerRegCodeOfConduct, rec.PlayerRegCovid19Waiver };
        string? raw = selector(waiverData);
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var html = WebUtility.HtmlDecode(raw) ?? string.Empty;
        html = html.Replace("!JOBNAME", jobName ?? string.Empty).Replace("!CUSTOMERNAME", customerName ?? string.Empty);
        var safeLabel = string.IsNullOrWhiteSpace(label) ? "Waiver:" : label;
        return $"<div><strong>{WebUtility.HtmlEncode(safeLabel)}</strong> {html}</div>";
    }

    private async Task<string> BuildStaffChoicesAsync(Guid registrationId)
    {
        var keys = await _repo.GetStaffInfoAsync(registrationId);
        if (keys == null) return string.Empty;
        var sb = new StringBuilder("<ul>");
        sb.AppendFormat("<li>Coaching Requests: {0}</li>", WebUtility.HtmlEncode(keys.SpecialRequests ?? string.Empty));
        sb.Append("</ul>");
        return sb.ToString();
    }

    private async Task<string> BuildCoachFullTeamNameChoicesAsync(Guid registrationId, bool emailMode)
    {
        var choices = await _repo.GetCoachTeamChoicesAsync(registrationId);
        var sb = new StringBuilder();
        HtmlTableBuilder.StartTable(sb, emailMode);
        HtmlTableBuilder.AddCaption(sb, "Coach Team Selections", emailMode);
        HtmlTableBuilder.StartHead(sb);
        HtmlTableBuilder.AddHeaderRow(sb, "Club", "Age Group", "Team");
        HtmlTableBuilder.EndHeadStartBody(sb);
        foreach (var c in choices)
        {
            HtmlTableBuilder.AddRow(sb,
                WebUtility.HtmlEncode(c.Club ?? string.Empty),
                WebUtility.HtmlEncode(c.Age ?? string.Empty),
                WebUtility.HtmlEncode(c.Team ?? string.Empty));
        }
        HtmlTableBuilder.EndBodyOnly(sb);
        HtmlTableBuilder.EndTableOnly(sb);
        return sb.ToString();
    }
}


