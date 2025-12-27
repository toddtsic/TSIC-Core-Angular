// Profile Migration and Editor related interfaces extracted from service for clarity.

export interface ProfileSummary {
    profileType: string;
    jobCount: number;
    migratedJobCount: number;
    allJobsMigrated: boolean;
    sampleJobNames: string[];
}

export interface ProfileMigrationResult {
    profileType: string;
    success: boolean;
    fieldCount: number;
    jobsAffected: number;
    affectedJobIds: string[];
    affectedJobNames: string[];
    affectedJobYears: string[];
    generatedMetadata?: ProfileMetadata;
    warnings: string[];
    errorMessage?: string;
}

export interface ProfileBatchMigrationReport {
    startedAt: Date;
    completedAt?: Date;
    totalProfiles: number;
    successCount: number;
    failureCount: number;
    totalJobsAffected: number;
    results: ProfileMigrationResult[];
    globalWarnings: string[];
}

export interface ProfileMetadata {
    fields: ProfileMetadataField[];
    source?: ProfileMetadataSource;
}

export interface ProfileMetadataField {
    name: string;
    dbColumn: string;
    displayName: string;
    inputType: string;
    dataSource?: string;
    options?: ProfileFieldOption[];
    validation?: FieldValidation;
    order: number;
    visibility: 'public' | 'adminOnly' | 'hidden';
    adminOnly: boolean; // Deprecated: use visibility instead
    computed: boolean;
    helpText?: string;
    placeholder?: string;
    condition?: FieldCondition;
}

export interface ProfileFieldOption {
    value: string;
    label: string;
}

export interface FieldValidation {
    required?: boolean;
    email?: boolean;
    requiredTrue?: boolean; // For checkboxes: must be checked (true)
    minLength?: number;
    maxLength?: number;
    pattern?: string;
    min?: number;
    max?: number;
    compare?: string;
    remote?: string;
    message?: string;
}

export interface FieldCondition {
    field: string;
    value: any;
    operator: string;
}

export interface ProfileMetadataSource {
    sourceFile: string;
    repository: string;
    commitSha: string;
    migratedAt: Date;
    migratedBy: string;
}

export interface ProfileMetadataWithOptions {
    jobId: string;
    jobName: string;
    metadata: ProfileMetadata;
    jsonOptions?: Record<string, any>;
}

export interface ValidationTestResult {
    fieldName: string;
    testValue: string;
    isValid: boolean;
    messages: string[];
}

export interface MigrateProfilesRequest {
    dryRun: boolean;
    profileTypes?: string[];
}

export interface CloneProfileRequest {
    sourceProfileType: string;
}

export interface CloneProfileResult {
    success: boolean;
    newProfileType: string;
    sourceProfileType: string;
    fieldCount: number;
    errorMessage?: string;
}

export interface NextProfileTypeResult {
    newProfileType: string;
}

export interface CurrentJobProfileResponse {
    profileType: string;
    metadata: ProfileMetadata;
}

export interface CurrentJobProfileConfigResponse {
    profileType: string;
    teamConstraint: string | null;
    coreRegform: string;
    metadata: ProfileMetadata | null;
}

export interface OptionSet {
    key: string;
    values: ProfileFieldOption[];
    provider?: string; // e.g., Jobs.JsonOptions or Registrations
    readOnly?: boolean; // true for Registrations sources
}

export interface OptionSetUpdateRequest {
    values: ProfileFieldOption[];
}

export interface RenameOptionSetRequest {
    newKey: string;
}
