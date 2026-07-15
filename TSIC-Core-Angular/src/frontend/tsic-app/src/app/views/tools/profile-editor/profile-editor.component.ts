import { ChangeDetectionStrategy, Component, signal, computed, inject, OnInit, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ProfileMigrationService } from '@infrastructure/services/profile-migration.service';
import { ProfileMetadata, ProfileMetadataField, ValidationTestResult, CurrentJobProfileConfigResponse } from '@infrastructure/view-models/profile-migration.models';
import type { EditableJobDto, AffectedJobsResult } from '@core/api';
import { ToastService } from '@shared-ui/toast.service';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { OptionsPanelComponent } from './options-panel/options-panel.component';
import { FieldSetEditorComponent } from './field-set-editor/field-set-editor.component';
import { JobPickerComponent } from './job-picker/job-picker.component';
import { ALLOWED_PROFILE_FIELDS } from './allowed-fields';
import { AuthService } from '@infrastructure/services/auth.service';
import { AdultProfileEditorPanelComponent } from './adult-profile-editor-panel/adult-profile-editor-panel.component';
import { CopyFormsCardComponent } from './copy-forms-card/copy-forms-card.component';

/** Edit scope — the safe default is a single job; template edits every job of a type. */
export type EditScope = 'thisJob' | 'otherJob' | 'template';

@Component({
    selector: 'app-profile-editor',
    standalone: true,
    imports: [CommonModule, FormsModule, RouterLink, TsicDialogComponent, OptionsPanelComponent, FieldSetEditorComponent, JobPickerComponent, AdultProfileEditorPanelComponent, CopyFormsCardComponent],
    templateUrl: './profile-editor.component.html',
    styleUrl: './profile-editor.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ProfileEditorComponent implements OnInit {
    private readonly migrationService = inject(ProfileMigrationService);
    private readonly authService = inject(AuthService);
    private readonly toast = inject(ToastService);

    // Player / Adult segment. Player is the original editor; Adult is the type-scoped mirror.
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

    // Available profile types (seeded dynamically) — used by the template-scope picker & clone modal.
    availableProfiles = signal<Array<{ type: string; display: string }>>([]);

    // The metadata currently being edited (whatever the active scope points at).
    currentMetadata = signal<ProfileMetadata | null>(null);

    // ============ EDIT SCOPE ============
    editScope = signal<EditScope>('thisJob');

    // Current job (resolved from JWT) — id lets us do per-job writes and exclude it from pickers.
    currentJobId = signal<string | null>(null);
    activeJobProfileType = signal<string | null>(null);

    // Editable jobs (for the "A specific job" picker) + the picked job.
    editableJobs = signal<EditableJobDto[]>([]);
    otherJob = signal<EditableJobDto | null>(null);

    // Template scope: which profile type's shared definition is being edited.
    selectedProfileType = signal<string | null>(null);
    // Template edits are deferred (no auto-save) until an explicit "Apply to all jobs".
    templateDirty = signal(false);
    affectedJobs = signal<AffectedJobsResult | null>(null);

    // Friendly name of the current job (looked up from the editable list; falls back to jobPath).
    currentJobName = computed(() => {
        const id = this.currentJobId();
        const found = id ? this.editableJobs().find(j => j.jobId === id)?.jobName : null;
        return found ?? this.jobPath();
    });

    // The named target of the active scope, for the banner and toasts.
    scopeTargetLabel = computed(() => {
        switch (this.editScope()) {
            case 'thisJob': return this.currentJobName();
            case 'otherJob': return this.otherJob()?.jobName ?? '(no job chosen)';
            case 'template': return this.selectedProfileType() ? `${this.selectedProfileType()} template` : '(no template chosen)';
        }
    });

    isTemplateScope = computed(() => this.editScope() === 'template');
    isPerJobScope = computed(() => this.editScope() !== 'template');

    // Display label for the currently selected profile
    selectedProfileDisplay = computed(() => {
        const type = this.selectedProfileType();
        if (!type) return '';
        const found = this.availableProfiles().find(p => p.type === type)?.display;
        return found ?? this.formatProfileDisplayType(type);
    });

    // Create new profile modal state
    isCreateModalOpen = signal(false);
    selectedCloneSource = signal<string | null>(null);
    newProfileName = signal<string>('');
    isCloning = signal(false);

    // Test validation result (owned here; passed down to the field editor)
    testResult = signal<ValidationTestResult | null>(null);
    isTesting = signal(false);

    fieldCount = computed(() => this.currentMetadata()?.fields?.length ?? 0);

    // Job Options (Jobs.JsonOptions) — current-job only, so only meaningful in the This-job scope.
    activeTab = signal<'fields' | 'options'>('fields');
    showJobOptionsTab = computed(() => this.editScope() === 'thisJob' && !!this.currentMetadata());

    // ========= This Job's Player Profile (CoreRegformPlayer parts) =========
    jobProfileType = signal<string>('');
    jobTeamConstraint = signal<string>(''); // '', 'BYGRADYEAR', 'BYAGEGROUP', 'BYAGERANGE', 'BYCLUBNAME'
    readonly teamConstraintOptions = [
        { value: '', label: 'None' },
        { value: 'BYGRADYEAR', label: 'By Graduation Year' },
        { value: 'BYAGEGROUP', label: 'By Age Group' },
        { value: 'BYAGERANGE', label: 'By Age Range' },
        { value: 'BYCLUBNAME', label: 'By Club Name' }
    ];
    // Raw CoreRegform string (e.g., PP53|PerGradYear)
    jobCoreRegformRaw = signal<string>('');

    // Track last-applied job config to detect unsaved changes
    private readonly lastAppliedTeamConstraint = signal<string>('');
    private readonly lastSelectedProfileType = signal<string | null>(null);

    jobConfigDirty = computed(() => {
        const appliedType = (this.activeJobProfileType() || '').trim();
        const uiType = (this.jobProfileType() || '').trim();
        const appliedTeam = (this.lastAppliedTeamConstraint() || '').trim();
        const uiTeam = (this.jobTeamConstraint() || '').trim();
        return (appliedType.toLowerCase() !== uiType.toLowerCase())
            || (appliedTeam.toLowerCase() !== uiTeam.toLowerCase());
    });

    // Confirm modal state (Bootstrap-styled, not browser confirm) — used for scope-switch discard.
    showConfirmModal = signal(false);
    confirmModalTitle = signal('Confirm Action');
    confirmModalMessage = signal('');
    confirmModalAction = signal<(() => void) | null>(null);

    // Template-apply confirmation (typed profile-type gate).
    showTemplateConfirm = signal(false);
    templateConfirmText = signal('');
    canApplyTemplate = computed(() => {
        const t = (this.selectedProfileType() || '').trim().toUpperCase();
        return t.length > 0 && this.templateConfirmText().trim().toUpperCase() === t;
    });

    ngOnInit() {
        // Prefer server-known types based on Jobs.PlayerProfileMetadataJson (prod-safe)
        this.migrationService.getKnownProfileTypes((types) => {
            const mapped = types
                .filter(t => t && (t.startsWith('PP') || t.startsWith('CAC')))
                .sort((a, b) => a.localeCompare(b))
                .map(t => ({ type: t, display: this.formatProfileDisplayType(t) }));
            if (mapped.length > 0) {
                this.availableProfiles.set(mapped);
            } else {
                this.migrationService.loadProfileSummaries();
            }
        }, () => {
            this.migrationService.loadProfileSummaries();
        });

        // Editable jobs power the "A specific job" picker and the current-job name lookup.
        this.migrationService.listEditableJobs(
            (jobs) => this.editableJobs.set(jobs),
            () => { /* silent; picker will be empty */ }
        );

        // Seed the profile list + active type from the current job's employed profile. NOTE: this
        // returns the profile TYPE's representative metadata — not necessarily this job's actual form —
        // so it is used only for seeding, never as the edited metadata.
        this.migrationService.getCurrentJobProfileMetadata(
            (resp) => {
                const displayName = this.formatProfileDisplayType(resp.profileType);
                this.availableProfiles.update(list => {
                    const exists = list.some(p => p.type === resp.profileType);
                    return exists ? list : [{ type: resp.profileType, display: displayName }, ...list];
                });
                this.activeJobProfileType.set(resp.profileType);
                this.migrationService.getCurrentJobOptionSets(_ => { }, _ => { });
                this.lastSelectedProfileType.set(resp.profileType);
            },
            (_err) => { /* leave editor empty; user can still pick another scope */ }
        );

        // Current job's CoreRegformPlayer parts + JobId (for per-job writes). Once we know the JobId,
        // load THIS job's ACTUAL form (not the type canonical) for the default This-job scope.
        this.migrationService.getCurrentJobProfileConfig(
            (resp: CurrentJobProfileConfigResponse) => {
                this.jobProfileType.set(resp.profileType || '');
                this.jobTeamConstraint.set(resp.teamConstraint || '');
                this.jobCoreRegformRaw.set(resp.coreRegform || '');
                this.currentJobId.set(resp.jobId || null);
                this.lastAppliedTeamConstraint.set(resp.teamConstraint || '');
                if (resp.profileType) {
                    this.activeJobProfileType.set(resp.profileType);
                }
                if (this.editScope() === 'thisJob' && resp.jobId) {
                    this.loadJobForm(resp.jobId);
                }
            },
            () => { /* silent; panel will still render with defaults */ }
        );
    }

    // Warn on browser/tab close if there are unapplied changes
    @HostListener('window:beforeunload', ['$event'])
    onBeforeUnload(event: BeforeUnloadEvent) {
        if (this.jobConfigDirty() || this.templateDirty()) {
            event.preventDefault();
            (event as any).returnValue = '';
        }
    }

    private formatProfileDisplayType(type: string): string {
        return type
            .replaceAll('_', ' ')
            .replaceAll(/([a-z])(\d)/g, '$1 $2')
            .replaceAll(/([a-z])([A-Z])/g, '$1 $2')
            .replaceAll(/\s+/g, ' ')
            .trim();
    }

    // ============================================================================
    // SCOPE SWITCHING
    // ============================================================================

    setScope(scope: EditScope): void {
        if (scope === this.editScope()) return;

        const doSwitch = () => {
            this.templateDirty.set(false);
            this.templateConfirmText.set('');
            this.errorMessage.set(null);
            this.successMessage.set(null);
            this.activeTab.set('fields');
            this.editScope.set(scope);

            switch (scope) {
                case 'thisJob':
                    this.loadThisJobForm();
                    break;
                case 'otherJob':
                    // Wait for a job to be picked; keep any existing pick's metadata.
                    if (this.otherJob()) {
                        this.loadJobForm(this.otherJob()!.jobId);
                    } else {
                        this.currentMetadata.set(null);
                    }
                    break;
                case 'template':
                    if (this.selectedProfileType()) {
                        this.loadTemplate(this.selectedProfileType()!);
                    } else {
                        this.currentMetadata.set(null);
                    }
                    break;
            }
        };

        // Guard against silently discarding staged template edits.
        if (this.templateDirty()) {
            this.openConfirm(
                'Discard template changes?',
                `You have unsaved changes to the ${this.selectedProfileType()} template that have not been applied to any job. Switch scope and discard them?`,
                doSwitch
            );
            return;
        }
        doSwitch();
    }

    private loadThisJobForm(): void {
        const id = this.currentJobId();
        if (!id) return; // metadata may already be loaded from getCurrentJobProfileMetadata
        this.loadJobForm(id);
    }

    private loadJobForm(jobId: string): void {
        this.isLoading.set(true);
        this.migrationService.getJobPlayerForm(
            jobId,
            (metadata) => { this.currentMetadata.set(metadata); this.isLoading.set(false); },
            (error) => {
                this.errorMessage.set(`Failed to load job form: ${error?.error?.error || 'Unknown error'}`);
                this.currentMetadata.set(null);
                this.isLoading.set(false);
            }
        );
    }

    onOtherJobSelected(job: EditableJobDto | null): void {
        this.otherJob.set(job);
        if (job) {
            this.loadJobForm(job.jobId);
        } else {
            this.currentMetadata.set(null);
        }
    }

    onTemplateTypeChange(nextType: string | null): void {
        if (nextType === 'CREATE_NEW') {
            this.openCreateModal();
            return;
        }
        this.selectedProfileType.set(nextType);
        this.templateDirty.set(false);
        this.affectedJobs.set(null);
        if (nextType) {
            this.loadTemplate(nextType);
            this.loadAffectedJobs(nextType);
        } else {
            this.currentMetadata.set(null);
        }
    }

    private loadTemplate(profileType: string): void {
        this.isLoading.set(true);
        this.migrationService.getProfileMetadata(
            profileType,
            (metadata) => { this.currentMetadata.set(metadata); this.isLoading.set(false); },
            (error) => {
                this.errorMessage.set(`Failed to load template: ${error || 'Unknown error'}`);
                this.currentMetadata.set(null);
                this.isLoading.set(false);
            }
        );
    }

    private loadAffectedJobs(profileType: string): void {
        this.migrationService.getAffectedJobs(
            profileType,
            (result) => this.affectedJobs.set(result),
            () => this.affectedJobs.set(null)
        );
    }

    // ============================================================================
    // FIELD EDITING (delegated to <app-field-set-editor>)
    // ============================================================================

    // Every field mutation emits the full new array. Per-job scopes save immediately (safe, one job);
    // template scope stages the change and defers to an explicit, confirmed "Apply to all".
    onFieldsChange(newFields: ProfileMetadataField[]) {
        const metadata = this.currentMetadata();
        if (!metadata) return;
        const next = { ...metadata, fields: newFields };
        this.currentMetadata.set(next);

        if (this.isPerJobScope()) {
            this.savePerJob(next);
        } else {
            this.templateDirty.set(true);
        }
    }

    private savePerJob(metadata: ProfileMetadata) {
        const jobId = this.editScope() === 'thisJob' ? this.currentJobId() : this.otherJob()?.jobId;
        if (!jobId) {
            this.errorMessage.set('No target job resolved — cannot save.');
            return;
        }
        const targetName = this.scopeTargetLabel();
        this.isSaving.set(true);
        this.errorMessage.set(null);
        this.migrationService.updateJobPlayerForm(
            jobId,
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

    // ---- Template apply (deliberate, guarded fan-out) ----

    openTemplateApply(): void {
        const type = this.selectedProfileType();
        if (!type) return;
        this.templateConfirmText.set('');
        if (!this.affectedJobs()) {
            this.loadAffectedJobs(type);
        }
        this.showTemplateConfirm.set(true);
    }

    cancelTemplateApply(): void {
        this.showTemplateConfirm.set(false);
        this.templateConfirmText.set('');
    }

    confirmTemplateApply(): void {
        const type = this.selectedProfileType();
        const metadata = this.currentMetadata();
        if (!type || !metadata || !this.canApplyTemplate()) return;

        this.isSaving.set(true);
        this.errorMessage.set(null);
        this.migrationService.updateProfileMetadata(
            type,
            metadata,
            (result) => {
                this.isSaving.set(false);
                this.templateDirty.set(false);
                this.showTemplateConfirm.set(false);
                this.templateConfirmText.set('');
                this.loadAffectedJobs(type);
                this.toast.show(`Template ${type} updated — ${result.jobsAffected} job(s) rewritten.`, 'success');
            },
            (error) => {
                this.isSaving.set(false);
                this.errorMessage.set(`Failed to apply template: ${error?.error?.message || 'Unknown error'}`);
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

    // ============================================================================
    // CREATE NEW PROFILE
    // ============================================================================

    openCreateModal() {
        this.selectedCloneSource.set(null);
        this.newProfileName.set('');
        this.isCreateModalOpen.set(true);
        this.selectedProfileType.set(null);
    }

    closeCreateModal() {
        this.isCreateModalOpen.set(false);
        this.selectedCloneSource.set(null);
        this.newProfileName.set('');
    }

    onCloneSourceSelected(sourceProfile: string) {
        this.selectedCloneSource.set(sourceProfile);
        this.generateNewProfileName(sourceProfile);
    }

    generateNewProfileName(sourceProfile: string) {
        this.migrationService.getNextProfileType(
            sourceProfile,
            (resp) => this.newProfileName.set(resp.newProfileType),
            () => this.newProfileName.set('')
        );
    }

    createNewProfile() {
        const sourceProfile = this.selectedCloneSource();
        if (!sourceProfile) {
            this.errorMessage.set('Please select a profile to clone from');
            return;
        }

        this.isCloning.set(true);
        this.errorMessage.set(null);

        this.migrationService.cloneProfile(
            sourceProfile,
            (result) => {
                if (result.success) {
                    this.successMessage.set(`Successfully created new profile: ${result.newProfileType}`);
                    setTimeout(() => this.successMessage.set(null), 4000);

                    const displayName = this.formatProfileDisplayType(result.newProfileType);
                    this.availableProfiles.update(profiles => [
                        ...profiles,
                        { type: result.newProfileType, display: displayName }
                    ]);

                    this.closeCreateModal();
                    // Cloning targets the current job; land the user in template scope on the new type.
                    this.editScope.set('template');
                    this.onTemplateTypeChange(result.newProfileType);
                } else {
                    this.errorMessage.set(result.errorMessage || 'Failed to create profile');
                }
                this.isCloning.set(false);
            },
            (error) => {
                this.errorMessage.set(`Failed to create profile: ${error || 'Unknown error'}`);
                this.isCloning.set(false);
            }
        );
    }

    // ============================================================================
    // THIS JOB'S PROFILE ASSIGNMENT (CoreRegformPlayer) — current job only
    // ============================================================================

    applyJobProfileConfig() {
        const newType = (this.jobProfileType() || '').trim();
        const team = (this.jobTeamConstraint() || '').trim();
        if (!newType) return;

        this.isSaving.set(true);
        this.errorMessage.set(null);
        this.successMessage.set(null);

        this.migrationService.updateCurrentJobProfileConfig(
            newType,
            team,
            (resp) => {
                this.activeJobProfileType.set(resp.profileType);
                this.jobCoreRegformRaw.set(resp.coreRegform || '');
                this.lastAppliedTeamConstraint.set(team);
                this.lastSelectedProfileType.set(resp.profileType);
                // Re-stamped this job's form from the new type — reload the actual stored form.
                if (this.editScope() === 'thisJob') {
                    this.loadThisJobForm();
                }
                this.migrationService.getCurrentJobOptionSets(_ => { }, _ => { });
                this.toast.show(`Profile assignment updated — ${this.currentJobName()}.`, 'success');
                this.isSaving.set(false);
            },
            (err) => {
                this.isSaving.set(false);
                this.errorMessage.set(err?.error?.message || 'Failed to update job profile configuration');
            }
        );
    }

    onResetJobProfileConfig() {
        this.jobProfileType.set(this.activeJobProfileType() || '');
        this.jobTeamConstraint.set(this.lastAppliedTeamConstraint() || '');
    }

    // ============================================================================
    // Confirm modal helpers (scope-switch discard)
    // ============================================================================
    openConfirm(title: string, message: string, action: () => void) {
        this.confirmModalTitle.set(title);
        this.confirmModalMessage.set(message);
        this.confirmModalAction.set(() => action());
        this.showConfirmModal.set(true);
    }

    confirmAction() {
        const action = this.confirmModalAction();
        if (action) action();
        this.closeConfirm();
    }

    closeConfirm() {
        this.showConfirmModal.set(false);
        this.confirmModalTitle.set('Confirm Action');
        this.confirmModalMessage.set('');
        this.confirmModalAction.set(null);
    }
}
