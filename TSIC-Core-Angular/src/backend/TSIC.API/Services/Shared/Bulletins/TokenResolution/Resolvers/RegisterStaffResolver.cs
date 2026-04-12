namespace TSIC.API.Services.Shared.Bulletins.TokenResolution.Resolvers;

public sealed class RegisterStaffResolver : IBulletinTokenResolver
{
    public string TokenName => "REGISTER_STAFF";

    public string Resolve(TokenContext ctx)
    {
        if (!ctx.Pulse.AdultRegistrationPlanned)
        {
            return string.Empty;
        }

        // role=0 → Coach (per adult.component.ts:29 in frontend)
        return $"""<a href="/{ctx.JobPath}/registration/adult?role=0" class="btn btn-primary">Register as Staff / Coach</a>""";
    }
}
