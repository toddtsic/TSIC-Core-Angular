using System.Net;
using System.Text;
using TSIC.Application.Services.Shared.Html;

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
    public static string BuildInactivePlayersHtml(List<PlayerRegistrationData> registrations, bool emailMode)
    {
        var inactive = registrations.Where(q => q.Active != true && string.IsNullOrEmpty(q.AdnSubscriptionId)).ToList();
        if (inactive.Count == 0) return string.Empty;

        var inner = new StringBuilder();
        inner.Append("<p>The following players are INACTIVE and are considered NOT REGISTERED</p><ul>");
        foreach (var i in inactive)
            inner.Append($"<li>{WebUtility.HtmlEncode(i.Person)} ({WebUtility.HtmlEncode(i.Assignment)})</li>");
        inner.Append("</ul><p>The player(s) above will be considered registered ONLY after they are PAID IN FULL.</p>")
             .Append("<p>Unpaid players are subject to being dropped by the program director.</p>");

        return HtmlTableBuilder.RenderWarningBlock(inner.ToString(), emailMode);
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
        var table = new HtmlTable(sb, emailMode, "Family Players");
        table.HeaderRow("Player", "Status", "Assignment",
            HtmlTableBuilder.Num("Fees$"), HtmlTableBuilder.Num("Paid$"), HtmlTableBuilder.Num("Owes$"));

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

            table.Row(
                WebUtility.HtmlEncode(q.Person ?? string.Empty),
                status,
                WebUtility.HtmlEncode(q.Assignment ?? string.Empty),
                HtmlTableBuilder.FormatCurrency(fees),
                HtmlTableBuilder.FormatCurrency(paid),
                HtmlTableBuilder.FormatCurrency(owes));
        }

        table.FooterRow("Totals", string.Empty, string.Empty,
            HtmlTableBuilder.FormatCurrency(feesSum),
            HtmlTableBuilder.FormatCurrency(paidSum),
            HtmlTableBuilder.FormatCurrency(owesSum));
        table.End();

        return sb.ToString();
    }

    /// <summary>
    /// Maps raw Authorize.Net ARB statuses to registrant-facing labels. "expired" is ADN's term
    /// for a subscription that completed all scheduled billings — rendering it verbatim reads as
    /// a problem when it is actually success.
    /// </summary>
    internal static string ArbStatusLabel(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "expired" => "Completed",
        "active" => "Active",
        "suspended" => "Suspended — payment issue",
        "terminated" or "canceled" or "cancelled" => "Canceled",
        null or "" => string.Empty,
        _ => status.Trim(),
    };

    /// <summary>
    /// Builds an HTML table showing automated recurring billing subscriptions.
    /// </summary>
    /// <param name="registrations">List of player registration data.</param>
    /// <param name="emailMode">True for email-compatible inline styles, false for CSS classes.</param>
    /// <returns>HTML table string, or empty string if no ARB subscriptions.</returns>
    public static string BuildArbTableHtml(List<PlayerRegistrationData> registrations, bool emailMode)
    {
        // Only rows that actually carry a subscription — a family can mix ARB and paid-in-full
        // siblings, and sub-less rows previously rendered as junk ("every 0 month", $0.00).
        // Gating on ANY subscription row (not registrations[0]) also fixes the family whose
        // first registration happens to be the non-ARB one.
        var subs = registrations
            .Where(q => !string.IsNullOrEmpty(q.AdnSubscriptionId) && (q.AdnSubscriptionAmountPerOccurence ?? 0m) > 0m)
            .ToList();
        if (subs.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        var table = new HtmlTable(sb, emailMode, "Automated Recurring Billing");
        table.HeaderRow("Player", "Sub. Id", "Status", "Starting",
            HtmlTableBuilder.Num("#Billings"), "Frequency",
            HtmlTableBuilder.Num("Charge/Billing"), HtmlTableBuilder.Num("Total Charges"));

        decimal totalAll = 0m;

        foreach (var q in subs)
        {
            var intervalLabel = (q.AdnSubscriptionIntervalLength ?? 0) > 1 ? "months" : "month";
            var totalCharges = (q.AdnSubscriptionAmountPerOccurence ?? 0m) * (q.AdnSubscriptionBillingOccurences ?? 0);
            totalAll += totalCharges;

            table.Row(
                WebUtility.HtmlEncode(q.Person ?? string.Empty),
                q.AdnSubscriptionId ?? string.Empty,
                WebUtility.HtmlEncode(ArbStatusLabel(q.AdnSubscriptionStatus)),
                q.AdnSubscriptionStartDate?.ToString("d") ?? string.Empty,
                HtmlTableBuilder.Num((q.AdnSubscriptionBillingOccurences ?? 0).ToString()),
                $"every {q.AdnSubscriptionIntervalLength} {intervalLabel}",
                HtmlTableBuilder.FormatCurrency(q.AdnSubscriptionAmountPerOccurence ?? 0m),
                HtmlTableBuilder.FormatCurrency(totalCharges));
        }

        table.FooterRow("Total", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, HtmlTableBuilder.FormatCurrency(totalAll));
        table.End();

        return sb.ToString();
    }
}
