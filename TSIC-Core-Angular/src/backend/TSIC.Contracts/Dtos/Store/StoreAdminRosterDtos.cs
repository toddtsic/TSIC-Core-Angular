namespace TSIC.Contracts.Dtos.Store;

/// <summary>
/// One row of the Store Administrators roster — legacy's `StoreAdminAdd/Index` jqGrid,
/// column for column: Active · Username · LastName · FirstName · Email · Cell Phone.
/// </summary>
public record StoreAdminRosterRowDto
{
    public required Guid RegistrationId { get; init; }
    public required string UserName { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public string? Cellphone { get; init; }
    public required bool IsActive { get; init; }
}

/// <summary>
/// Grant Store Admin on this job to an existing account.
/// </summary>
/// <remarks>
/// Legacy's add branch MINTED a brand-new AspNetUsers row with `password == username`,
/// gender "F" and a 1980-01-01 date of birth, then registered it. That path is deliberately
/// not carried forward: the AM-004 ruling (2026-07-29, extended 2026-08-13) made every admin
/// grant go through an existing, eligibility-checked account. So this request names a user
/// rather than describing one — the typeahead is the only way to fill it.
/// </remarks>
public record StoreAdminAddRequest
{
    public required string UserName { get; init; }
}

/// <summary>
/// Edit an existing Store Admin. Mirrors exactly what legacy's Edit actually wrote:
/// the registration's Active flag, and the user's email and cell phone.
/// </summary>
/// <remarks>
/// Legacy's grid also marked Username, LastName and FirstName editable, but its controller
/// never read them on the edit branch — typing a new surname there silently did nothing.
/// Those three are read-only here so the form cannot promise a write the server discards.
/// </remarks>
public record StoreAdminUpdateRequest
{
    public required bool IsActive { get; init; }
    public required string Email { get; init; }
    public string? Cellphone { get; init; }
}
