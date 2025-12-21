using System.Net;
using System.Text;
using TSIC.Application.Services.Shared;

namespace TSIC.Application.Services.Players;

/// <summary>
/// Pure business logic for generating HTML representations of player registration data.
/// Builds tables and warnings for family players and automated recurring billing.
/// </summary>
public static class PlayerHtmlGenerator
{
    /// <summary>
    /// Represents the fixed fields from a player registration record used for HTML generation.
    /// This is a data transfer structure to avoid framework dependencies.
    /// </summary>
    public class PlayerRegistrationData
    {
        public string? Person { get; set; }
        public string? Assignment { get; set; }
        public bool? Active { get; set; }
        public decimal? FeeTotal { get; set; }
        public decimal? PaidTotal { get; set; }
        public decimal? OwedTotal { get; set; }
        public string? AdnSubscriptionId { get; set; }
        public string? AdnSubscriptionStatus { get; set; }
        public int? AdnSubscriptionBillingOccurences { get; set; }
        public decimal? AdnSubscriptionAmountPerOccurence { get; set; }
        public DateTime? AdnSubscriptionStartDate { get; set; }
        public int? AdnSubscriptionIntervalLength { get; set; }
    }

    /// <summary>
    /// Builds an HTML warning message for inactive players who are not registered.
    /// </summary>
    /// <param name="registrations">List of player registration data.</param>
    /// <returns>HTML string with warning message, or empty string if no inactive players.</returns>
    public static string BuildInactivePlayersHtml(List<PlayerRegistrationData> registrations)
    {
        var inactive = registrations.Where(q => q.Active != true && string.IsNullOrEmpty(q.AdnSubscriptionId)).ToList();
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

    /// <summary>
    /// Builds an HTML table showing family players with fees, payments, and balances.
    /// </summary>
    /// <param name="registrations">List of player registration data.</param>
    /// <param name="emailMode">True for email-compatible inline styles, false for CSS classes.</param>
    /// <returns>HTML table string, or empty string if no registrations.</returns>
    public static string BuildPlayersTableHtml(List<PlayerRegistrationData> registrations, bool emailMode)
    {
        if (registrations.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        HtmlTableBuilder.StartTable(sb, emailMode);
        HtmlTableBuilder.AddCaption(sb, "Family Players", emailMode);
        HtmlTableBuilder.StartHead(sb);
        HtmlTableBuilder.AddHeaderRow(sb, "Player", "Status", "Assignment", "Fees$", "Paid$", "Owes$");
        HtmlTableBuilder.EndHeadStartBody(sb);

        decimal feesSum = 0m, paidSum = 0m, owesSum = 0m;

        foreach (var q in registrations)
        {
            var status = (string.IsNullOrEmpty(q.AdnSubscriptionId) && q.Active != true) ? "INACTIVE" : "ACTIVE";
            var fees = q.FeeTotal ?? 0m;
            var paid = q.PaidTotal ?? 0m;
            var owes = q.OwedTotal ?? 0m;

            feesSum += fees;
            paidSum += paid;
            owesSum += owes;

            HtmlTableBuilder.AddRow(sb,
                WebUtility.HtmlEncode(q.Person ?? string.Empty),
                status,
                WebUtility.HtmlEncode(q.Assignment ?? string.Empty),
                HtmlTableBuilder.FormatCurrency(fees),
                HtmlTableBuilder.FormatCurrency(paid),
                HtmlTableBuilder.FormatCurrency(owes));
        }

        HtmlTableBuilder.EndBodyStartFoot(sb);
        HtmlTableBuilder.AddFooterRow(sb, "Totals", string.Empty, string.Empty,
            HtmlTableBuilder.FormatCurrency(feesSum),
            HtmlTableBuilder.FormatCurrency(paidSum),
            HtmlTableBuilder.FormatCurrency(owesSum));
        HtmlTableBuilder.EndFootEndTable(sb);

        return sb.ToString();
    }

    /// <summary>
    /// Builds an HTML table showing automated recurring billing subscriptions.
    /// </summary>
    /// <param name="registrations">List of player registration data.</param>
    /// <param name="emailMode">True for email-compatible inline styles, false for CSS classes.</param>
    /// <returns>HTML table string, or empty string if no ARB subscriptions.</returns>
    public static string BuildArbTableHtml(List<PlayerRegistrationData> registrations, bool emailMode)
    {
        if (registrations.Count == 0) return string.Empty;

        var first = registrations[0];
        if (first.AdnSubscriptionAmountPerOccurence is not > 0) return string.Empty;

        var sb = new StringBuilder();
        HtmlTableBuilder.StartTable(sb, emailMode);
        HtmlTableBuilder.AddCaption(sb, "Automated Recurring Billing", emailMode);
        HtmlTableBuilder.StartHead(sb);
        HtmlTableBuilder.AddHeaderRow(sb, "Player", "Sub. Id", "Status", "Starting", "#Billings", "Frequency", "Charge/Billing", "Total Charges");
        HtmlTableBuilder.EndHeadStartBody(sb);

        decimal totalAll = 0m;

        foreach (var q in registrations)
        {
            var intervalLabel = (q.AdnSubscriptionIntervalLength ?? 0) > 1 ? "months" : "month";
            var totalCharges = (q.AdnSubscriptionAmountPerOccurence ?? 0m) * (q.AdnSubscriptionBillingOccurences ?? 0);
            totalAll += totalCharges;

            HtmlTableBuilder.AddRow(sb,
                WebUtility.HtmlEncode(q.Person ?? string.Empty),
                q.AdnSubscriptionId ?? string.Empty,
                q.AdnSubscriptionStatus ?? string.Empty,
                q.AdnSubscriptionStartDate?.ToString("d") ?? string.Empty,
                (q.AdnSubscriptionBillingOccurences ?? 0).ToString(),
                $"every {q.AdnSubscriptionIntervalLength} {intervalLabel}",
                HtmlTableBuilder.FormatCurrency(q.AdnSubscriptionAmountPerOccurence ?? 0m),
                HtmlTableBuilder.FormatCurrency(totalCharges));
        }

        HtmlTableBuilder.EndBodyStartFoot(sb);
        HtmlTableBuilder.AddFooterRow(sb, "Total", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, HtmlTableBuilder.FormatCurrency(totalAll));
        HtmlTableBuilder.EndFootEndTable(sb);

        return sb.ToString();
    }
}



