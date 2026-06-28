using System.Security.Claims;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Shared;

/// <summary>
/// Maps the authenticated principal to the coarse <see cref="CapabilityActor"/> the
/// registration-create authority composes against. <see cref="CapabilityActor.Admin"/> iff the
/// JWT role-name claim (<c>ClaimTypes.Role</c> — never raw <c>"role"</c>, which ASP.NET remaps)
/// is one of the three admin roles, literally the <c>AdminOnly</c> policy. Everyone else and
/// anonymous resolves to <see cref="CapabilityActor.User"/>.
///
/// The JWT carries the SINGLE role authenticated for this job, so a Director acting in a
/// Club-Rep session resolves to User (dual-role ≠ escalation) — which is exactly what we want
/// for the eventConcluded door.
/// </summary>
public static class CapabilityActorExtensions
{
    public static CapabilityActor ToCapabilityActor(this ClaimsPrincipal? user)
    {
        var role = user?.FindFirstValue(ClaimTypes.Role);
        return role is RoleConstants.Names.SuperuserName
                    or RoleConstants.Names.DirectorName
                    or RoleConstants.Names.SuperDirectorName
            ? CapabilityActor.Admin
            : CapabilityActor.User;
    }
}
