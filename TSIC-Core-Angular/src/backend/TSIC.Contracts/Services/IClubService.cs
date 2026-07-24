using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Services;

public interface IClubService
{
    Task<ClubRepRegistrationResponse> RegisterAsync(ClubRepRegistrationRequest request);
    Task<List<ClubSearchResult>> SearchClubsAsync(string query, string? state);
    Task<AddClubResponse> AddClubAsync(AddClubRequest request, string userId);

    /// <summary>
    /// Read the authenticated user's profile fields (first/last, email, address, phone).
    /// Returns null when the user no longer exists.
    /// </summary>
    Task<ClubRepProfileDto?> GetSelfProfileAsync(string userId);

    /// <summary>
    /// Update the authenticated user's profile fields. Excludes username / password /
    /// club name — those are handled through dedicated flows.
    /// Returns false when the user doesn't exist or Identity rejects the update.
    /// </summary>
    Task<bool> UpdateSelfProfileAsync(string userId, ClubRepProfileUpdateRequest request);

    /// <summary>
    /// Rename a club the authenticated user reps. Allowed only while the club has
    /// no registered teams (IsInUse=false) and the new name doesn't collide with an
    /// existing club. Returns Success=false with a Message on any guard failure.
    /// </summary>
    Task<ClubRenameResponse> RenameClubAsync(string userId, ClubRenameRequest request);

    /// <summary>
    /// Jobs holding teams that belong to this club, with team counts — the impact scope of an
    /// admin rename. Drives the confirm modal's affected-jobs list. Empty when no scheduled teams.
    /// </summary>
    Task<IReadOnlyList<ClubAffectedJob>> GetClubAffectedJobsAsync(int clubId);

    /// <summary>
    /// SuperUser admin rename of a club, allowed even once the club has registered teams. Updates the
    /// single Clubs row, best-effort rewrites the denormalized registration display copies, and
    /// recomposes every affected job's schedule (the canonical name writer). Idempotent: resubmitting
    /// the current name skips the Clubs write and re-runs the recompose as a repair.
    /// </summary>
    Task<AdminClubRenameResponse> AdminRenameClubAsync(string userId, AdminClubRenameRequest request);

    /// <summary>
    /// Invalidate cached club search candidates (call after creating a club).
    /// </summary>
    void InvalidateSearchCache();
}
