import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import {
    ProfileSummary,
    ProfileMigrationResult,
    ProfileBatchMigrationReport,
    ProfileMetadata,
    ProfileMetadataField,
    ProfileFieldOption,
    ProfileMetadataWithOptions,
    ValidationTestResult,
    MigrateProfilesRequest,
    CloneProfileResult,
    NextProfileTypeResult,
    CurrentJobProfileResponse,
    CurrentJobProfileConfigResponse,
    OptionSet,
    OptionSetUpdateRequest,
    RenameOptionSetRequest
} from '../models/profile-migration.models';

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
    private readonly _currentJobProfileConfig = signal<CurrentJobProfileConfigResponse | null>(null);
    private readonly _currentJobProfile = signal<CurrentJobProfileResponse | null>(null);
    private readonly _currentOptionSets = signal<OptionSet[]>([]);
    private readonly _currentMetadata = signal<ProfileMetadata | null>(null);
    private readonly _optionsLoading = signal(false);
    private readonly _optionsError = signal<string | null>(null);
    private readonly _singleMigrationResult = signal<ProfileMigrationResult | null>(null);
    private readonly _batchMigrationReport = signal<ProfileBatchMigrationReport | null>(null);
    private readonly _previewResult = signal<ProfileMigrationResult | null>(null);
    private readonly _isMigrating = signal(false);

    // Public read-only signals
    readonly profileSummaries = this._profileSummaries.asReadonly();
    readonly isLoading = this._isLoading.asReadonly();
    readonly errorMessage = this._errorMessage.asReadonly();
    readonly currentJobProfileConfig = this._currentJobProfileConfig.asReadonly();
    readonly currentJobProfile = this._currentJobProfile.asReadonly();
    readonly currentOptionSets = this._currentOptionSets.asReadonly();
    readonly currentMetadata = this._currentMetadata.asReadonly();
    readonly optionsLoading = this._optionsLoading.asReadonly();
    readonly optionsError = this._optionsError.asReadonly();
    readonly singleMigrationResult = this._singleMigrationResult.asReadonly();
    readonly batchMigrationReport = this._batchMigrationReport.asReadonly();
    readonly previewResult = this._previewResult.asReadonly();
    readonly isMigrating = this._isMigrating.asReadonly();

    // ============================================================================
    // MIGRATION DASHBOARD APIs
    // ============================================================================

    // Generic helper to reduce repetitive subscribe boilerplate.
    // Accepts an observable, loading/error signal setters, and success/error callbacks.
    private runCall<T>(obs: { subscribe: Function }, cfg: {
        setLoading?: (v: boolean) => void;
        setError?: (m: string | null) => void;
        errorMessage?: string; // default fallback message
    }, onSuccess: (data: T) => void, onError?: (err: any) => void): void {
        cfg.setLoading?.(true);
        cfg.setError?.(null);
        const fallback = cfg.errorMessage || 'Request failed';
        (obs as any).subscribe({
            next: (data: T) => { cfg.setLoading?.(false); onSuccess(data); },
            error: (err: any) => {
                cfg.setLoading?.(false);
                const msg = err?.error?.message || fallback;
                cfg.setError?.(msg);
                if (onError) onError(err);
            }
        });
    }

    loadProfileSummaries(): void {
        this.runCall<ProfileSummary[]>(
            this.http.get<ProfileSummary[]>(`${this.apiUrl}/profiles`),
            { setLoading: v => this._isLoading.set(v), setError: m => this._errorMessage.set(m), errorMessage: 'Failed to load profile summaries' },
            profiles => this._profileSummaries.set(profiles)
        );
    }

    previewProfileMigration(profileType: string, onSuccess: (result: ProfileMigrationResult) => void, onError?: (error: any) => void): void {
        this.runCall<ProfileMigrationResult>(
            this.http.get<ProfileMigrationResult>(`${this.apiUrl}/preview-profile/${profileType}`),
            { setLoading: v => this._isLoading.set(v), setError: m => this._errorMessage.set(m), errorMessage: 'Preview failed' },
            result => { this._previewResult.set(result); onSuccess(result); },
            onError
        );
    }

    migrateProfile(profileType: string, onSuccess: (result: ProfileMigrationResult) => void, onError?: (error: any) => void): void {
        this.runCall<ProfileMigrationResult>(
            this.http.post<ProfileMigrationResult>(`${this.apiUrl}/migrate-profile/${profileType}`, {}),
            { setLoading: v => this._isMigrating.set(v), setError: m => this._errorMessage.set(m), errorMessage: 'Migration failed' },
            result => { this.loadProfileSummaries(); this._singleMigrationResult.set(result); onSuccess(result); },
            onError
        );
    }

    migrateAllProfiles(request: MigrateProfilesRequest, onSuccess: (report: ProfileBatchMigrationReport) => void, onError?: (error: any) => void): void {
        this.runCall<ProfileBatchMigrationReport>(
            this.http.post<ProfileBatchMigrationReport>(`${this.apiUrl}/migrate-all-profiles`, request),
            { setLoading: v => this._isMigrating.set(v), setError: m => this._errorMessage.set(m), errorMessage: 'Batch migration failed' },
            report => { this.loadProfileSummaries(); this._batchMigrationReport.set(report); onSuccess(report); },
            onError
        );
    }

    // ============================================================================
    // PROFILE EDITOR APIs
    // ============================================================================

    getProfileMetadata(profileType: string, onSuccess: (metadata: ProfileMetadata) => void, onError?: (error: any) => void): void {
        this.runCall<ProfileMetadata>(
            this.http.get<ProfileMetadata>(`${this.apiUrl}/profiles/${profileType}/metadata`),
            { setLoading: v => this._isLoading.set(v), setError: m => this._errorMessage.set(m), errorMessage: 'Failed to load metadata' },
            meta => { this._currentMetadata.set(meta); onSuccess(meta); },
            onError
        );
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
        this.runCall<ProfileMetadataWithOptions>(
            this.http.get<ProfileMetadataWithOptions>(`${this.apiUrl}/profiles/${profileType}/preview/${jobId}`),
            { setLoading: v => this._isLoading.set(v), setError: m => this._errorMessage.set(m), errorMessage: 'Failed to load metadata with job options' },
            result => { onSuccess(result); },
            onError
        );
    }

    updateProfileMetadata(profileType: string, metadata: ProfileMetadata, onSuccess: (result: ProfileMigrationResult) => void, onError?: (error: any) => void): void {
        this.runCall<ProfileMigrationResult>(
            this.http.put<ProfileMigrationResult>(`${this.apiUrl}/profiles/${profileType}/metadata`, metadata),
            { setLoading: v => this._isLoading.set(v), setError: m => this._errorMessage.set(m), errorMessage: 'Failed to update metadata' },
            result => { this.loadProfileSummaries(); onSuccess(result); },
            onError
        );
    }

    /**
     * Get the current job's profile type and metadata (based on regId claim)
     */
    getCurrentJobProfileMetadata(onSuccess: (resp: CurrentJobProfileResponse) => void, onError?: (error: any) => void): void {
        this.runCall<CurrentJobProfileResponse>(
            this.http.get<CurrentJobProfileResponse>(`${this.apiUrl}/profiles/current/metadata`),
            { setLoading: v => this._isLoading.set(v), setError: m => this._errorMessage.set(m), errorMessage: 'Failed to load current job metadata' },
            result => { this._currentJobProfile.set(result); onSuccess(result); },
            onError
        );
    }

    testValidation(field: ProfileMetadataField, testValue: string, onSuccess: (result: ValidationTestResult) => void, onError?: (error: any) => void): void {
        this.runCall<ValidationTestResult>(
            this.http.post<ValidationTestResult>(`${this.apiUrl}/test-validation`, { field, testValue }),
            { errorMessage: 'Validation test failed', setError: m => this._errorMessage.set(m) },
            result => onSuccess(result),
            onError
        );
    }

    // ============================================================================
    // PROFILE CREATION APIs
    // ============================================================================

    /**
     * Clone an existing profile with auto-incremented name
     * Creates a new profile for the current user's job (determined from JWT token)
     */
    cloneProfile(sourceProfileType: string, onSuccess: (result: CloneProfileResult) => void, onError?: (error: any) => void): void {
        this.runCall<CloneProfileResult>(
            this.http.post<CloneProfileResult>(`${this.apiUrl}/clone-profile`, { sourceProfileType }),
            { setLoading: v => this._isLoading.set(v), setError: m => this._errorMessage.set(m), errorMessage: 'Failed to clone profile' },
            result => { this.loadProfileSummaries(); onSuccess(result); },
            onError
        );
    }

    getNextProfileType(sourceProfileType: string, onSuccess: (result: NextProfileTypeResult) => void, onError?: (error: any) => void): void {
        this.runCall<NextProfileTypeResult>(
            this.http.get<NextProfileTypeResult>(`${this.apiUrl}/next-profile-type/${encodeURIComponent(sourceProfileType)}`),
            { setError: m => this._errorMessage.set(m), errorMessage: 'Failed to get next profile type' },
            result => onSuccess(result),
            onError
        );
    }
    getKnownProfileTypes(onSuccess: (types: string[]) => void, onError?: (error: any) => void): void {
        this.runCall<string[]>(
            this.http.get<string[]>(`${this.apiUrl}/known-profile-types`),
            { setError: m => this._errorMessage.set(m), errorMessage: 'Failed to load known profile types' },
            types => onSuccess(types),
            onError
        );
    }

    // ============================================================================
    // CURRENT JOB PROFILE CONFIG (CoreRegformPlayer parts)
    // ============================================================================

    getCurrentJobProfileConfig(onSuccess: (resp: CurrentJobProfileConfigResponse) => void, onError?: (error: any) => void): void {
        this.runCall<CurrentJobProfileConfigResponse>(
            this.http.get<CurrentJobProfileConfigResponse>(`${this.apiUrl}/profiles/current/config`),
            { setLoading: v => this._isLoading.set(v), setError: m => this._errorMessage.set(m), errorMessage: 'Failed to load current job profile config' },
            resp => { this._currentJobProfileConfig.set(resp); onSuccess(resp); },
            onError
        );
    }

    updateCurrentJobProfileConfig(
        profileType: string,
        teamConstraint: string,
        onSuccess: (resp: CurrentJobProfileConfigResponse) => void,
        onError?: (error: any) => void
    ): void {
        this.runCall<CurrentJobProfileConfigResponse>(
            this.http.put<CurrentJobProfileConfigResponse>(`${this.apiUrl}/profiles/current/config`, { profileType, teamConstraint }),
            { setLoading: v => this._isLoading.set(v), setError: m => this._errorMessage.set(m), errorMessage: 'Failed to update current job profile config' },
            resp => { this._currentJobProfileConfig.set(resp); if (resp.metadata) { this._currentMetadata.set(resp.metadata); } onSuccess(resp); },
            onError
        );
    }

    // ============================================================================
    // CURRENT JOB OPTION SET APIs (Jobs.JsonOptions)
    // ============================================================================

    getCurrentJobOptionSets(onSuccess: (sets: OptionSet[]) => void, onError?: (error: any) => void): void {
        this.runCall<OptionSet[]>(
            this.http.get<OptionSet[]>(`${this.apiUrl}/profiles/current/options`),
            { setLoading: v => this._optionsLoading.set(v), setError: m => { this._optionsError.set(m); this._errorMessage.set(m); }, errorMessage: 'Failed to load option sets' },
            sets => { this._currentOptionSets.set(sets); onSuccess(sets); },
            onError
        );
    }

    createCurrentJobOptionSet(request: OptionSet, onSuccess: (created: OptionSet) => void, onError?: (error: any) => void): void {
        this.runCall<OptionSet>(
            this.http.post<OptionSet>(`${this.apiUrl}/profiles/current/options`, request),
            { setLoading: v => this._optionsLoading.set(v), setError: m => { this._optionsError.set(m); this._errorMessage.set(m); }, errorMessage: 'Failed to create option set' },
            created => {
                this._currentOptionSets.update(list => {
                    const exists = list.some(s => s.key.toLowerCase() === created.key.toLowerCase());
                    return exists ? list.map(s => s.key.toLowerCase() === created.key.toLowerCase() ? created : s) : [created, ...list];
                });
                onSuccess(created);
            },
            onError
        );
    }

    updateCurrentJobOptionSet(key: string, values: ProfileFieldOption[], onSuccess: (updated: OptionSet) => void, onError?: (error: any) => void): void {
        const body: OptionSetUpdateRequest = { values };
        this.runCall<OptionSet>(
            this.http.put<OptionSet>(`${this.apiUrl}/profiles/current/options/${encodeURIComponent(key)}`, body),
            { setLoading: v => this._optionsLoading.set(v), setError: m => { this._optionsError.set(m); this._errorMessage.set(m); }, errorMessage: 'Failed to update option set' },
            updated => { this._currentOptionSets.update(list => list.map(s => s.key.toLowerCase() === updated.key.toLowerCase() ? updated : s)); onSuccess(updated); },
            onError
        );
    }

    deleteCurrentJobOptionSet(key: string, onSuccess: () => void, onError?: (error: any) => void): void {
        this.runCall<any>(
            this.http.delete(`${this.apiUrl}/profiles/current/options/${encodeURIComponent(key)}`),
            { setLoading: v => this._optionsLoading.set(v), setError: m => { this._optionsError.set(m); this._errorMessage.set(m); }, errorMessage: 'Failed to delete option set' },
            () => { this._currentOptionSets.update(list => list.filter(s => s.key.toLowerCase() !== key.toLowerCase())); onSuccess(); },
            onError
        );
    }

    renameCurrentJobOptionSet(oldKey: string, newKey: string, onSuccess: (resp: { updatedKey: string; referencingFields: string[] }) => void, onError?: (error: any) => void): void {
        const body: RenameOptionSetRequest = { newKey };
        this.runCall<{ updatedKey: string; referencingFields: string[] }>(
            this.http.post<{ updatedKey: string; referencingFields: string[] }>(`${this.apiUrl}/profiles/current/options/${encodeURIComponent(oldKey)}/rename`, body),
            { setLoading: v => this._optionsLoading.set(v), setError: m => { this._optionsError.set(m); this._errorMessage.set(m); }, errorMessage: 'Failed to rename option set' },
            resp => { this._currentOptionSets.update(list => list.map(s => s.key.toLowerCase() === oldKey.toLowerCase() ? { ...s, key: newKey } : s)); onSuccess(resp); },
            onError
        );
    }

    // (Removed) Current job option sources APIs – UI no longer exposes these
}
