using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Scheduling;

/// <summary>
/// Covers ScheduleRepository.ExecuteWeatherAdjustmentAsync — the EF replacement for
/// the legacy [utility].[ScheduleAlterGSIPerGameDate] sproc. One test per result code
/// (1 success, 2–8 rejections).
/// </summary>
public class WeatherAdjustmentTests
{
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid FieldA = Guid.NewGuid();
    private static readonly Guid FieldB = Guid.NewGuid();
    private static readonly DateTime Day = new(2026, 6, 1);

    private static int _gid;

    private static ScheduleRepository CreateRepo(SqlDbContext ctx) => new(ctx);

    private static Schedule SeedGame(SqlDbContext ctx, DateTime gDate, Guid? fieldId = null)
    {
        var game = new Schedule
        {
            Gid = System.Threading.Interlocked.Increment(ref _gid),
            JobId = JobId,
            LeagueId = Guid.NewGuid(),
            FieldId = fieldId ?? FieldA,
            GDate = gDate,
            Season = "Summer",
            Year = "2026",
            LebUserId = "test",
            Modified = DateTime.UtcNow
        };
        ctx.Schedule.Add(game);
        return game;
    }

    private static AdjustWeatherRequest Request(
        DateTime preFirstGame, int preGSI,
        DateTime postFirstGame, int postGSI,
        List<Guid>? fieldIds = null) => new()
        {
            PreFirstGame = preFirstGame,
            PreGSI = preGSI,
            PostFirstGame = postFirstGame,
            PostGSI = postGSI,
            FieldIds = fieldIds ?? new List<Guid>()
        };

    // ─── Code 5 ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Code 5 — pre and post in different years")]
    public async Task ReturnsFive_WhenYearsDiffer()
    {
        await using var ctx = DbContextFactory.Create();
        var repo = CreateRepo(ctx);

        var code = await repo.ExecuteWeatherAdjustmentAsync(
            JobId,
            Request(new DateTime(2026, 12, 31, 10, 0, 0), 45, new DateTime(2027, 1, 1, 10, 0, 0), 45));

        code.Should().Be(5);
    }

    // ─── Code 4 ────────────────────────────────────────────────────────────

    [Theory(DisplayName = "Code 4 — postGSI out of [0,120]")]
    [InlineData(-1)]
    [InlineData(121)]
    public async Task ReturnsFour_WhenPostGSIOutOfRange(int postGSI)
    {
        await using var ctx = DbContextFactory.Create();
        var repo = CreateRepo(ctx);

        var code = await repo.ExecuteWeatherAdjustmentAsync(
            JobId,
            Request(Day.AddHours(10), 45, Day.AddHours(11), postGSI));

        code.Should().Be(4);
    }

    // ─── Code 6 ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Code 6 — no games match the filter")]
    public async Task ReturnsSix_WhenNoTargetGames()
    {
        await using var ctx = DbContextFactory.Create();
        SeedGame(ctx, Day.AddDays(1).AddHours(10));  // wrong day
        await ctx.SaveChangesAsync();

        var code = await CreateRepo(ctx).ExecuteWeatherAdjustmentAsync(
            JobId,
            Request(Day.AddHours(10), 45, Day.AddHours(11), 45));

        code.Should().Be(6);
    }

    // ─── Code 3 ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Code 3 — claimed preGSI doesn't match actual game spacing")]
    public async Task ReturnsThree_WhenPreGSIDoesntMatchSpacing()
    {
        await using var ctx = DbContextFactory.Create();
        SeedGame(ctx, Day.AddHours(10));            // 10:00
        SeedGame(ctx, Day.AddHours(10).AddMinutes(30));  // 10:30 — 30-min spacing
        await ctx.SaveChangesAsync();

        var code = await CreateRepo(ctx).ExecuteWeatherAdjustmentAsync(
            JobId,
            Request(Day.AddHours(10), preGSI: 45, Day.AddHours(11), postGSI: 45));

        code.Should().Be(3);
    }

    // ─── Code 8 ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Code 8 — at least one target game is off the preGSI grid")]
    public async Task ReturnsEight_WhenTargetGameOffGrid()
    {
        await using var ctx = DbContextFactory.Create();
        SeedGame(ctx, Day.AddHours(10));                 // slot 0
        SeedGame(ctx, Day.AddHours(10).AddMinutes(45));  // slot 1 (anchors the firstTwo check at 45)
        SeedGame(ctx, Day.AddHours(10).AddMinutes(50));  // off-grid (50 mod 45 != 0)
        await ctx.SaveChangesAsync();

        var code = await CreateRepo(ctx).ExecuteWeatherAdjustmentAsync(
            JobId,
            Request(Day.AddHours(10), 45, Day.AddHours(11), 30));

        code.Should().Be(8);
    }

    // ─── Code 7 ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Code 7 — pre and post are identical (no-op)")]
    public async Task ReturnsSeven_WhenNoChange()
    {
        await using var ctx = DbContextFactory.Create();
        SeedGame(ctx, Day.AddHours(10));
        SeedGame(ctx, Day.AddHours(10).AddMinutes(45));
        await ctx.SaveChangesAsync();

        var preFirst = Day.AddHours(10);
        var code = await CreateRepo(ctx).ExecuteWeatherAdjustmentAsync(
            JobId,
            Request(preFirst, 45, preFirst, 45));

        code.Should().Be(7);
    }

    // ─── Code 2 ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Code 2 — shifted games would collide with a non-target game")]
    public async Task ReturnsTwo_WhenShiftedRangeCollides()
    {
        // Targets at 10:00 and 10:45 shift backward to 8:00 and 8:15 (postGSI=15).
        // A non-target game at 8:15 sits before preFirstGame so it isn't a target,
        // but it lands inside [8:00, 8:30) → interference.
        await using var ctx = DbContextFactory.Create();
        SeedGame(ctx, Day.AddHours(10));
        SeedGame(ctx, Day.AddHours(10).AddMinutes(45));
        SeedGame(ctx, Day.AddHours(8).AddMinutes(15));  // non-target, in post range
        await ctx.SaveChangesAsync();

        var code = await CreateRepo(ctx).ExecuteWeatherAdjustmentAsync(
            JobId,
            Request(Day.AddHours(10), 45, Day.AddHours(8), 15));

        code.Should().Be(2);
    }

    // ─── Code 1 ────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Code 1 — success: target game times are recomputed")]
    public async Task ReturnsOne_AndUpdatesGameTimes()
    {
        await using var ctx = DbContextFactory.Create();
        var g1 = SeedGame(ctx, Day.AddHours(10));                 // slot 0
        var g2 = SeedGame(ctx, Day.AddHours(10).AddMinutes(45));  // slot 1
        await ctx.SaveChangesAsync();

        var code = await CreateRepo(ctx).ExecuteWeatherAdjustmentAsync(
            JobId,
            Request(Day.AddHours(10), 45, Day.AddHours(11), 30));

        code.Should().Be(1);

        var updated = await ctx.Schedule.AsNoTracking()
            .Where(s => s.Gid == g1.Gid || s.Gid == g2.Gid)
            .OrderBy(s => s.Gid)
            .ToListAsync();

        updated[0].GDate.Should().Be(Day.AddHours(11));
        updated[1].GDate.Should().Be(Day.AddHours(11).AddMinutes(30));
    }

    // ─── Field filter ──────────────────────────────────────────────────────

    [Fact(DisplayName = "Field filter — only games on selected fields are shifted")]
    public async Task FieldFilter_OnlyShiftsSelectedFieldGames()
    {
        await using var ctx = DbContextFactory.Create();
        var a1 = SeedGame(ctx, Day.AddHours(10), FieldA);
        var a2 = SeedGame(ctx, Day.AddHours(10).AddMinutes(45), FieldA);
        var b1 = SeedGame(ctx, Day.AddHours(10), FieldB);  // same anchor, different field — must NOT move
        await ctx.SaveChangesAsync();

        var code = await CreateRepo(ctx).ExecuteWeatherAdjustmentAsync(
            JobId,
            Request(Day.AddHours(10), 45, Day.AddHours(11), 30, fieldIds: new List<Guid> { FieldA }));

        code.Should().Be(1);

        var games = await ctx.Schedule.AsNoTracking().ToListAsync();
        games.Single(g => g.Gid == a1.Gid).GDate.Should().Be(Day.AddHours(11));
        games.Single(g => g.Gid == a2.Gid).GDate.Should().Be(Day.AddHours(11).AddMinutes(30));
        games.Single(g => g.Gid == b1.Gid).GDate.Should().Be(Day.AddHours(10), "FieldB game must be untouched");
    }
}
