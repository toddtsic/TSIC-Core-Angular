namespace TSIC.API.Dtos;

/// <summary>
/// Summary of a profile type and its usage across jobs
/// </summary>
public class ProfileSummary
{
    public string ProfileType { get; set; } = string.Empty;
    public int JobCount { get; set; }
    public int MigratedJobCount { get; set; }
    public bool AllJobsMigrated { get; set; }
    public List<string> SampleJobNames { get; set; } = new();
}

/// <summary>
/// Result of migrating a single profile type across all jobs
/// </summary>
public class ProfileMigrationResult
{
    public string ProfileType { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int FieldCount { get; set; }
    public int JobsAffected { get; set; }
    public List<Guid> AffectedJobIds { get; set; } = new();
    public List<string> AffectedJobNames { get; set; } = new();
    public ProfileMetadata? GeneratedMetadata { get; set; }
    public List<string> Warnings { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Report for batch profile migrations
/// </summary>
public class ProfileBatchMigrationReport
{
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int TotalProfiles { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int TotalJobsAffected { get; set; }
    public List<ProfileMigrationResult> Results { get; set; } = new();
    public List<string> GlobalWarnings { get; set; } = new();
}

/// <summary>
/// Request to migrate multiple profiles
/// </summary>
public class MigrateProfilesRequest
{
    public bool DryRun { get; set; } = true;
    public List<string>? ProfileTypes { get; set; }
}
