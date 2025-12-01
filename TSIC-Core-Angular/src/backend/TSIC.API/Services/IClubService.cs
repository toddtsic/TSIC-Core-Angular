using TSIC.API.Dtos;

namespace TSIC.API.Services;

public interface IClubService
{
    Task<ClubRegistrationResponse> RegisterAsync(ClubRegistrationRequest request);
    Task<List<ClubSearchResult>> SearchClubsAsync(string query, string? state);
}
