import { ChangeDetectionStrategy, Component, signal, computed, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AdultProfileMigrationService } from '@infrastructure/services/adult-profile-migration.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { ProfileMetadata, ProfileMetadataField, ValidationTestResult, AdultRoleKey } from '@infrastructure/view-models/profile-migration.models';
import { FieldSetEditorComponent } from '../profile-editor/field-set-editor/field-set-editor.component';
import { ADULT_ALLOWED_FIELDS } from './adult-allowed-fields';

@Component({
    selector: 'app-adult-profile-editor',
    standalone: true,
    imports: [CommonModule, RouterLink, FieldSetEditorComponent],
    templateUrl: './adult-profile-editor.component.html',
    styleUrl: './adult-profile-editor.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class AdultProfileEditorComponent implements OnInit {
    private readonly adultService = inject(AdultProfileMigrationService);
    private readonly authService = inject(AuthService);

    readonly adultAllowedFields = ADULT_ALLOWED_FIELDS;
    jobPath = computed(() => this.authService.currentUser()?.jobPath || 'tsic');

    // Role tabs. UnassignedAdult covers Coach/Volunteer AND Staff (same metadata key).
    readonly roleTabs: { key: AdultRoleKey; label: string }[] = [
        { key: 'UnassignedAdult', label: 'Coach / Volunteer' },
        { key: 'Referee', label: 'Referee' },
        { key: 'Recruiter', label: 'Recruiter' }
    ];

    isLoading = signal(false);
    isSaving = signal(false);
    errorMessage = signal<string | null>(null);
    successMessage = signal<string | null>(null);

    activeRole = signal<AdultRoleKey>('UnassignedAdult');

    // All three roles held in memory; only the active role is edited/saved at a time.
    private readonly emptyMeta = (): ProfileMetadata => ({ fields: [] });
    roleMetadata = signal<Record<AdultRoleKey, ProfileMetadata>>({
        UnassignedAdult: this.emptyMeta(),
        Referee: this.emptyMeta(),
        Recruiter: this.emptyMeta()
    });

    activeFields = computed<ProfileMetadataField[]>(() => this.roleMetadata()[this.activeRole()]?.fields ?? []);
    activeRoleLabel = computed(() => this.roleTabs.find(t => t.key === this.activeRole())?.label ?? '');

    // Test validation result (owned here; passed down to the field editor)
    testResult = signal<ValidationTestResult | null>(null);
    isTesting = signal(false);

    ngOnInit() {
        this.isLoading.set(true);
        this.adultService.getCurrentJobAdultMetadata(
            (resp) => {
                this.roleMetadata.set({
                    UnassignedAdult: resp.roles?.unassignedAdult ?? this.emptyMeta(),
                    Referee: resp.roles?.referee ?? this.emptyMeta(),
                    Recruiter: resp.roles?.recruiter ?? this.emptyMeta()
                });
                this.isLoading.set(false);
            },
            (err) => {
                this.errorMessage.set(err?.error?.error || 'Failed to load adult form metadata.');
                this.isLoading.set(false);
            }
        );
    }

    selectRole(role: AdultRoleKey) {
        this.activeRole.set(role);
        this.successMessage.set(null);
        this.errorMessage.set(null);
    }

    // Field mutation from the shared editor: update the active role immutably, then persist it.
    onActiveFieldsChange(newFields: ProfileMetadataField[]) {
        const role = this.activeRole();
        this.roleMetadata.update(m => ({ ...m, [role]: { ...m[role], fields: newFields } }));
        this.saveActiveRole();
    }

    onValidationTest(e: { field: ProfileMetadataField; testValue: string }) {
        this.isTesting.set(true);
        this.testResult.set(null);
        this.adultService.testValidation(
            e.field,
            e.testValue,
            (result) => { this.testResult.set(result); this.isTesting.set(false); },
            (error) => {
                this.testResult.set({
                    isValid: false,
                    messages: [`Test failed: ${error?.error?.error || 'Unknown error'}`],
                    testValue: e.testValue,
                    fieldName: e.field.name
                });
                this.isTesting.set(false);
            }
        );
    }

    private saveActiveRole() {
        const role = this.activeRole();
        this.isSaving.set(true);
        this.errorMessage.set(null);
        this.successMessage.set(null);

        this.adultService.updateCurrentJobAdultRole(
            role,
            this.roleMetadata()[role],
            (resp) => {
                // Echo back the normalized metadata (order/HIDDEN normalization applied server-side).
                this.roleMetadata.update(m => ({ ...m, [role]: resp.metadata }));
                this.successMessage.set(`${this.activeRoleLabel()} form saved.`);
                setTimeout(() => this.successMessage.set(null), 3000);
                this.isSaving.set(false);
            },
            (err) => {
                this.errorMessage.set(err?.error?.error || 'Failed to save adult form.');
                this.isSaving.set(false);
            }
        );
    }
}
