namespace TSIC.Contracts.Dtos;

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
    public List<string> AffectedJobYears { get; set; } = new();
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

/// <summary>
/// Result of testing field validation
/// </summary>
public class ValidationTestResult
{
    public string FieldName { get; set; } = string.Empty;
    public string TestValue { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public List<string> Messages { get; set; } = new();
}

/// <summary>
/// Request to clone an existing profile for the current job
/// JobId is determined from regId claim in JWT token
/// </summary>
public class CloneProfileRequest
{
    public string SourceProfileType { get; set; } = string.Empty;
}

/// <summary>
/// Result of cloning a profile
/// </summary>
public class CloneProfileResult
{
    public bool Success { get; set; }
    public string NewProfileType { get; set; } = string.Empty;
    public string SourceProfileType { get; set; } = string.Empty;
    public int FieldCount { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Result when asking server for the next profile type name for a given family
/// </summary>
public class NextProfileTypeResult
{
    public string NewProfileType { get; set; } = string.Empty;
}

/// <summary>
/// Profile metadata enriched with job-specific JsonOptions
/// Used for previewing how a form will appear for a specific job
/// </summary>
public class ProfileMetadataWithOptions
{
    public Guid JobId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public ProfileMetadata Metadata { get; set; } = new();
    public Dictionary<string, object>? JsonOptions { get; set; }
}

/// <summary>
/// Aggregated domain entry for all known profile fields across profiles/jobs
/// Useful for generating a static allowed field list for the editor
/// </summary>
public class AllowedFieldDomainItem
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DefaultInputType { get; set; } = string.Empty; // e.g., TEXT, SELECT, CHECKBOX, HIDDEN
    public string DefaultVisibility { get; set; } = "public";   // public | adminOnly | hidden
    public int SeenInProfiles { get; set; }
}
