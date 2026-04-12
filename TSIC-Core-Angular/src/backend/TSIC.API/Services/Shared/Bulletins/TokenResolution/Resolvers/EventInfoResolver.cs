using System.Net;

namespace TSIC.API.Services.Shared.Bulletins.TokenResolution.Resolvers;

public sealed class EventInfoResolver : IBulletinTokenResolver
{
    public string TokenName => "EVENT_INFO";
    public string Description => "Rich card displaying event name and key dates. Always visible.";
    public string[] GatingConditions => [];

    public string Resolve(TokenContext ctx)
    {
        var name = WebUtility.HtmlEncode(ctx.Job.JobName);
        var datesLine = ctx.Job.USLaxNumberValidThroughDate.HasValue
            ? $"""<div class="text-muted">Valid through {ctx.Job.USLaxNumberValidThroughDate.Value:MMM d, yyyy}</div>"""
            : string.Empty;

        return $"""
            <div class="card">
              <div class="card-body">
                <h5 class="card-title mb-2">{name}</h5>
                {datesLine}
              </div>
            </div>
            """;
    }
}
