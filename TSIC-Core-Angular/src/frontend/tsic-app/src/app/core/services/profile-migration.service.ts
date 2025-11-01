import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
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

    // ============================================================================
    // MIGRATION DASHBOARD APIs
    // ============================================================================

    getProfileSummaries(): Observable<ProfileSummary[]> {
        return this.http.get<ProfileSummary[]>(`${this.apiUrl}/profiles`);
    }

    previewProfileMigration(profileType: string): Observable<ProfileMigrationResult> {
        return this.http.get<ProfileMigrationResult>(`${this.apiUrl}/preview-profile/${profileType}`);
    }

    migrateProfile(profileType: string): Observable<ProfileMigrationResult> {
        return this.http.post<ProfileMigrationResult>(`${this.apiUrl}/migrate-profile/${profileType}`, {});
    }

    migrateAllProfiles(request: MigrateProfilesRequest): Observable<ProfileBatchMigrationReport> {
        return this.http.post<ProfileBatchMigrationReport>(`${this.apiUrl}/migrate-all-profiles`, request);
    }

    // ============================================================================
    // PROFILE EDITOR APIs
    // ============================================================================

    getProfileMetadata(profileType: string): Observable<ProfileMetadata> {
        return this.http.get<ProfileMetadata>(`${this.apiUrl}/profiles/${profileType}/metadata`);
    }

    updateProfileMetadata(profileType: string, metadata: ProfileMetadata): Observable<ProfileMigrationResult> {
        return this.http.put<ProfileMigrationResult>(`${this.apiUrl}/profiles/${profileType}/metadata`, metadata);
    }

    testValidation(field: ProfileMetadataField, testValue: string): Observable<ValidationTestResult> {
        return this.http.post<ValidationTestResult>(`${this.apiUrl}/test-validation`, { field, testValue });
    }

    // ============================================================================
    // PROFILE CREATION APIs
    // ============================================================================

    /**
     * Clone an existing profile with auto-incremented name
     * Creates a new profile for the current user's job (determined from JWT token)
     */
    cloneProfile(sourceProfileType: string): Observable<CloneProfileResult> {
        return this.http.post<CloneProfileResult>(`${this.apiUrl}/clone-profile`, {
            sourceProfileType
        });
    }
}
