using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Widgets;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Repositories.Shared;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for widget dashboard data access.
/// </summary>
public class WidgetRepository : IWidgetRepository
{
    private readonly SqlDbContext _context;

    public WidgetRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<WidgetItemProjection>> GetDefaultsAsync(
        int jobTypeId,
        string? roleId,
        CancellationToken ct = default)
    {
        return await _context.WidgetDefault
            .AsNoTracking()
            .Where(wd => wd.JobTypeId == jobTypeId
                      && (wd.RoleId == null || wd.RoleId == roleId))
            .OrderBy(wd => wd.Category.Workspace)
            .ThenBy(wd => wd.Category.DefaultOrder)
            .ThenBy(wd => wd.DisplayOrder)
            .Select(wd => new WidgetItemProjection
            {
                WidgetId = wd.WidgetId,
                CategoryId = wd.CategoryId,
                DisplayOrder = wd.DisplayOrder,
                Config = wd.Config ?? wd.Widget.DefaultConfig,
                IsEnabled = true,
                WidgetName = wd.Widget.Name,
                WidgetType = wd.Widget.WidgetType,
                ComponentKey = wd.Widget.ComponentKey,
                Description = wd.Widget.Description,
                CategoryName = wd.Category.Name,
                CategoryIcon = wd.Category.Icon,
                CategoryDefaultOrder = wd.Category.DefaultOrder,
                Workspace = wd.Category.Workspace
            })
            .ToListAsync(ct);
    }

    public async Task<List<WidgetItemProjection>> GetJobWidgetsAsync(
        Guid jobId,
        string? roleId,
        CancellationToken ct = default)
    {
        return await _context.JobWidget
            .AsNoTracking()
            .Where(jw => jw.JobId == jobId
                      && (jw.RoleId == null || jw.RoleId == roleId))
            .OrderBy(jw => jw.Category.Workspace)
            .ThenBy(jw => jw.Category.DefaultOrder)
            .ThenBy(jw => jw.DisplayOrder)
            .Select(jw => new WidgetItemProjection
            {
                WidgetId = jw.WidgetId,
                CategoryId = jw.CategoryId,
                DisplayOrder = jw.DisplayOrder,
                Config = jw.Config ?? jw.Widget.DefaultConfig,
                IsEnabled = jw.IsEnabled,
                WidgetName = jw.Widget.Name,
                WidgetType = jw.Widget.WidgetType,
                ComponentKey = jw.Widget.ComponentKey,
                Description = jw.Widget.Description,
                CategoryName = jw.Category.Name,
                CategoryIcon = jw.Category.Icon,
                CategoryDefaultOrder = jw.Category.DefaultOrder,
                Workspace = jw.Category.Workspace
            })
            .ToListAsync(ct);
    }

    public async Task<int?> GetJobTypeIdAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => (int?)j.JobTypeId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<DashboardMetricsDto> GetDashboardMetricsAsync(Guid jobId, CancellationToken ct = default)
    {
        // Registration + financial aggregates — single GroupBy query
        var regStats = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId)
            .GroupBy(r => 1)
            .Select(g => new
            {
                TotalActive = g.Count(r => r.BActive == true),
                TotalInactive = g.Count(r => r.BActive != true),
                TotalFees = g.Where(r => r.BActive == true).Sum(r => r.FeeTotal),
                TotalPaid = g.Where(r => r.BActive == true).Sum(r => r.PaidTotal),
                TotalOwed = g.Where(r => r.BActive == true).Sum(r => r.OwedTotal),
                PaidInFull = g.Count(r => r.BActive == true && r.OwedTotal == 0),
                Underpaid = g.Count(r => r.BActive == true && r.OwedTotal > 0),
                OverPaid = g.Count(r => r.BActive == true && r.OwedTotal < 0),
            })
            .FirstOrDefaultAsync(ct);

        // Team count — sequential (shared DbContext)
        var teamCount = await _context.Teams
            .AsNoTracking()
            .CountAsync(t => t.JobId == jobId && t.Active == true, ct);

        // Club count — distinct ClubName from active registrations
        var clubCount = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.BActive == true && r.ClubName != null && r.ClubName != "")
            .Select(r => r.ClubName)
            .Distinct()
            .CountAsync(ct);

        return new DashboardMetricsDto
        {
            Registrations = new RegistrationMetrics
            {
                TotalActive = regStats?.TotalActive ?? 0,
                TotalInactive = regStats?.TotalInactive ?? 0,
                Teams = teamCount,
                Clubs = clubCount,
            },
            Financials = new FinancialMetrics
            {
                TotalFees = regStats?.TotalFees ?? 0,
                TotalPaid = regStats?.TotalPaid ?? 0,
                TotalOwed = regStats?.TotalOwed ?? 0,
                PaidInFull = regStats?.PaidInFull ?? 0,
                Underpaid = regStats?.Underpaid ?? 0,
                OverPaid = regStats?.OverPaid ?? 0,
            },
            Scheduling = new SchedulingMetrics
            {
                TotalAgegroups = 0,
                AgegroupsScheduled = 0,
                FieldCount = 0,
                TotalDivisions = 0,
            }
        };
    }

    public async Task<RegistrationTimeSeriesDto> GetRegistrationTimeSeriesAsync(Guid jobId, CancellationToken ct = default)
    {
        // Daily aggregates — group active registrations by date
        var dailyRaw = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.BActive == true)
            .GroupBy(r => r.RegistrationTs.Date)
            .Select(g => new
            {
                Date = g.Key,
                Count = g.Count(),
                Revenue = g.Sum(r => r.PaidTotal),
            })
            .OrderBy(x => x.Date)
            .ToListAsync(ct);

        // Summary aggregates — single pass
        var summary = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.BActive == true)
            .GroupBy(r => 1)
            .Select(g => new
            {
                Total = g.Count(),
                TotalRevenue = g.Sum(r => r.PaidTotal),
                TotalOutstanding = g.Sum(r => r.OwedTotal),
                PaidInFull = g.Count(r => r.OwedTotal == 0),
                Underpaid = g.Count(r => r.OwedTotal > 0),
                OverPaid = g.Count(r => r.OwedTotal < 0),
            })
            .FirstOrDefaultAsync(ct);

        // Build cumulative totals in-memory (cheap — daily buckets are small)
        var cumulativeCount = 0;
        var cumulativeRevenue = 0m;
        var dailyData = dailyRaw.Select(d =>
        {
            cumulativeCount += d.Count;
            cumulativeRevenue += d.Revenue;
            return new DailyRegistrationPointDto
            {
                Date = d.Date,
                Count = d.Count,
                CumulativeCount = cumulativeCount,
                Revenue = d.Revenue,
                CumulativeRevenue = cumulativeRevenue,
            };
        }).ToList();

        return new RegistrationTimeSeriesDto
        {
            DailyData = dailyData,
            Summary = new RegistrationTrendSummaryDto
            {
                TotalRegistrations = summary?.Total ?? 0,
                TotalRevenue = summary?.TotalRevenue ?? 0,
                TotalOutstanding = summary?.TotalOutstanding ?? 0,
                PaidInFull = summary?.PaidInFull ?? 0,
                Underpaid = summary?.Underpaid ?? 0,
                OverPaid = summary?.OverPaid ?? 0,
            }
        };
    }

    public async Task<RegistrationTimeSeriesDto> GetPlayerTimeSeriesAsync(Guid jobId, CancellationToken ct = default)
    {
        // Daily player registration aggregates — RoleId = Player, active only
        var dailyRaw = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.BActive == true && r.RoleId == RoleConstants.Player)
            .GroupBy(r => r.RegistrationTs.Date)
            .Select(g => new
            {
                Date = g.Key,
                Count = g.Count(),
                Revenue = g.Sum(r => r.PaidTotal),
            })
            .OrderBy(x => x.Date)
            .ToListAsync(ct);

        // Summary aggregates for players
        var summary = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.BActive == true && r.RoleId == RoleConstants.Player)
            .GroupBy(r => 1)
            .Select(g => new
            {
                Total = g.Count(),
                TotalRevenue = g.Sum(r => r.PaidTotal),
                TotalOutstanding = g.Sum(r => r.OwedTotal),
                PaidInFull = g.Count(r => r.OwedTotal == 0),
                Underpaid = g.Count(r => r.OwedTotal > 0),
                OverPaid = g.Count(r => r.OwedTotal < 0),
            })
            .FirstOrDefaultAsync(ct);

        var cumulativeCount = 0;
        var cumulativeRevenue = 0m;
        var dailyData = dailyRaw.Select(d =>
        {
            cumulativeCount += d.Count;
            cumulativeRevenue += d.Revenue;
            return new DailyRegistrationPointDto
            {
                Date = d.Date,
                Count = d.Count,
                CumulativeCount = cumulativeCount,
                Revenue = d.Revenue,
                CumulativeRevenue = cumulativeRevenue,
            };
        }).ToList();

        return new RegistrationTimeSeriesDto
        {
            DailyData = dailyData,
            Summary = new RegistrationTrendSummaryDto
            {
                TotalRegistrations = summary?.Total ?? 0,
                TotalRevenue = summary?.TotalRevenue ?? 0,
                TotalOutstanding = summary?.TotalOutstanding ?? 0,
                PaidInFull = summary?.PaidInFull ?? 0,
                Underpaid = summary?.Underpaid ?? 0,
                OverPaid = summary?.OverPaid ?? 0,
            }
        };
    }

    public async Task<RegistrationTimeSeriesDto> GetTeamTimeSeriesAsync(Guid jobId, CancellationToken ct = default)
    {
        // Daily team aggregates — teams with ClubRep payment, active only
        // Uses Teams.Createdate for timing, Teams financial fields for revenue
        var dailyRaw = await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId && t.Active == true && t.ClubrepRegistrationid != null)
            .GroupBy(t => t.Createdate.Date)
            .Select(g => new
            {
                Date = g.Key,
                Count = g.Count(),
                Revenue = g.Sum(t => t.PaidTotal ?? 0m),
            })
            .OrderBy(x => x.Date)
            .ToListAsync(ct);

        // Summary aggregates for teams
        var summary = await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId && t.Active == true && t.ClubrepRegistrationid != null)
            .GroupBy(t => 1)
            .Select(g => new
            {
                Total = g.Count(),
                TotalRevenue = g.Sum(t => t.PaidTotal ?? 0m),
                TotalOutstanding = g.Sum(t => t.OwedTotal ?? 0m),
                PaidInFull = g.Count(t => (t.OwedTotal ?? 0m) == 0m),
                Underpaid = g.Count(t => (t.OwedTotal ?? 0m) > 0m),
                OverPaid = g.Count(t => (t.OwedTotal ?? 0m) < 0m),
            })
            .FirstOrDefaultAsync(ct);

        var cumulativeCount = 0;
        var cumulativeRevenue = 0m;
        var dailyData = dailyRaw.Select(d =>
        {
            cumulativeCount += d.Count;
            cumulativeRevenue += d.Revenue;
            return new DailyRegistrationPointDto
            {
                Date = d.Date,
                Count = d.Count,
                CumulativeCount = cumulativeCount,
                Revenue = d.Revenue,
                CumulativeRevenue = cumulativeRevenue,
            };
        }).ToList();

        return new RegistrationTimeSeriesDto
        {
            DailyData = dailyData,
            Summary = new RegistrationTrendSummaryDto
            {
                TotalRegistrations = summary?.Total ?? 0,
                TotalRevenue = summary?.TotalRevenue ?? 0m,
                TotalOutstanding = summary?.TotalOutstanding ?? 0m,
                PaidInFull = summary?.PaidInFull ?? 0,
                Underpaid = summary?.Underpaid ?? 0,
                OverPaid = summary?.OverPaid ?? 0,
            }
        };
    }

    public async Task<AgegroupDistributionDto> GetAgegroupDistributionAsync(Guid jobId, CancellationToken ct = default)
    {
        // Player counts per age group (derived from assigned team)
        var playersByAg = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.BActive == true
                     && r.RoleId == RoleConstants.Player
                     && r.AssignedTeamId != null)
            .Join(_context.Teams.AsNoTracking(),
                  r => r.AssignedTeamId,
                  t => t.TeamId,
                  (r, t) => t.AgegroupId)
            .GroupBy(agId => agId)
            .Select(g => new { AgegroupId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Revenue per age group (sum of PaidTotal for players with assigned teams)
        var revenueByAg = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.BActive == true
                     && r.RoleId == RoleConstants.Player
                     && r.AssignedTeamId != null)
            .Join(_context.Teams.AsNoTracking(),
                  r => r.AssignedTeamId,
                  t => t.TeamId,
                  (r, t) => new { t.AgegroupId, r.PaidTotal })
            .GroupBy(x => x.AgegroupId)
            .Select(g => new { AgegroupId = g.Key, Revenue = g.Sum(x => x.PaidTotal) })
            .ToListAsync(ct);

        // Team counts per age group
        var teamsByAg = await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId && t.Active == true)
            .GroupBy(t => t.AgegroupId)
            .Select(g => new { AgegroupId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Agegroup name lookup for all referenced IDs
        var allAgIds = playersByAg.Select(p => p.AgegroupId)
            .Union(teamsByAg.Select(t => t.AgegroupId))
            .Distinct()
            .ToList();

        var agNames = await _context.Agegroups
            .AsNoTracking()
            .Where(ag => allAgIds.Contains(ag.AgegroupId))
            .Select(ag => new { ag.AgegroupId, ag.AgegroupName })
            .ToDictionaryAsync(ag => ag.AgegroupId, ag => ag.AgegroupName ?? "Unknown", ct);

        // Merge into distribution points
        var playerLookup = playersByAg.ToDictionary(p => p.AgegroupId, p => p.Count);
        var teamLookup = teamsByAg.ToDictionary(t => t.AgegroupId, t => t.Count);
        var revenueLookup = revenueByAg.ToDictionary(r => r.AgegroupId, r => r.Revenue);

        var points = allAgIds
            .Select(id => new AgegroupDistributionPointDto
            {
                AgegroupName = agNames.GetValueOrDefault(id, "Unknown"),
                PlayerCount = playerLookup.GetValueOrDefault(id, 0),
                TeamCount = teamLookup.GetValueOrDefault(id, 0),
                Revenue = revenueLookup.GetValueOrDefault(id, 0m),
            })
            .OrderBy(p => p.AgegroupName)
            .ToList();

        return new AgegroupDistributionDto
        {
            Agegroups = points,
            TotalPlayers = playersByAg.Sum(p => p.Count),
            TotalTeams = teamsByAg.Sum(t => t.Count),
            TotalRevenue = revenueByAg.Sum(r => r.Revenue),
        };
    }

    public async Task<EventContactDto?> GetEventContactAsync(Guid jobId, CancellationToken ct = default)
    {
        // Prefer explicitly-set primary contact
        var primaryContactId = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.PrimaryContactRegistrationId)
            .FirstOrDefaultAsync(ct);

        if (primaryContactId != null)
        {
            var explicit_ = await _context.Registrations
                .AsNoTracking()
                .Where(r => r.RegistrationId == primaryContactId && r.BActive == true)
                .Select(r => new EventContactDto
                {
                    FirstName = r.User!.FirstName ?? "",
                    LastName = r.User.LastName ?? "",
                    Email = r.User.Email ?? "",
                })
                .FirstOrDefaultAsync(ct);

            if (explicit_ != null)
                return explicit_;
        }

        // Fallback: earliest-registered active DIRECTOR.
        // Director ONLY (Todd ruling 2026-08-23). This value is served anonymously via
        // public/{jobPath}/event-contact, so it must name someone who actually speaks for
        // the event. The pool was previously all seven admin roles, which published TSIC
        // Superuser addresses on live customer events and left vendor export logins
        // (ApiAuthorized, StpAdmin) eligible to appear as the public contact.
        // No eligible Director => no contact; the widget renders nothing.
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId
                      && r.RoleId == RoleConstants.Director
                      && r.BActive == true)
            .OrderBy(r => r.RegistrationTs)
            .Select(r => new EventContactDto
            {
                FirstName = r.User != null ? r.User.FirstName ?? "" : "",
                LastName = r.User != null ? r.User.LastName ?? "" : "",
                Email = r.User != null ? r.User.Email ?? "" : "",
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<YearOverYearComparisonDto> GetYearOverYearAsync(
        Guid currentJobId, CancellationToken ct = default)
    {
        // 1. Get current job's identity fields
        var currentJob = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == currentJobId)
            .Select(j => new
            {
                j.CustomerId,
                j.JobTypeId,
                j.SportId,
                j.Season,
                j.Year,
            })
            .FirstOrDefaultAsync(ct);

        if (currentJob?.Year == null)
            return new YearOverYearComparisonDto
            {
                Series = [],
                CurrentYear = "",
            };

        // 2. Find sibling jobs (same customer + type + sport + season, current year and earlier only)
        var siblings = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.CustomerId == currentJob.CustomerId
                      && j.JobTypeId == currentJob.JobTypeId
                      && j.SportId == currentJob.SportId
                      && j.Season == currentJob.Season
                      && j.Year != null
                      && string.Compare(j.Year, currentJob.Year) <= 0)
            .OrderByDescending(j => j.Year)
            .Select(j => new { j.JobId, j.Year, j.JobName })
            .ToListAsync(ct);

        // Cap at 4 most recent years for chart readability
        var recentSiblings = siblings.Take(4).ToList();

        // 3. For each sibling, get daily registration counts (sequential — shared DbContext)
        var series = new List<YearSeriesDto>();

        foreach (var sibling in recentSiblings)
        {
            var dailyRaw = await _context.Registrations
                .AsNoTracking()
                .Where(r => r.JobId == sibling.JobId && r.BActive == true)
                .GroupBy(r => r.RegistrationTs.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .OrderBy(x => x.Date)
                .ToListAsync(ct);

            if (dailyRaw.Count == 0) continue;

            // Build cumulative totals — keep real calendar dates
            var cumulative = 0;
            var points = dailyRaw.Select(d =>
            {
                cumulative += d.Count;
                return new YearDayPointDto
                {
                    Date = d.Date,
                    CumulativeCount = cumulative,
                };
            }).ToList();

            series.Add(new YearSeriesDto
            {
                Year = sibling.Year!,
                JobName = sibling.JobName ?? sibling.Year!,
                FinalTotal = cumulative,
                DailyData = points,
            });
        }

        return new YearOverYearComparisonDto
        {
            Series = series,
            CurrentYear = currentJob.Year,
        };
    }

    // =====================================================================
    // YEAR OVER YEAR — ALL EVENTS: "where did each season stand on THIS DATE?"
    //
    // A SEPARATE report from GetYearOverYearAsync above, deliberately, and that one is not to be
    // folded into this (Todd, 2026-09-20). Year-over-Year compares a job against its own prior
    // seasons, which is right for the 221 customers running one event a season. It cannot answer
    // this question: American Select runs ~24 regional tryout sites plus a Main Event every
    // season, and the SuperDirector standing on the Main Event wants the pace of ALL of them
    // against prior years. On that job Year-over-Year finds two comparable jobs, both nearly
    // empty, because the Main Event does not fill until the tryouts have run.
    //
    // ONE COLUMN GROUP PER SEASON (Todd, 2026-09-20): registrations and money collected, each
    // season cut at the same calendar month and day. Not a curve through the season — the
    // question is where each season STANDS today, and a bar answers that where a line buries it
    // in the bottom-left corner of a chart scaled for finished seasons.
    //
    // EVERY JOB IN THE SEASON IS A PEER (Todd, 2026-09-20) — the Main Event is one site row like
    // any other, and it is inside the rollup. So the rollup counts REGISTRATIONS, not athletes:
    // 2,155 of the Main Event's 2,758 players in season 2026 also hold a regional registration.
    // Label it registrations; it is what the customer sells.
    //
    // SCOPE IS CUSTOMER + SEASON, NOT A NAME LINEAGE. Sites are cut and recut between seasons —
    // American Select split California into NorCal + SoCal for 2026 alone, merged them back for
    // 2027, and added Utah and New York-Capital Region — so per-name history breaks exactly
    // where the customer reorganised. A total does not care how the sites were cut, which is why
    // the rollup is the headline and the per-site series are the detail beneath it. Names still
    // key the site rows, and every row carries its composing job names as the safety rail.
    //
    // THE POPULATION IS THE ONE GetYoyRevenueAsync USES: active players on ACTIVE teams, with
    // the money read off the same join. Counting registrations one way and their money another
    // would let the two halves of a single column disagree. Verified on American Select: the
    // two populations are identical at every pin, and differ by 22 of 5,854 across a whole
    // season (players left on inactive teams in 2023).
    //
    // BATCHED BY PIN, not one pass per job: every job of a season shares a cutoff, so this is
    // six batches of four queries. Sequential awaits throughout — shared scoped DbContext.
    // =====================================================================
    public async Task<FeederPaceDto> GetFeederPaceAsync(
        Guid currentJobId, CancellationToken ct = default)
    {
        // Chart readability. Deeper history stays reachable by asking a job in an older season.
        const int MaxSeasons = 6;

        var asOf = DateTime.Today;

        var currentJob = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == currentJobId)
            .Select(j => new { j.CustomerId, j.Year })
            .FirstOrDefaultAsync(ct);

        var currentSeason = JobSeasonNaming.ParseYear(currentJob?.Year);
        if (currentJob == null || currentSeason == null)
        {
            return EmptyFeederPace(currentSeason ?? 0, asOf);
        }

        var customerId = currentJob.CustomerId;

        var allJobs = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.CustomerId == customerId && j.JobName != null)
            .Select(j => new { j.JobId, JobName = j.JobName!, j.Year, j.ExpiryUsers })
            .ToListAsync(ct);

        // Jobs.year is varchar and unvalidated — parsed in memory, same as the CJR report does.
        var spine = new List<FeederJobRef>();
        var ungrouped = new List<string>();
        foreach (var j in allJobs)
        {
            var season = JobSeasonNaming.ParseYear(j.Year);
            if (season == null)
            {
                // Only worth reporting if it would otherwise be on this chart. A dead 2014 job
                // with a blank year is noise; a LIVE job that cannot be placed is a hole in the
                // report the reader would never catch unaided.
                if (j.ExpiryUsers >= asOf)
                {
                    ungrouped.Add(j.JobName);
                }
                continue;
            }
            // A season NEWER than the one being stood in has no pin to be read at — shifting the
            // cutoff forward would measure it against a date it has not reached.
            if (season > currentSeason)
            {
                continue;
            }
            spine.Add(new FeederJobRef(
                j.JobId, j.JobName, season.Value, JobSeasonNaming.StripSeasonToken(j.JobName)));
        }

        var seasons = spine
            .Select(s => s.Season)
            .Distinct()
            .OrderByDescending(y => y)
            .Take(MaxSeasons)
            .ToHashSet();

        if (seasons.Count == 0)
        {
            return EmptyFeederPace(currentSeason.Value, asOf, ungrouped);
        }

        var charted = spine.Where(s => seasons.Contains(s.Season)).ToList();
        var jobById = charted.ToDictionary(j => j.JobId);
        var jobIds = charted.Select(s => s.JobId).ToList();

        // --- WHERE THE YEAR STARTS, derived from this customer's own registrations rather than
        //     assumed. American Select opens in August, five months before 1 January of the
        //     season the jobs are stamped with, so "2027 year to date" means since 1 Aug 2026.
        //     A customer whose season runs on the calendar year derives to 0 and gets 1 January
        //     with no special case. Taken as the MINIMUM across every charted season, so the
        //     window can never begin after a season's own first registration — the year-to-date
        //     bound then excludes nothing, which is exactly what it must not do. ---
        var firstByJob = await _context.Registrations
            .AsNoTracking()
            .Where(r => jobIds.Contains(r.JobId)
                && r.BActive == true
                && r.RoleId == RoleConstants.Player)
            .GroupBy(r => r.JobId)
            .Select(g => new { JobId = g.Key, First = g.Min(x => x.RegistrationTs) })
            .ToListAsync(ct);

        var originOffset = 0;
        foreach (var f in firstByJob)
        {
            var offset = MonthOffsetFromSeason(f.First, jobById[f.JobId].Season);
            if (offset < originOffset)
            {
                originOffset = offset;
            }
        }

        // --- ONE definition of the population and the money rules, read either at a cutoff or
        //     over the whole season. Both readings are on the chart at once — completed seasons
        //     are drawn at their finished size, the live one at its year-to-date size — so they
        //     must not be able to drift apart. `pinEx` is the EXCLUSIVE upper bound; null means
        //     the whole season.
        //
        //     The money rules are carried straight from GetYoyRevenueAsync: team route and
        //     player route are disjoint (a ledger row with a TeamId is the team's), and
        //     `|| FeeDiscount != 0` is load-bearing — a fully comped registration is charged to
        //     zero, and a fee-only guard would drop it along with the discount money routed
        //     through it. ---
        async Task<Dictionary<Guid, FeederPinTotals>> LoadTotalsAsync(
            List<Guid> ids, DateTime? pinEx)
        {
            var acc = ids.ToDictionary(id => id, _ => new FeederPinTotals());

            // Registrations — the same team-joined population the money is read from.
            var counts = await (
                from r in _context.Registrations.AsNoTracking()
                join t in _context.Teams on r.AssignedTeamId equals t.TeamId
                where ids.Contains(t.JobId)
                    && t.Active == true
                    && r.BActive == true
                    && r.RoleId == RoleConstants.Player
                    && (pinEx == null || r.RegistrationTs < pinEx)
                group r by t.JobId into g
                select new { JobId = g.Key, N = g.Count() })
                .ToListAsync(ct);

            foreach (var c in counts)
            {
                acc[c.JobId].Registrations += c.N;
            }

            // Team route: the TEAM carries the fee and its players carry none. `Createdate <
            // pinEx` matters — a team that did not exist at the cutoff cannot have been billed
            // by it.
            var teamRows = await (
                from t in _context.Teams.AsNoTracking()
                where ids.Contains(t.JobId)
                    && t.Active == true
                    && (pinEx == null || t.Createdate < pinEx)
                select new
                {
                    t.JobId,
                    t.FeeTotal,
                    Paid = _context.RegistrationAccounting
                        .Where(ra => ra.TeamId == t.TeamId
                            && ra.Active == true
                            && ra.Createdate != null
                            && (pinEx == null || ra.Createdate < pinEx))
                        .Sum(ra => ra.Payamt ?? 0m),
                })
                .ToListAsync(ct);

            foreach (var t in teamRows)
            {
                var totals = acc[t.JobId];
                totals.Billed += t.FeeTotal ?? 0m;
                totals.Collected += t.Paid;
            }

            var playerBilled = await (
                from r in _context.Registrations.AsNoTracking()
                join t in _context.Teams on r.AssignedTeamId equals t.TeamId
                where ids.Contains(t.JobId)
                    && t.Active == true
                    && (pinEx == null || r.RegistrationTs < pinEx)
                group r by t.JobId into g
                select new
                {
                    JobId = g.Key,
                    Billed = g.Sum(x => (x.FeeTotal != 0m || x.FeeDiscount != 0m) ? x.FeeTotal : 0m),
                })
                .ToListAsync(ct);

            foreach (var p in playerBilled)
            {
                acc[p.JobId].Billed += p.Billed;
            }

            // Player ledger rows never carry a TeamId — the route discriminator, verified
            // disjoint on Top Threat and relied on by the CJR report.
            var playerPaid = await (
                from ra in _context.RegistrationAccounting.AsNoTracking()
                join r in _context.Registrations on ra.RegistrationId equals r.RegistrationId
                join t in _context.Teams on r.AssignedTeamId equals t.TeamId
                where ids.Contains(t.JobId)
                    && t.Active == true
                    && ra.TeamId == null
                    && ra.Active == true
                    && ra.Createdate != null
                    && (pinEx == null || (ra.Createdate < pinEx && r.RegistrationTs < pinEx))
                group ra by t.JobId into g
                select new { JobId = g.Key, Paid = g.Sum(x => x.Payamt ?? 0m) })
                .ToListAsync(ct);

            foreach (var p in playerPaid)
            {
                acc[p.JobId].Collected += p.Paid;
            }

            return acc;
        }

        // --- Whole season, no pin. What a completed season FINISHED with — the size of the
        //     thing being paced against, and what the per-site charts draw for every season that
        //     is already over. ---
        var fullByJob = await LoadTotalsAsync(jobIds, null);

        // --- At each season's own pin. Sequential per season: one scoped DbContext, so these
        //     may never run concurrently. ---
        var atPinByJob = new Dictionary<Guid, FeederPinTotals>();
        var pinBySeason = seasons.ToDictionary(y => y, y => asOf.AddYears(y - currentSeason.Value));

        foreach (var (season, pin) in pinBySeason)
        {
            var batchIds = charted.Where(j => j.Season == season).Select(j => j.JobId).ToList();
            var batch = await LoadTotalsAsync(batchIds, pin.AddDays(1));

            foreach (var kv in batch)
            {
                atPinByJob[kv.Key] = kv.Value;
            }
        }
        // --- Assemble. One season series for the rollup, one per site, same shape. ---
        var orderedSeasons = seasons.OrderBy(y => y).ToList();

        List<FeederPaceSeasonDto> SeriesFor(IReadOnlyCollection<FeederJobRef> jobs) =>
            orderedSeasons.Select(season =>
            {
                var members = jobs.Where(j => j.Season == season).ToList();

                static FeederPinTotals Sum(
                    List<FeederJobRef> ms, Dictionary<Guid, FeederPinTotals> src) =>
                    ms.Aggregate(new FeederPinTotals(), (acc, j) =>
                    {
                        var t = src[j.JobId];
                        acc.Registrations += t.Registrations;
                        acc.Billed += t.Billed;
                        acc.Collected += t.Collected;
                        return acc;
                    });

                var atPin = Sum(members, atPinByJob);
                var full = Sum(members, fullByJob);

                return new FeederPaceSeasonDto
                {
                    Season = season,
                    // AddMonths is calendar-safe, and AddYears lands a Feb 29 ask on Feb 28.
                    YtdFrom = new DateTime(season, 1, 1).AddMonths(originOffset),
                    PinDate = pinBySeason[season],
                    Registrations = atPin.Registrations,
                    Billed = atPin.Billed,
                    Collected = atPin.Collected,
                    TotalRegistrations = full.Registrations,
                    TotalBilled = full.Billed,
                    TotalCollected = full.Collected,
                    JobCount = members.Count,
                };
            }).ToList();

        var priorSeason = orderedSeasons.Where(y => y < currentSeason.Value).DefaultIfEmpty(0).Max();

        var sites = charted
            .Where(j => j.Season == currentSeason.Value || j.Season == priorSeason)
            .GroupBy(j => j.Site, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                // The SERIES spans every charted season, not just the two that qualified this
                // site for a row — the per-site chart is the same picture as the rollup.
                var siteJobs = charted
                    .Where(j => string.Equals(j.Site, group.Key, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                return new FeederPaceSiteDto
                {
                    Site = group.First().Site,
                    Seasons = SeriesFor(siteJobs),
                    // ANY earlier season this site ran, not merely the one immediately before.
                    // A region can sit a year out and come back: American Select ran California
                    // 2021-2025, skipped 2026 and returned for 2027. Testing only the prior
                    // season called it a first-timer and compared it against a season in which
                    // it did not exist (Todd, 2026-09-20).
                    HasPriorSeason = siteJobs.Any(j => j.Season < currentSeason.Value),
                    IsRetired = !siteJobs.Any(j => j.Season == currentSeason.Value),
                    JobNames = siteJobs
                        .OrderByDescending(j => j.Season)
                        .ThenBy(j => j.JobName, StringComparer.Ordinal)
                        .Select(j => j.JobName)
                        .ToList(),
                };
            })
            .ToList();

        return new FeederPaceDto
        {
            CurrentSeason = currentSeason.Value,
            AsOfDate = asOf,
            Seasons = SeriesFor(charted),
            // Sites that have OPENED lead, biggest first — that is where the season is being
            // made right now. Everything still to open then sorts by the size it reached last
            // time it ran, so a reader scanning the grid meets the big regions before the small
            // ones instead of a wall of alphabetised zeros.
            Sites = sites
                .OrderByDescending(s => s.Seasons[^1].Registrations)
                .ThenByDescending(s => s.Seasons.Count > 1
                    ? s.Seasons[^2].TotalRegistrations
                    : 0)
                .ThenBy(s => s.Site, StringComparer.Ordinal)
                .ToList(),
            UngroupedJobNames = ungrouped,
        };
    }

    /// <summary>One job's identity for feeder-pace placement. Figures never travel on this record.</summary>
    private sealed record FeederJobRef(Guid JobId, string JobName, int Season, string Site);

    /// <summary>Mutable accumulator — one job's figures at one pin.</summary>
    private sealed class FeederPinTotals
    {
        public int Registrations { get; set; }
        public decimal Billed { get; set; }
        public decimal Collected { get; set; }
    }

    /// <summary>
    /// Whole months from 1 January of <paramref name="season"/> to <paramref name="d"/> —
    /// negative for a date in the previous calendar year. This is what lets the start of the
    /// year be derived rather than assumed.
    /// </summary>
    private static int MonthOffsetFromSeason(DateTime d, int season)
        => ((d.Year - season) * 12) + (d.Month - 1);

    private static FeederPaceDto EmptyFeederPace(
        int season, DateTime asOf, List<string>? ungrouped = null)
        => new()
        {
            CurrentSeason = season,
            AsOfDate = asOf,
            Seasons = [],
            Sites = [],
            UngroupedJobNames = ungrouped ?? [],
        };

    public async Task<JobRegCountsAndDollarsDto> GetJobRegCountsAndDollarsAsync(
        Guid currentJobId,
        CancellationToken ct = default)
    {
        var now = DateTime.Now;

        // Scope = the customer owning the job the caller is standing in. This mirrors
        // JobRepository.GetCustomerJobIdsAsync, the shipped precedent for customer-scoped
        // reach, and pairs with the CanCrossCustomerJobs policy on the endpoint.
        var customerId = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == currentJobId)
            .Select(j => j.CustomerId)
            .FirstOrDefaultAsync(ct);

        if (customerId == Guid.Empty)
        {
            return new JobRegCountsAndDollarsDto
            {
                Rows = [],
                TotalPlayers = 0,
                TotalTeams = 0,
                TotalFees = 0m,
                TotalPaid = 0m,
                TotalOwed = 0m,
            };
        }

        // LIVE = ExpiryUsers > now. This is the canonical "is the event over?" test
        // (AdministratorService, JobClonePlanner). ExpiryAdmin is deliberately NOT used:
        // the admin door stays open ~a year past the event, so it would drag concluded
        // events into the portfolio.
        //
        // SHAPE: three round-trips — the job spine, then ONE grouped pass over each child
        // table. The first cut asked for five correlated subqueries PER JOB (player count,
        // team count, and three separate sums that re-read the very same rows), i.e. 1+5N
        // queries against a 667k-row Registrations table that has NO index on JobId. On 18
        // live jobs that measured 1,468ms; this shape measures 156ms for identical output.
        //
        // Each child aggregate joins back to the customer-filtered job set rather than
        // taking an IN-list of job ids: customerID is the scope key AND the security
        // boundary, so it belongs in the query, not in a list assembled beforehand.
        //
        // Scope is keyed on Jobs.Jobs.customerID and nothing else. Registrations and teams
        // both carry their own customerID column, but those are ~99.95% NULL (667,333 of
        // 667,686 and 51,395 of 51,608) — filtering on them returns almost nothing, and it
        // fails SILENTLY rather than erroring.
        var jobs = await (
            from j in _context.Jobs.AsNoTracking()
            join jt in _context.JobTypes on j.JobTypeId equals jt.JobTypeId
            where j.CustomerId == customerId && j.ExpiryUsers > now
            select new
            {
                j.JobId,
                JobName = j.JobName ?? string.Empty,
                j.JobPath,
                JobTypeName = jt.JobTypeName ?? string.Empty,
                j.EventStartDate,
            })
            .ToListAsync(ct);

        // Sequential awaits, never Task.WhenAll — these share one scoped DbContext.
        var regAgg = await (
            from r in _context.Registrations.AsNoTracking()
            join j in _context.Jobs on r.JobId equals j.JobId
            where j.CustomerId == customerId && j.ExpiryUsers > now && r.BActive == true
            group r by r.JobId into g
            select new
            {
                JobId = g.Key,
                PlayerCount = g.Count(x => x.RoleId == RoleConstants.Player),
                Fees = g.Sum(x => x.FeeTotal),
                Paid = g.Sum(x => x.PaidTotal),
                Owed = g.Sum(x => x.OwedTotal),
            })
            .ToDictionaryAsync(x => x.JobId, ct);

        var teamAgg = await (
            from t in _context.Teams.AsNoTracking()
            join j in _context.Jobs on t.JobId equals j.JobId
            where j.CustomerId == customerId && j.ExpiryUsers > now && t.Active == true
            group t by t.JobId into g
            select new { JobId = g.Key, TeamCount = g.Count() })
            .ToDictionaryAsync(x => x.JobId, ct);

        // A job with no registrations produces no group, which is a genuine zero, not a
        // gap — the widget renders it as an em dash.
        var rows = jobs
            .Select(j =>
            {
                var agg = regAgg.GetValueOrDefault(j.JobId);
                var teams = teamAgg.GetValueOrDefault(j.JobId);

                return new JobRegCountsAndDollarsRowDto
                {
                    JobId = j.JobId,
                    JobName = j.JobName,
                    JobPath = j.JobPath,
                    JobTypeName = j.JobTypeName,
                    EventStartDate = j.EventStartDate,
                    PlayerCount = agg?.PlayerCount ?? 0,
                    TeamCount = teams?.TeamCount ?? 0,
                    Fees = agg?.Fees ?? 0m,
                    Paid = agg?.Paid ?? 0m,
                    Owed = agg?.Owed ?? 0m,
                };
            })
            .OrderBy(x => x.EventStartDate)
            .ThenBy(x => x.JobName)
            .ToList();

        return new JobRegCountsAndDollarsDto
        {
            Rows = rows,
            TotalPlayers = rows.Sum(x => x.PlayerCount),
            TotalTeams = rows.Sum(x => x.TeamCount),
            TotalFees = rows.Sum(x => x.Fees),
            TotalPaid = rows.Sum(x => x.Paid),
            TotalOwed = rows.Sum(x => x.Owed),
        };
    }

    public async Task<Dictionary<Guid, string>> GetCustomerJobNamesAsync(
        Guid currentJobId,
        IReadOnlyCollection<Guid> jobIds,
        CancellationToken ct = default)
    {
        if (jobIds.Count == 0) return [];

        // Scope = the customer owning the job the caller is standing in, the same key the
        // portfolio widget uses. customerID is the scope AND the security boundary: the
        // job ids arriving here came from TSICLogs, which knows nothing about customers,
        // so this join is the only thing preventing one customer's dashboard from naming
        // -- and therefore revealing -- another customer's events.
        var customerId = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == currentJobId)
            .Select(j => j.CustomerId)
            .FirstOrDefaultAsync(ct);

        if (customerId == Guid.Empty) return [];

        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.CustomerId == customerId
                     && jobIds.Contains(j.JobId)
                     && j.JobName != null)
            .Select(j => new { j.JobId, JobName = j.JobName! })
            .ToDictionaryAsync(x => x.JobId, x => x.JobName, ct);
    }

}
