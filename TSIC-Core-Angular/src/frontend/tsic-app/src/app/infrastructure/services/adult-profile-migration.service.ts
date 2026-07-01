import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '@environments/environment';
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
 * Admin API for the per-job, role-keyed ADULT form designer. Reads/writes Jobs.AdultProfileMetadataJson
 * for the current job (resolved server-side from the JWT regId). Distinct from the player
 * ProfileMigrationService, which edits a shared profile-type template across jobs.
 */
@Injectable({ providedIn: 'root' })
export class AdultProfileMigrationService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/admin/profile-migration`;

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
