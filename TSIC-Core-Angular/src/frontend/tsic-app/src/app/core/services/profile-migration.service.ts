import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

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
    adminOnly: boolean; // Deprecated, use visibility instead
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
    requiredTrue?: boolean;  // For checkboxes: must be checked (true), not just present
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
    allowPayInFull: boolean;
    coreRegform: string;
    metadata: ProfileMetadata | null;
}



// ============================================================================
// Current Job Option Sets (Jobs.JsonOptions)
// ============================================================================
export interface OptionSet {
    key: string;
    values: ProfileFieldOption[];
    provider?: string;      // e.g., Jobs.JsonOptions or Registrations
    readOnly?: boolean;     // true for Registrations sources
}

export interface OptionSetUpdateRequest {
    values: ProfileFieldOption[];
}

export interface RenameOptionSetRequest {
    newKey: string;
}

@Injectable({
    providedIn: 'root'
})
export class ProfileMigrationService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/admin/profile-migration`;

    // Signals for state management
    private readonly _profileSummaries = signal<ProfileSummary[]>([]);
    private readonly _isLoading = signal(false);
    private readonly _errorMessage = signal<string | null>(null);

    // Public read-only signals
    readonly profileSummaries = this._profileSummaries.asReadonly();
    readonly isLoading = this._isLoading.asReadonly();
    readonly errorMessage = this._errorMessage.asReadonly();

    // ============================================================================
    // MIGRATION DASHBOARD APIs
    // ============================================================================

    loadProfileSummaries(): void {
        this._isLoading.set(true);
        this._errorMessage.set(null);

        this.http.get<ProfileSummary[]>(`${this.apiUrl}/profiles`).subscribe({
            next: (profiles) => {
                this._profileSummaries.set(profiles);
                this._isLoading.set(false);
            },
            error: (error) => {
                this._errorMessage.set(error.error?.message || 'Failed to load profile summaries');
                this._isLoading.set(false);
            }
        });
    }

    previewProfileMigration(profileType: string, onSuccess: (result: ProfileMigrationResult) => void, onError?: (error: any) => void): void {
        this._isLoading.set(true);
        this._errorMessage.set(null);

        this.http.get<ProfileMigrationResult>(`${this.apiUrl}/preview-profile/${profileType}`).subscribe({
            next: (result) => {
                this._isLoading.set(false);
                onSuccess(result);
            },
            error: (error) => {
                this._isLoading.set(false);
                const message = error.error?.message || 'Preview failed';
                this._errorMessage.set(message);
                if (onError) {
                    onError(error);
                }
            }
        });
    }

    migrateProfile(profileType: string, onSuccess: (result: ProfileMigrationResult) => void, onError?: (error: any) => void): void {
        this._isLoading.set(true);
        this._errorMessage.set(null);

        this.http.post<ProfileMigrationResult>(`${this.apiUrl}/migrate-profile/${profileType}`, {}).subscribe({
            next: (result) => {
                this._isLoading.set(false);
                // Reload summaries to reflect changes
                this.loadProfileSummaries();
                onSuccess(result);
            },
            error: (error) => {
                this._isLoading.set(false);
                const message = error.error?.message || 'Migration failed';
                this._errorMessage.set(message);
                if (onError) {
                    onError(error);
                }
            }
        });
    }

    migrateAllProfiles(request: MigrateProfilesRequest, onSuccess: (report: ProfileBatchMigrationReport) => void, onError?: (error: any) => void): void {
        this._isLoading.set(true);
        this._errorMessage.set(null);

        this.http.post<ProfileBatchMigrationReport>(`${this.apiUrl}/migrate-all-profiles`, request).subscribe({
            next: (report) => {
                this._isLoading.set(false);
                // Reload summaries to reflect changes
                this.loadProfileSummaries();
                onSuccess(report);
            },
            error: (error) => {
                this._isLoading.set(false);
                const message = error.error?.message || 'Batch migration failed';
                this._errorMessage.set(message);
                if (onError) {
                    onError(error);
                }
            }
        });
    }

    // ============================================================================
    // PROFILE EDITOR APIs
    // ============================================================================

    getProfileMetadata(profileType: string, onSuccess: (metadata: ProfileMetadata) => void, onError?: (error: any) => void): void {
        this._isLoading.set(true);
        this._errorMessage.set(null);

        this.http.get<ProfileMetadata>(`${this.apiUrl}/profiles/${profileType}/metadata`).subscribe({
            next: (metadata) => {
                this._isLoading.set(false);
                onSuccess(metadata);
            },
            error: (error) => {
                this._isLoading.set(false);
                const message = error.error?.message || 'Failed to load metadata';
                this._errorMessage.set(message);
                if (onError) {
                    onError(error);
                }
            }
        });
    }

    /**
     * Get metadata for a profile enriched with a specific job's JsonOptions
     * This allows previewing how the form will appear for that job
     */
    getProfileMetadataWithJobOptions(
        profileType: string,
        jobId: string,
        onSuccess: (result: ProfileMetadataWithOptions) => void,
        onError?: (error: any) => void
    ): void {
        this._isLoading.set(true);
        this._errorMessage.set(null);

        this.http.get<ProfileMetadataWithOptions>(`${this.apiUrl}/profiles/${profileType}/preview/${jobId}`).subscribe({
            next: (result) => {
                this._isLoading.set(false);
                onSuccess(result);
            },
            error: (error) => {
                this._isLoading.set(false);
                const message = error.error?.message || 'Failed to load metadata with job options';
                this._errorMessage.set(message);
                if (onError) {
                    onError(error);
                }
            }
        });
    }

    updateProfileMetadata(profileType: string, metadata: ProfileMetadata, onSuccess: (result: ProfileMigrationResult) => void, onError?: (error: any) => void): void {
        this._isLoading.set(true);
        this._errorMessage.set(null);

        this.http.put<ProfileMigrationResult>(`${this.apiUrl}/profiles/${profileType}/metadata`, metadata).subscribe({
            next: (result) => {
                this._isLoading.set(false);
                // Reload summaries to reflect changes
                this.loadProfileSummaries();
                onSuccess(result);
            },
            error: (error) => {
                this._isLoading.set(false);
                const message = error.error?.message || 'Failed to update metadata';
                this._errorMessage.set(message);
                if (onError) {
                    onError(error);
                }
            }
        });
    }

    /**
     * Get the current job's profile type and metadata (based on regId claim)
     */
    getCurrentJobProfileMetadata(onSuccess: (resp: CurrentJobProfileResponse) => void, onError?: (error: any) => void): void {
        this._isLoading.set(true);
        this._errorMessage.set(null);

        this.http.get<CurrentJobProfileResponse>(`${this.apiUrl}/profiles/current/metadata`).subscribe({
            next: (result) => {
                this._isLoading.set(false);
                onSuccess(result);
            },
            error: (error) => {
                this._isLoading.set(false);
                const message = error.error?.message || 'Failed to load current job metadata';
                this._errorMessage.set(message);
                if (onError) {
                    onError(error);
                }
            }
        });
    }

    testValidation(field: ProfileMetadataField, testValue: string, onSuccess: (result: ValidationTestResult) => void, onError?: (error: any) => void): void {
        this.http.post<ValidationTestResult>(`${this.apiUrl}/test-validation`, { field, testValue }).subscribe({
            next: (result) => {
                onSuccess(result);
            },
            error: (error) => {
                if (onError) {
                    onError(error);
                }
            }
        });
    }

    // ============================================================================
    // PROFILE CREATION APIs
    // ============================================================================

    /**
     * Clone an existing profile with auto-incremented name
     * Creates a new profile for the current user's job (determined from JWT token)
     */
    cloneProfile(sourceProfileType: string, onSuccess: (result: CloneProfileResult) => void, onError?: (error: any) => void): void {
        this._isLoading.set(true);
        this._errorMessage.set(null);

        this.http.post<CloneProfileResult>(`${this.apiUrl}/clone-profile`, { sourceProfileType }).subscribe({
            next: (result) => {
                this._isLoading.set(false);
                // Reload summaries to include new profile
                this.loadProfileSummaries();
                onSuccess(result);
            },
            error: (error) => {
                this._isLoading.set(false);
                const message = error.error?.message || 'Failed to clone profile';
                this._errorMessage.set(message);
                if (onError) {
                    onError(error);
                }
            }
        });
    }

    getNextProfileType(sourceProfileType: string, onSuccess: (result: NextProfileTypeResult) => void, onError?: (error: any) => void): void {
        this.http.get<NextProfileTypeResult>(`${this.apiUrl}/next-profile-type/${encodeURIComponent(sourceProfileType)}`).subscribe({
            next: (result) => onSuccess(result),
            error: (error) => {
                if (onError) onError(error);
            }
        });
    }
    getKnownProfileTypes(onSuccess: (types: string[]) => void, onError?: (error: any) => void): void {
        this.http.get<string[]>(`${this.apiUrl}/known-profile-types`).subscribe({
            next: (types) => onSuccess(types),
            error: (error) => { if (onError) onError(error); }
        });
    }

    // ============================================================================
    // CURRENT JOB PROFILE CONFIG (CoreRegformPlayer parts)
    // ============================================================================

    getCurrentJobProfileConfig(onSuccess: (resp: CurrentJobProfileConfigResponse) => void, onError?: (error: any) => void): void {
        this._isLoading.set(true);
        this._errorMessage.set(null);

        this.http.get<CurrentJobProfileConfigResponse>(`${this.apiUrl}/profiles/current/config`).subscribe({
            next: (resp) => { this._isLoading.set(false); onSuccess(resp); },
            error: (error) => {
                this._isLoading.set(false);
                const message = error.error?.message || 'Failed to load current job profile config';
                this._errorMessage.set(message);
                if (onError) onError(error);
            }
        });
    }

    updateCurrentJobProfileConfig(
        profileType: string,
        teamConstraint: string,
        allowPayInFull: boolean,
        onSuccess: (resp: CurrentJobProfileConfigResponse) => void,
        onError?: (error: any) => void
    ): void {
        this._isLoading.set(true);
        this._errorMessage.set(null);

        this.http.put<CurrentJobProfileConfigResponse>(`${this.apiUrl}/profiles/current/config`, {
            profileType, teamConstraint, allowPayInFull
        }).subscribe({
            next: (resp) => { this._isLoading.set(false); onSuccess(resp); },
            error: (error) => {
                this._isLoading.set(false);
                const message = error.error?.message || 'Failed to update current job profile config';
                this._errorMessage.set(message);
                if (onError) onError(error);
            }
        });
    }

    // ============================================================================
    // CURRENT JOB OPTION SET APIs (Jobs.JsonOptions)
    // ============================================================================

    getCurrentJobOptionSets(onSuccess: (sets: OptionSet[]) => void, onError?: (error: any) => void): void {
        this._isLoading.set(true);
        this._errorMessage.set(null);

        this.http.get<OptionSet[]>(`${this.apiUrl}/profiles/current/options`).subscribe({
            next: (sets) => {
                this._isLoading.set(false);
                onSuccess(sets);
            },
            error: (error) => {
                this._isLoading.set(false);
                const message = error.error?.message || 'Failed to load option sets';
                this._errorMessage.set(message);
                if (onError) onError(error);
            }
        });
    }

    createCurrentJobOptionSet(request: OptionSet, onSuccess: (created: OptionSet) => void, onError?: (error: any) => void): void {
        this._isLoading.set(true);
        this._errorMessage.set(null);

        this.http.post<OptionSet>(`${this.apiUrl}/profiles/current/options`, request).subscribe({
            next: (created) => {
                this._isLoading.set(false);
                onSuccess(created);
            },
            error: (error) => {
                this._isLoading.set(false);
                const message = error.error?.message || 'Failed to create option set';
                this._errorMessage.set(message);
                if (onError) onError(error);
            }
        });
    }

    updateCurrentJobOptionSet(key: string, values: ProfileFieldOption[], onSuccess: (updated: OptionSet) => void, onError?: (error: any) => void): void {
        this._isLoading.set(true);
        this._errorMessage.set(null);

        const body: OptionSetUpdateRequest = { values };
        this.http.put<OptionSet>(`${this.apiUrl}/profiles/current/options/${encodeURIComponent(key)}`, body).subscribe({
            next: (updated) => {
                this._isLoading.set(false);
                onSuccess(updated);
            },
            error: (error) => {
                this._isLoading.set(false);
                const message = error.error?.message || 'Failed to update option set';
                this._errorMessage.set(message);
                if (onError) onError(error);
            }
        });
    }

    deleteCurrentJobOptionSet(key: string, onSuccess: () => void, onError?: (error: any) => void): void {
        this._isLoading.set(true);
        this._errorMessage.set(null);

        this.http.delete(`${this.apiUrl}/profiles/current/options/${encodeURIComponent(key)}`).subscribe({
            next: () => {
                this._isLoading.set(false);
                onSuccess();
            },
            error: (error) => {
                this._isLoading.set(false);
                const message = error.error?.message || 'Failed to delete option set';
                this._errorMessage.set(message);
                if (onError) onError(error);
            }
        });
    }

    renameCurrentJobOptionSet(oldKey: string, newKey: string, onSuccess: (resp: { updatedKey: string; referencingFields: string[] }) => void, onError?: (error: any) => void): void {
        this._isLoading.set(true);
        this._errorMessage.set(null);

        const body: RenameOptionSetRequest = { newKey };
        this.http.post<{ updatedKey: string; referencingFields: string[] }>(`${this.apiUrl}/profiles/current/options/${encodeURIComponent(oldKey)}/rename`, body).subscribe({
            next: (resp) => {
                this._isLoading.set(false);
                onSuccess(resp);
            },
            error: (error) => {
                this._isLoading.set(false);
                const message = error.error?.message || 'Failed to rename option set';
                this._errorMessage.set(message);
                if (onError) onError(error);
            }
        });
    }

    // (Removed) Current job option sources APIs – UI no longer exposes these
}
