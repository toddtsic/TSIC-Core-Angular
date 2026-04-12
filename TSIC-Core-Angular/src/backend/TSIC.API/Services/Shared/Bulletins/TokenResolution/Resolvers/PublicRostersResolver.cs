namespace TSIC.API.Services.Shared.Bulletins.TokenResolution.Resolvers;

public sealed class PublicRostersResolver : IBulletinTokenResolver
{
    public string TokenName => "PUBLIC_ROSTERS";

    public string Resolve(TokenContext ctx)
    {
        return $"""<a href="/{ctx.JobPath}/rosters" class="btn btn-primary">View Public Rosters</a>""";
    }
}
