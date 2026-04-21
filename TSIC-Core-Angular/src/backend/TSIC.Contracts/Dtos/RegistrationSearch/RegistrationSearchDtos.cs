namespace TSIC.Contracts.Dtos.RegistrationSearch;

/// <summary>
/// Search request sent from the grid filter panel.
/// All multi-select filters use List for multi-value selection.
/// POST body because filter criteria can be complex.
/// </summary>
public record RegistrationSearchRequest
{
    // Text filters
    public string? Name { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? SchoolName { get; init; }
    public string? InvoiceNumber { get; init; }

    // Multi-select filters
    public List<string>? RoleIds { get; init; }
    public List<Guid>? TeamIds { get; init; }
    public List<Guid>? AgegroupIds { get; init; }
    public List<Guid>? DivisionIds { get; init; }
    public List<string>? ClubNames { get; init; }
    public List<string>? Genders { get; init; }
    public List<string>? Positions { get; init; }
    public List<string>? GradYears { get; init; }
    public List<string>? Grades { get; init; }
    public List<int>? AgeRangeIds { get; init; }

    // Status filters (multi-select)
    public List<string>? ActiveStatuses { get; init; }
    public List<string>? PayStatuses { get; init; }
    public List<string>? ArbSubscriptionStatuses { get; init; }
    public List<string>? MobileRegistrationRoles { get; init; }

    // Accounting filter (multi-select — includes payment methods + discount codes prefixed with "dc:")
    public List<string>? PaymentTypes { get; init; }

    // Date range
    public DateTime? RegDateFrom { get; init; }
    public DateTime? RegDateTo { get; init; }

    // Club roster threshold search
    public int? RosterThreshold { get; init; }
    public List<string>? RosterThresholdClubNames { get; init; }

    // CADT tree filter — team IDs resolved from Club/Agegroup/Division/Team tree.
    // ANDed with all other filters (including LADT tree TeamIds/AgegroupIds/DivisionIds).
    public List<Guid>? CadtTeamIds { get; init; }

    // Vertical Insure filters. Backend ignores each unless the job offers that VI product.
    // false = filter to population without coverage (implicitly constrains role: Player / ClubRep).
    // true  = filter to population with coverage.
    public bool? HasVIPlayerInsurance { get; init; }
    public bool? HasVITeamInsurance { get; init; }

    // ARB Health filter. Backend ignores unless the job has ARB enabled.
    //   "behind-active"  = subscription active or suspended, balance owed
    //   "behind-expired" = subscription expired or terminated, balance owed
    // Both implicitly restrict to active registrations.
    public string? ArbHealthStatus { get; init; }

    // USLax membership status. Backend ignores unless the job has
    // UslaxNumberValidThroughDate set AND is a Lacrosse job.
    //   "expired" = SportAssnIdexpDate is null OR < job's valid-through date
    // Implicitly restricts to active Player registrations.
    public string? UsLaxMembershipStatus { get; init; }
}

/// <summary>
/// Single row in the registration search results grid.
/// </summary>
public record RegistrationSearchResultDto
{
    public required Guid RegistrationId { get; init; }
    public required int RegistrationAi { get; init; }

    // Person (from AspNetUsers join)
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public string? Phone { get; init; }
    public DateTime? Dob { get; init; }

    // Registration context
    public required string RoleName { get; init; }
    public required bool Active { get; init; }
    public string? Position { get; init; }
    public string? TeamName { get; init; }
    public string? AgegroupName { get; init; }
    public string? DivisionName { get; init; }
    public string? ClubName { get; init; }
    /// <summary>Club name derived from team's club rep registration (null when no club rep).</summary>
    public string? ClubRepClubName { get; init; }
    /// <summary>Computed assignment display: "ClubRepClub AgegroupName TeamName" or "AgegroupName TeamName".</summary>
    public string? Assignment { get; init; }

    // Financials
    public required decimal FeeTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }

    // Dates
    public required DateTime RegistrationTs { get; init; }
    public DateTime? Modified { get; init; }

    // Discount code (null if none used)
    public string? DiscountCodeName { get; init; }

    // Email opt-out
    public required bool EmailOptOut { get; init; }
}

/// <summary>
/// Response wrapper with aggregates across all matching records.
/// </summary>
public record RegistrationSearchResponse
{
    public required List<RegistrationSearchResultDto> Result { get; init; }
    public required int Count { get; init; }

    // Aggregates across ALL matching records
    public required decimal TotalFees { get; init; }
    public required decimal TotalPaid { get; init; }
    public required decimal TotalOwed { get; init; }
}

/// <summary>
/// Filter options with counts, loaded once on component init.
/// </summary>
public record RegistrationFilterOptionsDto
{
    // Organization
    public required List<FilterOption> Roles { get; init; }
    public required List<FilterOption> Teams { get; init; }
    public required List<FilterOption> Agegroups { get; init; }
    public required List<FilterOption> Divisions { get; init; }
    public required List<FilterOption> Clubs { get; init; }

    // Status
    public required List<FilterOption> ActiveStatuses { get; init; }
    public required List<FilterOption> PayStatuses { get; init; }

    // Demographics
    public required List<FilterOption> Genders { get; init; }
    public required List<FilterOption> Positions { get; init; }
    public required List<FilterOption> GradYears { get; init; }
    public required List<FilterOption> Grades { get; init; }
    public required List<FilterOption> AgeRanges { get; init; }

    // Billing & Mobile
    public required List<FilterOption> ArbSubscriptionStatuses { get; init; }
    public required List<FilterOption> MobileRegistrations { get; init; }

    // Accounting (payment methods + discount codes combined)
    public required List<FilterOption> PaymentTypes { get; init; }

    // Club rep clubs (for roster threshold companion filter)
    public required List<FilterOption> ClubRepClubs { get; init; }
}

public record FilterOption
{
    public required string Value { get; init; }
    public required string Text { get; init; }
    public int Count { get; init; }
    public bool DefaultChecked { get; init; }
}
