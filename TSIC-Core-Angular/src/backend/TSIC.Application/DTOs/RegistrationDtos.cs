namespace TSIC.Application.DTOs
{
    public record RegistrationRoleDto(string RoleName, List<RegistrationDto> RoleRegistrations);
    public record RegistrationDto(string RegId, string DisplayText, string JobLogo, string? JobPath);
}
