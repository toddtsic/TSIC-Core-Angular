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

        // Icon + label + trailing arrow, matching the Schedule Links card's own CTA. The
        // classes are styled under .bulletin-body (styles/_component-overrides.scss) and all
        // three tags survive the rich-text sanitizer; with no stylesheet it still degrades to
        // a plain labelled link, so an unstyled surface loses polish, not the call to action.
        return $"""
            <a href="/{ctx.JobPath}/schedule" class="btn btn-primary bl-cta"><i class="bi bi-calendar-event" aria-hidden="true"></i><span>View Schedule</span><i class="bi bi-arrow-right bl-cta__go" aria-hidden="true"></i></a>
            """;
    }
}
