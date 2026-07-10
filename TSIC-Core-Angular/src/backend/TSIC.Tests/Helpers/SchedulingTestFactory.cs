using Microsoft.Extensions.Logging.Abstractions;
using TSIC.API.Services.Scheduling;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Repositories;

namespace TSIC.Tests.Helpers;

/// <summary>
/// Builds scheduling services for tests, hiding the bracket-advancement
/// dependency (added for R2). These tests don't score bracket games, so a real
/// advancement service over the same context is a correct, inert stand-in.
/// </summary>
public static class SchedulingTestFactory
{
    public static BracketAdvancementService Advancement(SqlDbContext ctx, ScheduleRepository scheduleRepo) =>
        new(new BracketRepository(ctx), scheduleRepo, NullLogger<BracketAdvancementService>.Instance);

    public static BracketGenerationService Generation(SqlDbContext ctx) =>
        new(new BracketRepository(ctx), NullLogger<BracketGenerationService>.Instance);

    public static BracketSeedResolutionService SeedResolution(SqlDbContext ctx, ScheduleRepository scheduleRepo) =>
        new(new BracketRepository(ctx), scheduleRepo, new JobRepository(ctx), Generation(ctx),
            NullLogger<BracketSeedResolutionService>.Instance);

    public static ViewScheduleService ViewSchedule(
        SqlDbContext ctx, ScheduleRepository scheduleRepo, TeamRepository teamRepo) =>
        new(scheduleRepo, teamRepo, new BracketRepository(ctx),
            Advancement(ctx, scheduleRepo), SeedResolution(ctx, scheduleRepo),
            new JobRepository(ctx));

    public static ViewScheduleService ViewSchedule(SqlDbContext ctx) =>
        ViewSchedule(ctx, new ScheduleRepository(ctx), new TeamRepository(ctx));
}
