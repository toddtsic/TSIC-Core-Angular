using TSIC.API.Dtos;

namespace TSIC.Application.Services;

public interface IClubService
{
    Task<ClubRepRegistrationResponse> RegisterAsync(ClubRepRegistrationRequest request);
    Task<List<ClubSearchResult>> SearchClubsAsync(string query, string? state);
}
