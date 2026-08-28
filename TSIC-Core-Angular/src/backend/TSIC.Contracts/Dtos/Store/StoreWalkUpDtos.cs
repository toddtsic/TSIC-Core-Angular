using System.ComponentModel.DataAnnotations;

namespace TSIC.Contracts.Dtos.Store;

/// <summary>
/// The counter form. Annotations are legacy's <c>StoreWalkUpRegistrationDto</c> verbatim — every
/// field required, a real email, phone exactly ten digits, ZIP <c>12345</c> or <c>12345-6789</c>.
///
/// <para>
/// They are here rather than only on the Angular form because this endpoint is
/// <c>[AllowAnonymous]</c> and one POST mints a user, a family and a registration. A junk phone
/// or ZIP is a permanent row, not a rejected form.
/// </para>
/// </summary>
public record StoreWalkUpRegisterRequest
{
	[Required]
	public required string JobPath { get; init; }

	[Required]
	public required string FirstName { get; init; }

	[Required]
	public required string LastName { get; init; }

	[Required]
	[EmailAddress]
	public required string Email { get; init; }

	[Required]
	[RegularExpression(@"^[0-9]{10}$", ErrorMessage = "Invalid phone number, enter 10 digits only.")]
	public required string Phone { get; init; }

	[Required]
	public required string StreetAddress { get; init; }

	[Required]
	public required string City { get; init; }

	[Required]
	public required string State { get; init; }

	[Required]
	[RegularExpression(@"^\d{5}(-\d{4})?$", ErrorMessage = "Invalid ZIP code format.")]
	public required string Zip { get; init; }
}

public record StoreWalkUpRegisterResponse
{
	public required string AccessToken { get; init; }
	public required string RefreshToken { get; init; }
	public required int ExpiresIn { get; init; }
}
