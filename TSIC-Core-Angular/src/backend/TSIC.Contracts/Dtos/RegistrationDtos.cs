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
        /// <summary>Event window, so the role picker can group upcoming vs past and date each row.</summary>
        public DateTime? EventStartDate { get; init; }
        public DateTime? EventEndDate { get; init; }
        /// <summary>Club Rep rows only: teams this registration has entered in the event (any age group, dropped included — same count the pulse reports). Null for every other role.</summary>
        public int? TeamCount { get; init; }
    }
}
