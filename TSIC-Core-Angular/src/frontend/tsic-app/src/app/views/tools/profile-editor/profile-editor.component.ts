import { ChangeDetectionStrategy, Component, signal, computed, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ProfileMigrationService } from '@infrastructure/services/profile-migration.service';
import { ProfileMetadata, ProfileMetadataField, ValidationTestResult, CurrentJobProfileConfigResponse } from '@infrastructure/view-models/profile-migration.models';
import { ToastService } from '@shared-ui/toast.service';
import { OptionsPanelComponent } from './options-panel/options-panel.component';
import { FieldSetEditorComponent } from './field-set-editor/field-set-editor.component';
import { ALLOWED_PROFILE_FIELDS } from './allowed-fields';
import { AuthService } from '@infrastructure/services/auth.service';
import { AdultProfileEditorPanelComponent } from './adult-profile-editor-panel/adult-profile-editor-panel.component';

/**
 * Player form editor — scoped to the CURRENT job only.
 *
 * POST-GO-LIVE LOCKDOWN (2026-08-16). This screen used to offer three edit scopes:
 * "This job", "A specific job" (any job by picker) and "This template (all jobs)" (a fan-out
 * that rewrote the player form on every job carrying the profile type). It also carried a
 * Copy Forms card that seeded this job's form from another job, a "This Job's Profile
 * Assignment" card, a create-new-profile modal, and a link to the migration dashboard.
 *
 * All of them are gone. The rule now is that a SuperUser may only read and write the job
 * they are logged into — reads included — because this runs against a live production
 * database that PROD, STAGING and the legacy app all share.
 *
 * What replaced what:
 *   - scope selector           → nothing; the current job IS the scope
 *   - PUT profiles/job/{id}/…  → PUT profiles/current/form (no jobId to supply)
 *   - Copy Forms               → Job Clone, which already carries CoreRegformPlayer,
 *                                PlayerProfileMetadataJson and JsonOptions (with grad-year
 *                                shifting) forward to a new job
 *   - Profile Assignment card  → Configure → Job → Player Settings, which sets
 *                                Jobs.CoreRegformPlayer and does NOT touch the field set
 *   - migration dashboard      → retired; its endpoints are commented out
 *
 * The server endpoints behind every removed control are commented out in
 * ProfileMigrationController — that, not this component, is the enforcement.
 *
 * OPEN EXPOSURE — the Adult tab below is UNCHANGED and still type-scoped: every field edit
 * writes to every materialized job on that adult profile, with no confirm and no staging.
 * That breaks the same rule this lockdown enforces.
 *
 * It was DEFERRED from this change, not accepted. The instruction was to keep this pass to
 * the player side; no one has ruled that the adult fan-out is acceptable. The per-job
 * replacement already exists unwired on both sides (PUT profiles/current/adult-metadata,
 * AdultProfileMigrationService.updateCurrentJobAdultRole) if it is picked up.
 *
 * Note also that the banner below says "Editing THIS JOB ONLY" while the Player/Adult
 * segment sits above it — switching to Adult silently inverts the scope the banner claims.
 */
@Component({
    selector: 'app-profile-editor',
    standalone: true,
    imports: [CommonModule, FormsModule, RouterLink, OptionsPanelComponent, FieldSetEditorComponent, AdultProfileEditorPanelComponent],
    templateUrl: './profile-editor.component.html',
    styleUrl: './profile-editor.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ProfileEditorComponent implements OnInit {
    private readonly migrationService = inject(ProfileMigrationService);
    private readonly authService = inject(AuthService);
    private readonly toast = inject(ToastService);

    // Player / Adult segment. Player is this job's form; Adult remains type-scoped (see class note).
    mode = signal<'player' | 'adult'>('player');
    setMode(m: 'player' | 'adult'): void { this.mode.set(m); }

    // Allowed field catalogue handed to the shared field editor.
    readonly playerAllowedFields = ALLOWED_PROFILE_FIELDS;

    // Navigation
    jobPath = computed(() => this.authService.currentUser()?.jobPath || 'tsic');

    // State signals
    isLoading = signal(false);
    isSaving = signal(false);
    errorMessage = signal<string | null>(null);
    successMessage = signal<string | null>(null);

    // THIS job's stored player form — the only thing this screen edits.
    currentMetadata = signal<ProfileMetadata | null>(null);

    // Read-only context for the banner: the job's display name and its profile-type pointer.
    // Both come from the current job's own config; nothing enumerates other jobs any more.
    private readonly jobName = signal<string | null>(null);
    jobCoreRegformRaw = signal<string>('');
    currentJobName = computed(() => this.jobName() ?? this.jobPath());

    // Job Options (Jobs.JsonOptions) — current job only, same as the fields.
    activeTab = signal<'fields' | 'options'>('fields');
    showJobOptionsTab = computed(() => !!this.currentMetadata());

    // Test validation result (owned here; passed down to the field editor)
    testResult = signal<ValidationTestResult | null>(null);
    isTesting = signal(false);

    ngOnInit() {
        // The job's name + profile-type pointer, for display only.
        this.migrationService.getCurrentJobProfileConfig(
            (resp: CurrentJobProfileConfigResponse) => {
                this.jobName.set(resp.jobName || null);
                this.jobCoreRegformRaw.set(resp.coreRegform || '');
            },
            () => { /* silent; banner falls back to the jobPath slug */ }
        );

        // THIS job's actual stored form.
        this.loadThisJobForm();

        // Option sets feed the Job Options tab. Loaded directly now — this used to ride along
        // inside the profile-assignment callbacks, which no longer exist.
        this.migrationService.getCurrentJobOptionSets(_ => { }, _ => { });
    }

    private loadThisJobForm(): void {
        this.isLoading.set(true);
        this.migrationService.getCurrentJobPlayerForm(
            (metadata) => { this.currentMetadata.set(metadata); this.isLoading.set(false); },
            (error) => {
                this.errorMessage.set(`Failed to load this job's form: ${error?.error?.error || 'Unknown error'}`);
                this.currentMetadata.set(null);
                this.isLoading.set(false);
            }
        );
    }

    // ============================================================================
    // FIELD EDITING (delegated to <app-field-set-editor>)
    // ============================================================================

    /** Every field mutation emits the full new array, and saves immediately to THIS job. */
    onFieldsChange(newFields: ProfileMetadataField[]) {
        const metadata = this.currentMetadata();
        if (!metadata) return;
        const next = { ...metadata, fields: newFields };
        this.currentMetadata.set(next);
        this.save(next);
    }

    private save(metadata: ProfileMetadata) {
        const targetName = this.currentJobName();
        this.isSaving.set(true);
        this.errorMessage.set(null);
        this.migrationService.updateCurrentJobPlayerForm(
            metadata,
            () => {
                this.isSaving.set(false);
                this.toast.show(`Saved — ${targetName} only.`, 'success');
            },
            (error) => {
                this.isSaving.set(false);
                this.errorMessage.set(`Failed to save: ${error?.error?.error || error?.error?.message || 'Unknown error'}`);
            }
        );
    }

    // Field editor requests a validation test; run it and push the result back down.
    onValidationTest(e: { field: ProfileMetadataField; testValue: string }) {
        this.isTesting.set(true);
        this.testResult.set(null);
        this.migrationService.testValidation(
            e.field,
            e.testValue,
            (result) => { this.testResult.set(result); this.isTesting.set(false); },
            (error) => {
                this.testResult.set({
                    isValid: false,
                    messages: [`Test failed: ${error || 'Unknown error'}`],
                    testValue: e.testValue,
                    fieldName: e.field.name
                });
                this.isTesting.set(false);
            }
        );
    }
}
