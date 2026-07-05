import { ChangeDetectionStrategy, Component, signal, computed, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdultProfileMigrationService } from '@infrastructure/services/adult-profile-migration.service';
import { ProfileMetadata, ProfileMetadataField, ValidationTestResult, AdultRoleKey } from '@infrastructure/view-models/profile-migration.models';
import { FieldSetEditorComponent } from '../field-set-editor/field-set-editor.component';
import { ADULT_ALLOWED_FIELDS } from '../adult-allowed-fields';

/**
 * Adult side of the segmented profile editor — the type-scoped mirror of the player editor.
 *
 * Edits a CANONICAL profile (AC1/AC2), not a single job: writes propagate to every already-materialized
 * job of that profile via the type-scoped adult endpoints. Three role blocks (Coach/Volunteer, Referee,
 * Recruiter) are held in memory; the active role saves on every field mutation. USA Lacrosse is NOT
 * editable here — it is an orthogonal per-job capability re-composed from RegformName_Coach on save.
 */
@Component({
    selector: 'app-adult-profile-editor-panel',
    standalone: true,
    imports: [CommonModule, FormsModule, FieldSetEditorComponent],
    templateUrl: './adult-profile-editor-panel.component.html',
    styleUrl: './adult-profile-editor-panel.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class AdultProfileEditorPanelComponent implements OnInit {
    private readonly service = inject(AdultProfileMigrationService);

    readonly adultAllowedFields = ADULT_ALLOWED_FIELDS;

    // Canonical profiles (AC1/AC2) sourced from the summary endpoint.
    readonly profiles = this.service.adultSummaries;

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

    selectedProfile = signal<string | null>(null);
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

    selectedSummary = computed(() => this.profiles().find(p => p.profile === this.selectedProfile()) ?? null);
    selectedDisplay = computed(() => this.selectedSummary()?.displayName ?? this.selectedProfile() ?? '');
    // A profile with zero materialized jobs can be viewed (catalog template) but edits reach nothing.
    selectedNotMigrated = computed(() => {
        const s = this.selectedSummary();
        return !!s && s.migratedJobCount === 0;
    });
    selectedUsLaxJobs = computed(() => this.selectedSummary()?.usLaxJobCount ?? 0);

    // Test validation result (owned here; passed down to the field editor)
    testResult = signal<ValidationTestResult | null>(null);
    isTesting = signal(false);

    ngOnInit(): void {
        this.isLoading.set(true);
        this.service.loadAdultSummaries(summaries => {
            if (summaries.length > 0 && !this.selectedProfile()) {
                this.selectProfile(summaries[0].profile);
            } else {
                this.isLoading.set(false);
            }
        });
    }

    onProfileChange(profile: string | null): void {
        if (!profile) return;
        this.selectProfile(profile);
    }

    private selectProfile(profile: string): void {
        this.selectedProfile.set(profile);
        this.activeRole.set('UnassignedAdult');
        this.errorMessage.set(null);
        this.successMessage.set(null);
        this.isLoading.set(true);
        this.service.getAdultProfileMetadata(
            profile,
            set => {
                this.roleMetadata.set({
                    UnassignedAdult: (set.unassignedAdult ?? this.emptyMeta()) as unknown as ProfileMetadata,
                    Referee: (set.referee ?? this.emptyMeta()) as unknown as ProfileMetadata,
                    Recruiter: (set.recruiter ?? this.emptyMeta()) as unknown as ProfileMetadata
                });
                this.isLoading.set(false);
            },
            err => {
                this.errorMessage.set(err?.error?.error || err?.error?.message || 'Failed to load adult profile metadata.');
                this.isLoading.set(false);
            }
        );
    }

    selectRole(role: AdultRoleKey): void {
        this.activeRole.set(role);
        this.successMessage.set(null);
        this.errorMessage.set(null);
    }

    // Field mutation from the shared editor: update the active role immutably, then persist it.
    onActiveFieldsChange(newFields: ProfileMetadataField[]): void {
        const role = this.activeRole();
        this.roleMetadata.update(m => ({ ...m, [role]: { ...m[role], fields: newFields } }));
        this.saveActiveRole();
    }

    onValidationTest(e: { field: ProfileMetadataField; testValue: string }): void {
        this.isTesting.set(true);
        this.testResult.set(null);
        this.service.testValidation(
            e.field,
            e.testValue,
            result => { this.testResult.set(result); this.isTesting.set(false); },
            error => {
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

    private saveActiveRole(): void {
        const profile = this.selectedProfile();
        if (!profile) return;
        const role = this.activeRole();
        this.isSaving.set(true);
        this.errorMessage.set(null);
        this.successMessage.set(null);

        this.service.updateAdultProfileRole(
            profile,
            role,
            this.roleMetadata()[role],
            result => {
                if (result.warnings && result.warnings.length > 0) {
                    this.successMessage.set(`${this.activeRoleLabel()} saved. ${result.warnings[0]}`);
                } else {
                    this.successMessage.set(
                        `${this.activeRoleLabel()} form saved to ${result.jobsAffected} job${result.jobsAffected === 1 ? '' : 's'}.`
                    );
                }
                setTimeout(() => this.successMessage.set(null), 3500);
                this.isSaving.set(false);
            },
            err => {
                this.errorMessage.set(err?.error?.error || err?.error?.message || 'Failed to save adult form.');
                this.isSaving.set(false);
            }
        );
    }
}
