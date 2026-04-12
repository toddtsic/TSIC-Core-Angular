namespace TSIC.API.Services.Shared.Bulletins.TokenResolution.Resolvers;

public sealed class RegisterSelfRosterPlayersAndCoachResolver : IBulletinTokenResolver
{
    public string TokenName => "REGISTER_SELFROSTERPLAYERSANDCOACH";

    public string Resolve(TokenContext ctx)
    {
        if (!ctx.Pulse.AdultRegistrationPlanned)
        {
            return string.Empty;
        }

        // Route/params TBD with user — placeholder points at self-roster-update entry.
        return $"""<a href="/{ctx.JobPath}/registration/self-roster-update" class="btn btn-primary">Register Self-Roster Players &amp; Coach</a>""";
    }
}
