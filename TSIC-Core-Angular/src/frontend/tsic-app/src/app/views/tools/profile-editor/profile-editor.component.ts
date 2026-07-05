import { ChangeDetectionStrategy, Component, signal, computed, inject, OnInit, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ProfileMigrationService } from '@infrastructure/services/profile-migration.service';
import { ProfileMetadata, ProfileMetadataField, ValidationTestResult, CurrentJobProfileConfigResponse } from '@infrastructure/view-models/profile-migration.models';
import { ToastService } from '@shared-ui/toast.service';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { OptionsPanelComponent } from './options-panel/options-panel.component';
import { FieldSetEditorComponent } from './field-set-editor/field-set-editor.component';
import { ALLOWED_PROFILE_FIELDS } from './allowed-fields';
import { AuthService } from '@infrastructure/services/auth.service';
import { AdultProfileEditorPanelComponent } from './adult-profile-editor-panel/adult-profile-editor-panel.component';
import { CopyFormsCardComponent } from './copy-forms-card/copy-forms-card.component';

@Component({
    selector: 'app-profile-editor',
    standalone: true,
    imports: [CommonModule, FormsModule, RouterLink, TsicDialogComponent, OptionsPanelComponent, FieldSetEditorComponent, AdultProfileEditorPanelComponent, CopyFormsCardComponent],
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

    // Available profile types (seeded dynamically)
    availableProfiles = signal<Array<{ type: string; display: string }>>([]);

    selectedProfileType = signal<string | null>(null);
    // The profile type currently employed by the active job (authoritative from server)
    activeJobProfileType = signal<string | null>(null);
    currentMetadata = signal<ProfileMetadata | null>(null);

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

    // Job Options (Jobs.JsonOptions)
    activeTab = signal<'fields' | 'options'>('fields');
    // Show Job Options only when editing the active job's profile
    showJobOptionsTab = computed(() => {
        const selected = this.selectedProfileType();
        const active = this.activeJobProfileType();
        if (!selected || !active) return false;
        return selected.toLowerCase() === active.toLowerCase();
    });

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

    // Confirm modal state (Bootstrap-styled, not browser confirm) — used for profile-switch discard.
    showConfirmModal = signal(false);
    confirmModalTitle = signal('Confirm Action');
    confirmModalMessage = signal('');
    confirmModalAction = signal<(() => void) | null>(null);

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
                // Fallback to summaries if none are known yet
                this.migrationService.loadProfileSummaries();
            }
        }, () => {
            // On error, fallback to summaries
            this.migrationService.loadProfileSummaries();
        });

        // Attempt to auto-load the current job's employed profile for editing
        this.migrationService.getCurrentJobProfileMetadata(
            (resp) => {
                const displayName = this.formatProfileDisplayType(resp.profileType);
                // Ensure list contains the current profile
                this.availableProfiles.update(list => {
                    const exists = list.some(p => p.type === resp.profileType);
                    return exists ? list : [{ type: resp.profileType, display: displayName }, ...list];
                });

                // Select and set metadata
                this.selectedProfileType.set(resp.profileType);
                this.activeJobProfileType.set(resp.profileType);
                this.currentMetadata.set(resp.metadata);

                // Load current job option sets in background (service mirrors via signals)
                this.migrationService.getCurrentJobOptionSets(
                    _ => { },
                    _ => { }
                );

                // Initialize last-selected tracker for the profile selector
                this.lastSelectedProfileType.set(resp.profileType);
            },
            (_err) => {
                // If not available, leave selector and allow manual choice
            }
        );

        // Load the current job's CoreRegformPlayer parts for the left panel
        this.migrationService.getCurrentJobProfileConfig(
            (resp: CurrentJobProfileConfigResponse) => {
                this.jobProfileType.set(resp.profileType || '');
                this.jobTeamConstraint.set(resp.teamConstraint || '');
                this.jobCoreRegformRaw.set(resp.coreRegform || '');
                this.lastAppliedTeamConstraint.set(resp.teamConstraint || '');
                // Also ensure active job type is synced
                if (resp.profileType) {
                    this.activeJobProfileType.set(resp.profileType);
                }
            },
            () => { /* silent; panel will still render with defaults */ }
        );
    }

    // Warn on browser/tab close if there are unapplied changes
    @HostListener('window:beforeunload', ['$event'])
    onBeforeUnload(event: BeforeUnloadEvent) {
        if (this.jobConfigDirty()) {
            event.preventDefault();
            // Use any-cast to avoid TS deprecation diagnostic while preserving cross-browser behavior
            (event as any).returnValue = '';
        }
    }

    // Fallback derivation of availableProfiles from summaries (pure derivation without effect)
    private readonly derivedProfiles = computed(() => {
        if (this.availableProfiles().length > 0) return this.availableProfiles();
        const summaries = this.migrationService.profileSummaries();
        if (!summaries || summaries.length === 0) return [];
        const all = summaries
            .map(s => s.profileType)
            .filter(t => t && (t.startsWith('PP') || t.startsWith('CAC')))
            .sort((a, b) => a.localeCompare(b));
        return all.map(t => ({ type: t, display: this.formatProfileDisplayType(t) }));
    });
    // Exposed getter to use in template if needed
    get effectiveAvailableProfiles() { return this.derivedProfiles(); }

    private formatProfileDisplayType(type: string): string {
        return type
            .replaceAll('_', ' ')
            // Insert space before numbers only when preceded by a lowercase letter: player3 -> player 3; PP47 stays PP47
            .replaceAll(/([a-z])(\d)/g, '$1 $2')
            // Insert space between lower->upper transitions: playerProfile -> player Profile
            .replaceAll(/([a-z])([A-Z])/g, '$1 $2')
            .replaceAll(/\s+/g, ' ')
            .trim();
    }

    // Helper to normalize activeTab without side-effecting via an effect
    get effectiveActiveTab(): 'fields' | 'options' {
        const tab = this.activeTab();
        return (tab === 'options' && !this.showJobOptionsTab()) ? 'fields' : tab;
    }

    loadProfile(profileType: string) {
        // Handle CREATE NEW special case
        if (profileType === 'CREATE_NEW') {
            this.openCreateModal();
            return;
        }

        this.isLoading.set(true);
        this.errorMessage.set(null);
        this.successMessage.set(null);
        this.selectedProfileType.set(profileType);

        this.migrationService.getProfileMetadata(
            profileType,
            (metadata) => {
                this.currentMetadata.set(metadata);
                this.isLoading.set(false);
            },
            (error) => {
                this.errorMessage.set(`Failed to load profile: ${error || 'Unknown error'}`);
                this.isLoading.set(false);
                this.currentMetadata.set(null);
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
        // Reset selected profile since they're creating new
        this.selectedProfileType.set(null);
    }

    closeCreateModal() {
        this.isCreateModalOpen.set(false);
        this.selectedCloneSource.set(null);
        this.newProfileName.set('');
    }

    onCloneSourceSelected(sourceProfile: string) {
        this.selectedCloneSource.set(sourceProfile);
        // Auto-generate the new profile name preview
        this.generateNewProfileName(sourceProfile);
    }

    generateNewProfileName(sourceProfile: string) {
        // Ask the server for the authoritative next profile type for this family
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
                    // Auto-dismiss success after a brief delay (polish)
                    setTimeout(() => this.successMessage.set(null), 4000);

                    // Add to available profiles list
                    const displayName = this.formatProfileDisplayType(result.newProfileType);
                    this.availableProfiles.update(profiles => [
                        ...profiles,
                        { type: result.newProfileType, display: displayName }
                    ]);

                    // Close modal and load the new profile
                    this.closeCreateModal();
                    this.loadProfile(result.newProfileType);
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
    // FIELD EDITING (delegated to <app-field-set-editor>)
    // ============================================================================

    // Every field mutation (reorder/add/edit/remove) emits the full new array; persist it.
    onFieldsChange(newFields: ProfileMetadataField[]) {
        const metadata = this.currentMetadata();
        if (!metadata) return;
        this.saveMetadata({ ...metadata, fields: newFields });
    }

    // Field editor requests a validation test; run it and push the result back down.
    onValidationTest(e: { field: ProfileMetadataField; testValue: string }) {
        this.isTesting.set(true);
        this.testResult.set(null);
        this.migrationService.testValidation(
            e.field,
            e.testValue,
            (result) => {
                this.testResult.set(result);
                this.isTesting.set(false);
            },
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

    saveMetadata(metadata: ProfileMetadata) {
        const profileType = this.selectedProfileType();
        if (!profileType) return;

        this.isSaving.set(true);
        this.errorMessage.set(null);
        this.successMessage.set(null);

        this.migrationService.updateProfileMetadata(
            profileType,
            metadata,
            (result) => {
                this.currentMetadata.set(metadata);
                this.successMessage.set(`Profile updated successfully. ${result.jobsAffected} job(s) affected.`);
                this.isSaving.set(false);
            },
            (error) => {
                this.errorMessage.set(`Failed to save profile: ${error || 'Unknown error'}`);
                this.isSaving.set(false);
            }
        );
    }

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
                // Update active job profile type and selected profile to stay in sync
                this.activeJobProfileType.set(resp.profileType);
                this.selectedProfileType.set(resp.profileType);
                if (resp.metadata) {
                    this.currentMetadata.set(resp.metadata);
                }
                this.jobCoreRegformRaw.set(resp.coreRegform || '');
                // Commit last-applied values so dirty state clears
                this.lastAppliedTeamConstraint.set(team);
                this.lastSelectedProfileType.set(resp.profileType);
                // Refresh options for the active job via service (mirrored by OptionsPanel)
                this.migrationService.getCurrentJobOptionSets(
                    _ => { },
                    _ => { }
                );
                // Positive feedback
                this.successMessage.set('Job profile configuration updated.');
                setTimeout(() => this.successMessage.set(null), 3000);
                this.isSaving.set(false);
            },
            (err) => {
                this.isSaving.set(false);
                this.errorMessage.set(err?.error?.message || 'Failed to update job profile configuration');
            }
        );
    }

    onResetJobProfileConfig() {
        // Revert UI values back to last-applied
        this.jobProfileType.set(this.activeJobProfileType() || '');
        this.jobTeamConstraint.set(this.lastAppliedTeamConstraint() || '');
    }

    onSelectedProfileTypeChange(nextType: string | null) {
        // If there are unapplied changes to job config, confirm before switching profile
        if (this.jobConfigDirty()) {
            const previous = this.lastSelectedProfileType();
            this.openConfirm(
                'Discard Unapplied Changes?',
                'You have changes to Team Constraint or Allow Pay In Full that are not applied. Switch profile and discard these changes?',
                () => {
                    this.loadProfile(nextType || '');
                    this.lastSelectedProfileType.set(nextType || null);
                }
            );
            // Revert the selector immediately; if user confirms we'll set it again in action
            this.selectedProfileType.set(previous || null);
            return;
        }
        this.loadProfile(nextType || '');
        this.lastSelectedProfileType.set(nextType || null);
    }

    // ============================================================================
    // Confirm modal helpers (profile-switch discard)
    // ============================================================================
    openConfirm(title: string, message: string, action: () => void) {
        this.confirmModalTitle.set(title);
        this.confirmModalMessage.set(message);
        this.confirmModalAction.set(() => action());
        this.showConfirmModal.set(true);
    }

    confirmAction() {
        const action = this.confirmModalAction();
        if (action) {
            action();
        }
        this.closeConfirm();
    }

    closeConfirm() {
        this.showConfirmModal.set(false);
        this.confirmModalTitle.set('Confirm Action');
        this.confirmModalMessage.set('');
        this.confirmModalAction.set(null);
    }

}
