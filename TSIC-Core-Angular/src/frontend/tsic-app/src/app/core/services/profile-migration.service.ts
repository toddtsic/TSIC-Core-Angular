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
    validation?: FieldValidation;
    order: number;
    adminOnly: boolean;
    computed: boolean;
    helpText?: string;
    placeholder?: string;
    condition?: FieldCondition;
}

export interface FieldValidation {
    required?: boolean;
    email?: boolean;
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
}
