using System.Net;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using TSIC.Contracts.Dtos.Rankings;
using TSIC.Contracts.Services;

namespace TSIC.Infrastructure.Services;

/// <summary>
/// Scrapes Girls National rankings from usclublax.com using HtmlAgilityPack.
/// Thin service — parse HTML, return DTOs. No business logic.
/// </summary>
public sealed class USLaxScrapingService : IUSLaxScrapingService
{
    private readonly HttpClient _http;
    private readonly ILogger<USLaxScrapingService> _logger;

    private const string BaseUrl = "https://www.usclublax.com";
    private const string RankingsPath = "/rankings";
    private const string RankPath = "/rank";

    // Girls Overall. The trailing two digits of v are the graduation class and shift
    // with the season, so only the family prefix is safe to pin:
    //   10xx = boys overall   11xx = boys national
    //   20xx = girls overall  21xx = girls national
    private const string DefaultVersion = "20";

    // Fallback only, for a link whose href somehow carries no yr. The season is never
    // pinned -- /rankings serves whatever season the site currently publishes, and
    // /rankings/?yr=N serves that archived season.
    private const string FallbackYear = "2025";

    // Hardcoded column mapping matching usclublax.com 2025 table structure
    private const int ColRank = 0;
    private const int ColTeam = 1;
    private const int ColState = 2;
    private const int ColRecord = 3;
    private const int ColRating = 4;
    private const int ColAgd = 5;
    private const int ColSched = 6;
    private const int MinColumns = 3;

    public USLaxScrapingService(HttpClient http, ILogger<USLaxScrapingService> logger)
    {
        _http = http;
        _logger = logger;

        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _http.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }
    }

    public async Task<List<RankingSeasonDto>> GetAvailableSeasonsAsync(CancellationToken ct = default)
    {
        try
        {
            var html = await _http.GetStringAsync(BaseUrl + RankingsPath, ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // The season tab strip: <a class="rank-season-tab [is-active]" href="/rankings/?yr=2025">2025-26</a>
            var nodes = doc.DocumentNode.SelectNodes(
                "//a[contains(@class, 'rank-season-tab')]");

            if (nodes is null)
            {
                _logger.LogWarning("No season tabs found on {Path}", RankingsPath);
                return [];
            }

            var seen = new HashSet<string>();
            var results = new List<RankingSeasonDto>();

            foreach (var node in nodes)
            {
                var href = WebUtility.HtmlDecode(node.GetAttributeValue("href", ""));
                var yr = ParseQueryString(href).GetValueOrDefault("yr", "");
                if (string.IsNullOrEmpty(yr) || !seen.Add(yr))
                    continue;

                var css = node.GetAttributeValue("class", "");
                results.Add(new RankingSeasonDto
                {
                    Value = yr,
                    Text = GetAgeGroupLabel(node),
                    IsCurrent = css.Contains("is-active")
                });
            }

            // Newest first. If the site ever drops the is-active marker, fall back to
            // the highest season so a default can still be chosen.
            results = [.. results.OrderByDescending(s => s.Value, StringComparer.Ordinal)];
            if (results.Count > 0 && !results.Any(s => s.IsCurrent))
            {
                _logger.LogWarning("No season tab marked is-active; defaulting to newest {Season}", results[0].Value);
                results[0] = results[0] with { IsCurrent = true };
            }

            _logger.LogInformation("Scraped {Count} seasons from usclublax.com", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching seasons from usclublax.com");
            return [];
        }
    }

    public async Task<List<AgeGroupOptionDto>> GetAvailableAgeGroupsAsync(string? yr = null, CancellationToken ct = default)
    {
        try
        {
            // No yr => the season the site currently serves. The season is never pinned:
            // usclublax rolls annually, and the set of age groups differs season to season
            // (older seasons publish 12 groups where the current one publishes 28).
            var path = string.IsNullOrWhiteSpace(yr)
                ? BaseUrl + RankingsPath
                : $"{BaseUrl}{RankingsPath}/?yr={Uri.EscapeDataString(yr)}";

            var html = await _http.GetStringAsync(path, ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Girls links for whatever season this page is serving. Only the v family is
            // pinned; the season comes back off the href below.
            var nodes = doc.DocumentNode.SelectNodes(
                $"//a[contains(@href, 'v={DefaultVersion}')]");

            if (nodes is null)
                return [];

            var seen = new HashSet<string>();
            var results = new List<AgeGroupOptionDto>();

            foreach (var node in nodes)
            {
                var text = GetAgeGroupLabel(node);
                var href = WebUtility.HtmlDecode(node.GetAttributeValue("href", ""));

                if (string.IsNullOrEmpty(text) || !text.Contains("Girls") || !seen.Add(href))
                    continue;

                // Parse query parameters from href (e.g., "/rank?v=2027&alpha=N&yr=2025")
                // Return "v|alpha|yr" format for frontend consumption
                var queryParams = ParseQueryString(href);
                var v = queryParams.GetValueOrDefault("v", DefaultVersion);
                var alpha = queryParams.GetValueOrDefault("alpha", "N");
                var season = queryParams.GetValueOrDefault("yr", yr ?? FallbackYear);
                var value = $"{v}|{alpha}|{season}";

                results.Add(new AgeGroupOptionDto { Value = value, Text = text });
            }

            _logger.LogInformation(
                "Scraped {Count} Girls age groups from usclublax.com for season {Season}",
                results.Count, yr ?? "(current)");
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching available age groups from usclublax.com");
            return [];
        }
    }

    public async Task<ScrapeResultDto> ScrapeRankingsAsync(string v, string alpha, string yr, CancellationToken ct = default)
    {
        var ageGroupLabel = DescribeAgeGroup(v, yr);
        try
        {
            var url = $"{BaseUrl}{RankPath}?v={v}&alpha={alpha}&yr={yr}";
            _logger.LogInformation("Scraping rankings from {Url}", url);

            var html = await _http.GetStringAsync(url, ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var table = doc.DocumentNode.SelectSingleNode(
                "//div[contains(@class, 'desc-container-table')]//table");

            if (table is null)
            {
                _logger.LogWarning("No rankings table found for {AgeGroup}", ageGroupLabel);
                return FailResult(ageGroupLabel, "Could not find rankings table on the page");
            }

            var rankings = ParseTable(table);
            _logger.LogInformation("Scraped {Count} rankings for {AgeGroup}", rankings.Count, ageGroupLabel);

            return new ScrapeResultDto
            {
                Success = true,
                AgeGroup = ageGroupLabel,
                LastUpdated = DateTime.UtcNow,
                Rankings = rankings
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error scraping rankings for {AgeGroup}", ageGroupLabel);
            return FailResult(ageGroupLabel, $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scraping rankings for {AgeGroup}", ageGroupLabel);
            return FailResult(ageGroupLabel, $"Error scraping rankings: {ex.Message}");
        }
    }

    private List<RankingEntryDto> ParseTable(HtmlNode table)
    {
        var rows = table.SelectNodes(".//tr");
        if (rows is null || rows.Count < 2)
            return [];

        var rankings = new List<RankingEntryDto>(rows.Count - 1);

        // Skip header row (index 0)
        for (var i = 1; i < rows.Count; i++)
        {
            var cells = rows[i].SelectNodes("td");
            if (cells is null || cells.Count < MinColumns)
                continue;

            var entry = ParseRow(cells);
            if (entry is not null)
                rankings.Add(entry);
        }

        return rankings;
    }

    private static RankingEntryDto? ParseRow(HtmlNodeCollection cells)
    {
        // Rank (column 0)
        var rankText = cells.Count > ColRank ? cells[ColRank].InnerText?.Trim() : null;
        if (!int.TryParse(ExtractDigits(rankText), out var rank) || rank == 0)
            return null;

        // Team name (column 1) — nested: span.uscl-team-cell__body > a
        var teamName = ExtractTeamName(cells.Count > ColTeam ? cells[ColTeam] : null);
        if (string.IsNullOrWhiteSpace(teamName))
            return null;

        return new RankingEntryDto
        {
            Rank = rank,
            Team = WebUtility.HtmlDecode(teamName),
            State = CellText(cells, ColState),
            Record = CellText(cells, ColRecord),
            Rating = CellDecimal(cells, ColRating),
            Agd = CellDecimal(cells, ColAgd),
            Sched = CellDecimal(cells, ColSched)
        };
    }

    private static string ExtractTeamName(HtmlNode? cell)
    {
        if (cell is null) return string.Empty;

        // Try structured selector first
        var link = cell.SelectSingleNode(".//span[contains(@class, 'uscl-team-cell__body')]//a")
                   ?? cell.SelectSingleNode(".//a");

        // Decode entities -- the site writes "&" as "&amp;" and "'" as "&#039;", and the
        // raw form degrades the fuzzy match against our registered team names.
        var raw = link?.InnerText ?? cell.InnerText ?? string.Empty;
        return WebUtility.HtmlDecode(raw).Trim();
    }

    private static string CellText(HtmlNodeCollection cells, int col) =>
        cells.Count > col ? cells[col].InnerText?.Trim() ?? string.Empty : string.Empty;

    private static decimal CellDecimal(HtmlNodeCollection cells, int col) =>
        cells.Count > col && decimal.TryParse(cells[col].InnerText?.Trim(), out var val) ? val : 0m;

    private static string ExtractDigits(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : new string(text.Where(char.IsDigit).ToArray());

    /// <summary>
    /// usclublax.com renders each rankings link with the label twice -- a mobile copy and a
    /// desktop copy -- followed by a chevron span holding an undecoded HTML entity. So
    /// HtmlAgilityPack's InnerText concatenates all three and yields
    /// "Girls 2027/U17/VarsityGirls 2027/U17/Varsity" plus a trailing entity. The title
    /// attribute carries the label exactly once; fall back to the desktop copy, then to
    /// raw InnerText.
    /// </summary>
    private static string GetAgeGroupLabel(HtmlNode node)
    {
        var title = WebUtility.HtmlDecode(node.GetAttributeValue("title", "")).Trim();
        if (!string.IsNullOrEmpty(title))
            return title;

        var desktopCopy = node.SelectSingleNode(".//span[contains(@class, 'uscl-rankings-nav__title')]");
        var raw = desktopCopy?.InnerText ?? node.InnerText ?? "";
        return WebUtility.HtmlDecode(raw).Trim().TrimEnd('\u203A').Trim();
    }

    /// <summary>
    /// Builds a display label from the scrape parameters. v is {family}{class}, e.g.
    /// 2027 = girls overall class of 2027, 2127 = girls national class of 2027; yr is
    /// the SEASON, not the class -- yr=2025 is the 2025-26 season.
    /// </summary>
    private static string DescribeAgeGroup(string v, string yr)
    {
        var season = int.TryParse(yr, out var y) ? $"{y}-{(y + 1) % 100:D2}" : yr;

        if (v.Length != 4)
            return $"Rankings {v} ({season})";

        var gender = v[0] == '1' ? "Boys" : "Girls";
        var tier = v[1] == '1' ? " National" : "";
        return $"{gender} 20{v[2..]}{tier} ({season})";
    }

    private static Dictionary<string, string> ParseQueryString(string href)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var qIndex = href.IndexOf('?');
        if (qIndex < 0) return result;

        var query = href[(qIndex + 1)..];
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex > 0)
            {
                var key = pair[..eqIndex];
                var val = eqIndex < pair.Length - 1 ? pair[(eqIndex + 1)..] : "";
                result[key] = val;
            }
        }
        return result;
    }

    private static ScrapeResultDto FailResult(string ageGroup, string error) => new()
    {
        Success = false,
        AgeGroup = ageGroup,
        LastUpdated = DateTime.UtcNow,
        ErrorMessage = error,
        Rankings = []
    };
}
