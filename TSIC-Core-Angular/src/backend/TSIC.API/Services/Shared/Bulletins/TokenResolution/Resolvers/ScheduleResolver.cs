namespace TSIC.API.Services.Shared.Bulletins.TokenResolution.Resolvers;

public sealed class ScheduleResolver : IBulletinTokenResolver
{
    public string TokenName => "SCHEDULE";
    public string Description => "Call-to-action button for the public schedule. Hidden until schedule is published.";
    public string[] GatingConditions => ["SchedulePublished"];

    public string Resolve(TokenContext ctx)
    {
        if (!ctx.Pulse.SchedulePublished)
        {
            return string.Empty;
        }

        return $"""<a href="/{ctx.JobPath}/schedule" class="btn btn-primary">View Schedule</a>""";
    }
}
