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

    // Girls Overall default params
    private const string DefaultVersion = "20";
    private const string DefaultYear = "2025";

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

    public async Task<List<AgeGroupOptionDto>> GetAvailableAgeGroupsAsync(CancellationToken ct = default)
    {
        try
        {
            var html = await _http.GetStringAsync(BaseUrl + RankingsPath, ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Find age group links matching Girls rankings pattern
            var nodes = doc.DocumentNode.SelectNodes(
                $"//a[contains(@href, 'v={DefaultVersion}') and contains(@href, 'yr={DefaultYear}')]");

            if (nodes is null)
                return [];

            var seen = new HashSet<string>();
            var results = new List<AgeGroupOptionDto>();

            foreach (var node in nodes)
            {
                var text = node.InnerText?.Trim();
                var href = WebUtility.HtmlDecode(node.GetAttributeValue("href", ""));

                if (string.IsNullOrEmpty(text) || !text.Contains("Girls") || !seen.Add(href))
                    continue;

                results.Add(new AgeGroupOptionDto { Value = href, Text = text });
            }

            _logger.LogInformation("Scraped {Count} Girls age groups from usclublax.com", results.Count);
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
        var ageGroupLabel = $"Girls {yr}";
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

        return link?.InnerText?.Trim() ?? cell.InnerText?.Trim() ?? string.Empty;
    }

    private static string CellText(HtmlNodeCollection cells, int col) =>
        cells.Count > col ? cells[col].InnerText?.Trim() ?? string.Empty : string.Empty;

    private static decimal CellDecimal(HtmlNodeCollection cells, int col) =>
        cells.Count > col && decimal.TryParse(cells[col].InnerText?.Trim(), out var val) ? val : 0m;

    private static string ExtractDigits(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : new string(text.Where(char.IsDigit).ToArray());

    private static ScrapeResultDto FailResult(string ageGroup, string error) => new()
    {
        Success = false,
        AgeGroup = ageGroup,
        LastUpdated = DateTime.UtcNow,
        ErrorMessage = error,
        Rankings = []
    };
}
