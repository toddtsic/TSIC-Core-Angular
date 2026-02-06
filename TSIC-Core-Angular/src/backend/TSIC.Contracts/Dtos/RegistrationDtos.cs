namespace TSIC.Contracts.Dtos
{
    public record RegistrationRoleDto
    {
        public required string RoleName { get; init; }
        public required List<RegistrationDto> RoleRegistrations { get; init; }
    }

    public record RegistrationDto
    {
        public required string RegId { get; init; }
        public required string DisplayText { get; init; }
        public required string JobLogo { get; init; }
        public string? JobPath { get; init; }
    }
}
