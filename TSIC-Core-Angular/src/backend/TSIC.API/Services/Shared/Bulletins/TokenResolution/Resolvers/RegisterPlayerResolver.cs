namespace TSIC.API.Services.Shared.Bulletins.TokenResolution.Resolvers;

public sealed class RegisterPlayerResolver : IBulletinTokenResolver
{
    public string TokenName => "REGISTER_PLAYER";

    public string Resolve(TokenContext ctx)
    {
        if (!ctx.Pulse.PlayerRegistrationOpen)
        {
            return string.Empty;
        }

        var label = ctx.Pulse.PlayerRegRequiresToken
            ? "Register as Player (invite required)"
            : "Register as Player";

        return $"""<a href="/{ctx.JobPath}/registration/player" class="btn btn-primary">{label}</a>""";
    }
}
