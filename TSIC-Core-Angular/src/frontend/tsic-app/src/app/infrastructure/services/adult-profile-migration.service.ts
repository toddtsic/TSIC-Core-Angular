import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '@environments/environment';
import {
    AdultProfileSummary,
    AdultProfileMigrationResult,
    AdultProfileBatchMigrationReport,
    AdultMigrateAllRequest,
    AdultRoleMetadataSet
} from '@core/api';
import {
    ProfileMetadata,
    ProfileMetadataField,
    ValidationTestResult,
    AdultRoleKey,
    AdultRoleMetadataResponse,
    UpdateAdultRoleMetadataRequest,
    UpdateAdultRoleMetadataResponse
} from '../view-models/profile-migration.models';

/**
 * Admin API for the ADULT profile tooling — the adult mirror of ProfileMigrationService.
 *
 * Two surfaces, both keyed on the canonical profiles (AC1/AC2) materialized from the legacy
 * Jobs.RegformName_Coach:
 *   1. Migration dashboard — summary / preview / materialize / export-SQL across a profile's jobs.
 *   2. Type-scoped editor — read/write one role's field set for a canonical profile.
 *
 * The per-CURRENT-job methods (getCurrentJobAdultMetadata / updateCurrentJobAdultRole) remain for
 * the in-job adult form designer resolved from the JWT regId.
 */
@Injectable({ providedIn: 'root' })
export class AdultProfileMigrationService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/admin/profile-migration`;

    // Signals for migration-dashboard state
    private readonly _adultSummaries = signal<AdultProfileSummary[]>([]);
    private readonly _isLoading = signal(false);
    private readonly _isMigrating = signal(false);
    private readonly _errorMessage = signal<string | null>(null);
    private readonly _previewResult = signal<AdultProfileMigrationResult | null>(null);
    private readonly _batchReport = signal<AdultProfileBatchMigrationReport | null>(null);

    // Public read-only signals
    readonly adultSummaries = this._adultSummaries.asReadonly();
    readonly isLoading = this._isLoading.asReadonly();
    readonly isMigrating = this._isMigrating.asReadonly();
    readonly errorMessage = this._errorMessage.asReadonly();
    readonly previewResult = this._previewResult.asReadonly();
    readonly batchReport = this._batchReport.asReadonly();

    // Generic helper mirroring ProfileMigrationService.runCall to keep subscribe boilerplate thin.
    private runCall<T>(obs: { subscribe: Function }, cfg: {
        setLoading?: (v: boolean) => void;
        setError?: (m: string | null) => void;
        errorMessage?: string;
    }, onSuccess: (data: T) => void, onError?: (err: any) => void): void {
        cfg.setLoading?.(true);
        cfg.setError?.(null);
        const fallback = cfg.errorMessage || 'Request failed';
        (obs as any).subscribe({
            next: (data: T) => { cfg.setLoading?.(false); onSuccess(data); },
            error: (err: any) => {
                cfg.setLoading?.(false);
                const msg = err?.error?.message || err?.error?.error || fallback;
                cfg.setError?.(msg);
                if (onError) onError(err);
            }
        });
    }

    // ============================================================================
    // ADULT MIGRATION DASHBOARD (canonical profiles AC1/AC2)
    // ============================================================================

    /** Load the canonical adult profile summaries (job counts, USLax counts, migrated status). */
    loadAdultSummaries(onSuccess?: (summaries: AdultProfileSummary[]) => void): void {
        this.runCall<AdultProfileSummary[]>(
            this.http.get<AdultProfileSummary[]>(`${this.apiUrl}/adult/summary`),
            { setLoading: v => this._isLoading.set(v), setError: m => this._errorMessage.set(m), errorMessage: 'Failed to load adult profile summaries' },
            summaries => { this._adultSummaries.set(summaries); onSuccess?.(summaries); }
        );
    }

    /** Dry-run preview of what materializing a single adult profile would produce (3-role set). */
    previewAdultProfile(profile: string, onSuccess: (result: AdultProfileMigrationResult) => void, onError?: (error: any) => void): void {
        this.runCall<AdultProfileMigrationResult>(
            this.http.get<AdultProfileMigrationResult>(`${this.apiUrl}/adult/preview/${encodeURIComponent(profile)}`),
            { setLoading: v => this._isLoading.set(v), setError: m => this._errorMessage.set(m), errorMessage: 'Adult preview failed' },
            result => { this._previewResult.set(result); onSuccess(result); },
            onError
        );
    }

    /** Materialize a single adult profile across its jobs (force re-writes already-migrated jobs). */
    migrateAdultProfile(profile: string, force: boolean, onSuccess: (result: AdultProfileMigrationResult) => void, onError?: (error: any) => void): void {
        const params = force ? '?force=true' : '';
        this.runCall<AdultProfileMigrationResult>(
            this.http.post<AdultProfileMigrationResult>(`${this.apiUrl}/adult/migrate/${encodeURIComponent(profile)}${params}`, {}),
            { setLoading: v => this._isMigrating.set(v), setError: m => this._errorMessage.set(m), errorMessage: 'Adult migration failed' },
            result => { this.loadAdultSummaries(); onSuccess(result); },
            onError
        );
    }

    /** Materialize all adult profiles (or a filtered subset); skips already-migrated unless force. */
    migrateAllAdult(request: AdultMigrateAllRequest, onSuccess: (report: AdultProfileBatchMigrationReport) => void, onError?: (error: any) => void): void {
        this.runCall<AdultProfileBatchMigrationReport>(
            this.http.post<AdultProfileBatchMigrationReport>(`${this.apiUrl}/adult/migrate-all`, request),
            { setLoading: v => this._isMigrating.set(v), setError: m => this._errorMessage.set(m), errorMessage: 'Adult batch migration failed' },
            report => { this.loadAdultSummaries(); this._batchReport.set(report); onSuccess(report); },
            onError
        );
    }

    /** Download the SQL script that applies adult metadata to another database. */
    exportAdultSql(onSuccess: (blob: Blob) => void, onError?: (error: any) => void): void {
        this._isLoading.set(true);
        this._errorMessage.set(null);
        this.http.post(`${this.apiUrl}/adult/export-sql`, {}, { responseType: 'blob' }).subscribe({
            next: (blob: Blob) => { this._isLoading.set(false); onSuccess(blob); },
            error: (err: any) => {
                this._isLoading.set(false);
                const msg = err?.error?.message || 'Failed to export adult SQL script';
                this._errorMessage.set(msg);
                if (onError) onError(err);
            }
        });
    }

    clearPreviewResult(): void {
        this._previewResult.set(null);
    }

    clearError(): void {
        this._errorMessage.set(null);
    }

    // ============================================================================
    // ADULT PROFILE EDITOR (type-scoped by canonical profile AC1/AC2)
    // ============================================================================

    /** Load the role-keyed metadata for a canonical profile (representative materialized job). */
    getAdultProfileMetadata(profile: string, onSuccess: (set: AdultRoleMetadataSet) => void, onError?: (error: any) => void): void {
        this.runCall<AdultRoleMetadataSet>(
            this.http.get<AdultRoleMetadataSet>(`${this.apiUrl}/adult-profiles/${encodeURIComponent(profile)}/metadata`),
            { setLoading: v => this._isLoading.set(v), setError: m => this._errorMessage.set(m), errorMessage: 'Failed to load adult profile metadata' },
            set => onSuccess(set),
            onError
        );
    }

    /** Replace ONE role's field set across all materialized jobs of a canonical profile. */
    updateAdultProfileRole(
        profile: string,
        roleKey: AdultRoleKey,
        metadata: ProfileMetadata,
        onSuccess: (result: AdultProfileMigrationResult) => void,
        onError?: (error: any) => void
    ): void {
        const body = { roleKey, metadata };
        this.runCall<AdultProfileMigrationResult>(
            this.http.put<AdultProfileMigrationResult>(`${this.apiUrl}/adult-profiles/${encodeURIComponent(profile)}/metadata`, body),
            { setLoading: v => this._isLoading.set(v), setError: m => this._errorMessage.set(m), errorMessage: 'Failed to update adult profile role' },
            result => onSuccess(result),
            onError
        );
    }

    // ============================================================================
    // CURRENT-JOB ADULT FORM DESIGNER (resolved from JWT regId)
    // ============================================================================

    /** Load all three adult roles' metadata for the current job. */
    getCurrentJobAdultMetadata(onSuccess: (resp: AdultRoleMetadataResponse) => void, onError?: (error: any) => void): void {
        this.http.get<AdultRoleMetadataResponse>(`${this.apiUrl}/profiles/current/adult-metadata`).subscribe({
            next: resp => onSuccess(resp),
            error: err => { if (onError) onError(err); }
        });
    }

    /** Replace ONE role's field set for the current job (backend preserves the other roles). */
    updateCurrentJobAdultRole(
        roleKey: AdultRoleKey,
        metadata: ProfileMetadata,
        onSuccess: (resp: UpdateAdultRoleMetadataResponse) => void,
        onError?: (error: any) => void
    ): void {
        const body: UpdateAdultRoleMetadataRequest = { roleKey, metadata };
        this.http.put<UpdateAdultRoleMetadataResponse>(`${this.apiUrl}/profiles/current/adult-metadata`, body).subscribe({
            next: resp => onSuccess(resp),
            error: err => { if (onError) onError(err); }
        });
    }

    /** Test a single field's validation rules (shared, role-agnostic endpoint). */
    testValidation(field: ProfileMetadataField, testValue: string, onSuccess: (result: ValidationTestResult) => void, onError?: (error: any) => void): void {
        this.http.post<ValidationTestResult>(`${this.apiUrl}/test-validation`, { field, testValue }).subscribe({
            next: result => onSuccess(result),
            error: err => { if (onError) onError(err); }
        });
    }
}
