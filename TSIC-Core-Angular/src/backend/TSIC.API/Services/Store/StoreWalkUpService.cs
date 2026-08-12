using Microsoft.AspNetCore.Identity;
using TSIC.API.Services.Auth;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Application.Services.Auth;
using TSIC.Contracts.Dtos.Store;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.API.Services.Store;

public sealed class StoreWalkUpService : IStoreWalkUpService
{
	private readonly UserManager<ApplicationUser> _userManager;
	private readonly IJobLookupService _jobLookupService;
	private readonly IJobRepository _jobRepo;
	private readonly IFamiliesRepository _familiesRepo;
	private readonly IRegistrationRepository _registrationRepo;
	private readonly ITeamRepository _teamRepo;
	private readonly ITokenService _tokenService;
	private readonly IRefreshTokenService _refreshTokenService;
	private readonly IConfiguration _configuration;

	public StoreWalkUpService(
		UserManager<ApplicationUser> userManager,
		IJobLookupService jobLookupService,
		IJobRepository jobRepo,
		IFamiliesRepository familiesRepo,
		IRegistrationRepository registrationRepo,
		ITeamRepository teamRepo,
		ITokenService tokenService,
		IRefreshTokenService refreshTokenService,
		IConfiguration configuration)
	{
		_userManager = userManager;
		_jobLookupService = jobLookupService;
		_jobRepo = jobRepo;
		_familiesRepo = familiesRepo;
		_registrationRepo = registrationRepo;
		_teamRepo = teamRepo;
		_tokenService = tokenService;
		_refreshTokenService = refreshTokenService;
		_configuration = configuration;
	}

	public async Task<StoreWalkUpRegisterResponse> RegisterAsync(StoreWalkUpRegisterRequest request)
	{
		// 1. Resolve job
		var jobId = await _jobLookupService.GetJobIdByPathAsync(request.JobPath)
			?? throw new InvalidOperationException("Invalid job path");

		// 2. Gate: reject if walk-up is disabled for this job
		if (!await _jobRepo.IsStoreWalkupAllowedAsync(jobId))
			throw new InvalidOperationException("Walk-up registration is not enabled for this event.");

		// 2b. Anchor gate — BEFORE any writes. Every walk-up registration hangs on the
		//     "Store Merch" team (under "Dropped Teams"); if it's missing, proceeding
		//     would mint a user + family + registration with AssignedTeamId = null — an
		//     orphaned, money-attached row. Clones are born with the anchor
		//     (JobCloneResetRules.CreateStoreMerchTeam); this guard fails loud for
		//     older/hand-built jobs instead of writing bad data.
		var storeMerchTeamId = await _teamRepo.GetStoreMerchTeamIdAsync(jobId)
			?? throw new InvalidOperationException(
				"This event's store is not provisioned for walk-up sales (no 'Store Merch' team). Contact the event director.");

		var jobMeta = await _jobLookupService.GetJobMetadataAsync(request.JobPath);
		var jobLogo = jobMeta?.JobLogoPath;

		// 3. Generate username: {First}{Last}{PhoneLast4}{4RandomGuid}
		var phoneLast4 = request.Phone.Length >= 4
			? request.Phone[^4..]
			: request.Phone;
		var guidSuffix = Guid.NewGuid().ToString("N")[..4];
		var username = $"{request.FirstName}{request.LastName}{phoneLast4}{guidSuffix}";

		// 4. Create user
		var user = new ApplicationUser
		{
			UserName = username,
			Email = request.Email,
			FirstName = request.FirstName,
			LastName = request.LastName,
			Gender = "U", // walk-up form doesn't collect gender; "U" = unknown, never null
			Cellphone = request.Phone,
			Phone = request.Phone,
			StreetAddress = request.StreetAddress,
			City = request.City,
			State = request.State,
			PostalCode = request.Zip,
			Modified = DateTime.Now
		};

		var randomPassword = $"WU!{Guid.NewGuid():N}";
		var createResult = await _userManager.CreateAsync(user, randomPassword);
		if (!createResult.Succeeded)
		{
			var msg = string.Join("; ", createResult.Errors.Select(e => e.Description));
			throw new InvalidOperationException($"Failed to create walk-up user: {msg}");
		}

		// 5. Create family record
		var family = new TSIC.Domain.Entities.Families
		{
			FamilyUserId = user.Id,
			MomFirstName = request.FirstName,
			MomLastName = request.LastName,
			MomCellphone = request.Phone,
			MomEmail = request.Email,
			Modified = DateTime.Now,
			LebUserId = TsicConstants.SuperUserId
		};
		_familiesRepo.Add(family);
		await _familiesRepo.SaveChangesAsync();

		// 6. (Store Merch anchor already resolved at step 2b — before any writes.)

		// 7. Create registration
		var regId = Guid.NewGuid();
		var registration = new Registrations
		{
			RegistrationId = regId,
			RegistrationTs = DateTime.Now,
			RegistrationCategory = "Store Purchase",
			Assignment = "Store Purchase",
			RoleId = RoleConstants.Player,
			UserId = user.Id,
			FamilyUserId = user.Id,
			JobId = jobId,
			AssignedTeamId = storeMerchTeamId,
			BActive = true,
			Modified = DateTime.Now,
			LebUserId = TsicConstants.SuperUserId
		};
		_registrationRepo.Add(registration);
		await _registrationRepo.SaveChangesAsync();

		// 8. Issue JWT
		var accessToken = _tokenService.GenerateEnrichedJwtToken(
			user,
			regId.ToString(),
			request.JobPath,
			jobLogo,
			RoleConstants.Names.PlayerName);

		var refreshToken = _refreshTokenService.GenerateRefreshToken(user.Id);

		var expiryMinutes = int.Parse(
			_configuration.GetSection("JwtSettings")["ExpirationMinutes"] ?? "60");

		return new StoreWalkUpRegisterResponse
		{
			AccessToken = accessToken,
			RefreshToken = refreshToken,
			ExpiresIn = expiryMinutes * 60
		};
	}
}
