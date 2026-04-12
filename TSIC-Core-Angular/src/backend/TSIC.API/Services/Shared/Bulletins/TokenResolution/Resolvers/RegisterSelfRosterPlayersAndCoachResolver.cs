namespace TSIC.API.Services.Shared.Bulletins.TokenResolution.Resolvers;

public sealed class RegisterSelfRosterPlayersAndCoachResolver : IBulletinTokenResolver
{
    public string TokenName => "REGISTER_SELFROSTERPLAYERSANDCOACH";
    public string Description => "Call-to-action button for self-roster registration flow covering both players and coach (placeholder route; refine at impl).";
    public string[] GatingConditions => ["AdultRegistrationPlanned"];

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
