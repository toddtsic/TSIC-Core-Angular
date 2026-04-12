namespace TSIC.API.Services.Shared.Bulletins.TokenResolution.Resolvers;

public sealed class RegisterUnassignedAdultResolver : IBulletinTokenResolver
{
    public string TokenName => "REGISTER_UNASSIGNEDADULT";
    public string Description => "Call-to-action button for adult registration without a preselected role (user picks in the wizard).";
    public string[] GatingConditions => ["AdultRegistrationPlanned"];

    public string Resolve(TokenContext ctx)
    {
        if (!ctx.Pulse.AdultRegistrationPlanned)
        {
            return string.Empty;
        }

        return $"""<a href="/{ctx.JobPath}/registration/adult" class="btn btn-primary">Register as Adult</a>""";
    }
}
