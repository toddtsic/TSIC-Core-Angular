using TSIC.Contracts.Dtos;
using TSIC.Domain.Adults;
using TSIC.Domain.Entities;

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
    Task<AdultRoleMetadataSet?> GetCurrentJobAdultMetadataAsync(Guid regId);
    Task<ProfileMetadata?> UpdateCurrentJobAdultRoleMetadataAsync(Guid regId, string roleKey, ProfileMetadata metadata);

    // Adult profile summary (AC1/AC2/AC3 canonical profiles ← legacy RegformName_Coach). The bulk
    // materialization members are gone: adult forms are DERIVED from RegformName_Coach, so an empty
    // AdultProfileMetadataJson means "use the catalog". Configure → Job → Adult is the single writer.
    Task<List<AdultProfileSummary>> GetAdultProfileSummariesAsync();

    // Adult profile editor (type-scoped, mirrors the player profile editor)

    /// <summary>
    /// The canonical adult profile the CURRENT job is configured for, derived from its
    /// <c>RegformName_Coach</c>. Lets the editor open on the job's own profile instead of defaulting to
    /// the first in the list (AC1), which silently misled anyone editing a job that is not AC1.
    /// </summary>
    Task<CurrentJobAdultProfileDto?> GetCurrentJobAdultProfileAsync(Guid regId);

    Task<AdultRoleMetadataSet> GetAdultProfileMetadataAsync(string profile);
    Task<AdultProfileMigrationResult> UpdateAdultProfileRoleAsync(string profile, string roleKey, ProfileMetadata metadata);

    /// <summary>
    /// Rebuild ONE job's coach (UnassignedAdult) role from a chosen canonical profile + USLax capability.
    /// When the job already has a materialized blob, only the coach role is replaced (Referee/Recruiter are
    /// preserved); otherwise the full three-role set is built. Mutates <paramref name="job"/>.JsonOptions
    /// (apparel option-set seeding, upsert-if-absent) and returns the new AdultProfileMetadataJson. Pure
    /// compute — does NOT persist and does NOT touch RegformName_Coach; the caller writes the reverse-mapped
    /// legacy identity (<see cref="Adults.AdultFormCatalog.ToLegacyRegformName"/>) and saves.
    /// </summary>
    string ComputeCoachFormSwap(Jobs job, string profile, AdultUsLaxMode usLax);
}
