namespace TSIC.API.Services.Shared.Bulletins.TokenResolution.Resolvers;

public sealed class RegisterPlayerResolver : IBulletinTokenResolver
{
    public string TokenName => "REGISTER_PLAYER";
    public string Description => "Call-to-action button for player registration. Hides when registration is closed (the job toggle is off OR no team is currently within its registration-availability window); label indicates invite-required when token-gated.";
    public string[] GatingConditions => ["PlayerRegistrationOpen", "PlayerTeamsAvailableForRegistration", "PlayerRegRequiresToken"];

    public string Resolve(TokenContext ctx)
    {
        // Mirror the wizard's `registrationClosed` gate and the invite guard: the toggle being
        // on isn't enough — at least one team must be within its registration window, else the
        // CTA links to a wizard that immediately shows "registration closed".
        if (!ctx.Pulse.PlayerRegistrationOpen || !ctx.Pulse.PlayerTeamsAvailableForRegistration)
        {
            return string.Empty;
        }

        var label = ctx.Pulse.PlayerRegRequiresToken
            ? "Register as Player (invite required)"
            : "Register as Player";

        return $"""<a href="/{ctx.JobPath}/registration/player" class="btn btn-primary">{label}</a>""";
    }
}
