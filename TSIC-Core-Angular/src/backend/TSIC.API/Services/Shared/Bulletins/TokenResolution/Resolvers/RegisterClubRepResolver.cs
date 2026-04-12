namespace TSIC.API.Services.Shared.Bulletins.TokenResolution.Resolvers;

public sealed class RegisterClubRepResolver : IBulletinTokenResolver
{
    public string TokenName => "REGISTER_CLUBREP";

    public string Resolve(TokenContext ctx)
    {
        if (!ctx.Pulse.TeamRegistrationOpen)
        {
            return string.Empty;
        }

        var label = ctx.Pulse.TeamRegRequiresToken
            ? "Register Club / Team (invite required)"
            : "Register Club / Team";

        return $"""<a href="/{ctx.JobPath}/registration/team" class="btn btn-primary">{label}</a>""";
    }
}
