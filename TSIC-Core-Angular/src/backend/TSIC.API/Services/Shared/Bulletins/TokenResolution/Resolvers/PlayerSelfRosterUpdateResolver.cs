namespace TSIC.API.Services.Shared.Bulletins.TokenResolution.Resolvers;

public sealed class PlayerSelfRosterUpdateResolver : IBulletinTokenResolver
{
    public string TokenName => "PLAYERSELFROSTER_UPDATE";

    public string Resolve(TokenContext ctx)
    {
        var label = ctx.IsAuthenticated
            ? "Update My Roster Info"
            : "Sign in to Update My Roster Info";

        return $"""<a href="/{ctx.JobPath}/registration/self-roster-update" class="btn btn-primary">{label}</a>""";
    }
}
