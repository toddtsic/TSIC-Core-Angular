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

    /// <summary>
    /// The role-select screen's standalone Club Team Library door. The user's most recent Club Rep
    /// registration REGARDLESS of the job's ExpiryUsers, because the library is the club's, not the
    /// event's: a rep whose every event has expired still owns a list to maintain. Null when the
    /// account has never been a club rep.
    /// </summary>
    public record ClubLibraryDoorDto
    {
        public required string RegId { get; init; }
        public required string JobPath { get; init; }
        public required string JobName { get; init; }
        public required string JobLogo { get; init; }
        public string? ClubName { get; init; }
        public DateTime? EventStartDate { get; init; }
        public DateTime? EventEndDate { get; init; }
        /// <summary>True when this registration is also offered as a row on the picker (job not expired).</summary>
        public required bool IsCurrent { get; init; }
        public int TeamCount { get; init; }
    }
}
