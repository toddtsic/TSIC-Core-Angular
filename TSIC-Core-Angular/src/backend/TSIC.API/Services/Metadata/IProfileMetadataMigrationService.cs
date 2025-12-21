using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Metadata;

public interface IProfileMetadataMigrationService
{
    Task<string> GetNextProfileTypeAsync(string sourceProfileType);
    Task<MigrationResult> PreviewMigrationAsync(Guid jobId);
    Task<MigrationResult> MigrateSingleJobAsync(Guid jobId, bool dryRun = false);
    Task<MigrationReport> MigrateAllJobsAsync(bool dryRun = false, List<string>? profileTypeFilter = null);
    Task<List<ProfileSummary>> GetProfileSummariesAsync();
    Task<List<string>> GetKnownProfileTypesAsync();
    Task<ProfileMigrationResult> PreviewProfileMigrationAsync(string profileType);
    Task<ProfileMigrationResult> MigrateProfileAsync(string profileType, bool dryRun = false);
    Task<ProfileBatchMigrationReport> MigrateMultipleProfilesAsync(
        bool dryRun = false,
        List<string>? profileTypeFilter = null);
    Task<ProfileMetadata?> GetProfileMetadataAsync(string profileType);
    Task<ProfileMetadataWithOptions?> GetProfileMetadataWithJobOptionsAsync(
        string profileType,
        Guid jobId);
    Task<(string? ProfileType, ProfileMetadata? Metadata)> GetCurrentJobProfileMetadataAsync(Guid regId);
    Task<List<OptionSet>> GetCurrentJobOptionSetsAsync(Guid regId);
    Task<OptionSet?> UpsertCurrentJobOptionSetAsync(Guid regId, string key, List<ProfileFieldOption> values);
    Task<List<AllowedFieldDomainItem>> BuildAllowedFieldDomainAsync();
    Task<bool> DeleteCurrentJobOptionSetAsync(Guid regId, string key);
    Task<bool> RenameCurrentJobOptionSetAsync(Guid regId, string oldKey, string newKey);
    Task<List<OptionSet>> GetCurrentJobOptionSourcesAsync(Guid regId);
    Task<ProfileMigrationResult> UpdateProfileMetadataAsync(string profileType, ProfileMetadata metadata);
}
