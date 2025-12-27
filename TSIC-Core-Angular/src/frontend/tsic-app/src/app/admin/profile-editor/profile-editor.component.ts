import { Component, signal, computed, inject, OnInit, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { DragDropModule, CdkDragDrop, moveItemInArray } from '@angular/cdk/drag-drop';
import { ProfileMigrationService } from '@infrastructure/services/profile-migration.service';
import { ProfileMetadata, ProfileMetadataField, ValidationTestResult, CurrentJobProfileConfigResponse } from '@infrastructure/view-models/profile-migration.models';
import { ToastService } from '../../shared/toast.service';
import { TsicDialogComponent } from '../../shared/components/tsic-dialog/tsic-dialog.component';
import { OptionsPanelComponent } from './options-panel/options-panel.component';
import { ALLOWED_PROFILE_FIELDS, AllowedField } from './allowed-fields';
import { AuthService } from '@infrastructure/services/auth.service';

type FieldType = 'TEXT' | 'TEXTAREA' | 'EMAIL' | 'NUMBER' | 'TEL' | 'DATE' | 'DATETIME' | 'CHECKBOX' | 'SELECT' | 'RADIO' | 'HIDDEN';

@Component({
    selector: 'app-profile-editor',
    standalone: true,
    imports: [CommonModule, FormsModule, RouterLink, DragDropModule, TsicDialogComponent, OptionsPanelComponent],
    templateUrl: './profile-editor.component.html',
    styleUrl: './profile-editor.component.scss'
})
export class ProfileEditorComponent implements OnInit {
    private readonly migrationService = inject(ProfileMigrationService);
    private readonly authService = inject(AuthService);
    private readonly toast = inject(ToastService);

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

    // Edit modal state
    isEditModalOpen = signal(false);
    editingField = signal<ProfileMetadataField | null>(null);
    editingFieldIndex = signal<number>(-1);
    // Reorder within registrant-visible fields
    orderOptions = signal<number[]>([]); // 0-based indices for registrant-visible ordering
    selectedOrderIndex = signal<number>(-1);

    // Test validation modal state
    isTestModalOpen = signal(false);
    testFieldName = signal<string>('');
    testValue = signal<string>('');
    testResult = signal<ValidationTestResult | null>(null);
    isTesting = signal(false);

    // Field type options
    fieldTypeOptions: FieldType[] = ['TEXT', 'TEXTAREA', 'EMAIL', 'NUMBER', 'TEL', 'DATE', 'DATETIME', 'CHECKBOX', 'SELECT', 'RADIO'];

    // Computed values
    hasUnsavedChanges = computed(() => {
        // Simple flag - could be enhanced with change tracking
        return false;
    });

    fieldCount = computed(() => this.currentMetadata()?.fields?.length ?? 0);

    // Computed groups for field lists
    registrantFields = computed(() => {
        const fields = this.currentMetadata()?.fields ?? [];
        return fields
            .filter(f => f.visibility === 'public')
            .slice()
            .sort((a, b) => (a.order ?? 0) - (b.order ?? 0));
    });
    adminOnlyFields = computed(() => {
        const fields = this.currentMetadata()?.fields ?? [];
        return fields
            .filter(f => f.visibility === 'adminOnly')
            .slice()
            .sort((a, b) => (a.order ?? 0) - (b.order ?? 0));
    });
    hiddenFields = computed(() => {
        const fields = this.currentMetadata()?.fields ?? [];
        return fields
            .filter(f => f.visibility === 'hidden')
            .slice()
            .sort((a, b) => (a.order ?? 0) - (b.order ?? 0));
    });

    // Job Options (Jobs.JsonOptions)
    activeTab = signal<'fields' | 'options'>('fields');
    // Show Job Options only when editing the active job's profile
    showJobOptionsTab = computed(() => {
        const selected = this.selectedProfileType();
        const active = this.activeJobProfileType();
        if (!selected || !active) return false;
        return selected.toLowerCase() === active.toLowerCase();
    });
    // Sources (removed UI) – formerly read-only from Registrations

    // ========= This Job's Player Profile (CoreRegformPlayer parts) =========
    jobProfileType = signal<string>('');
    jobTeamConstraint = signal<string>(''); // '', 'BYGRADYEAR', 'BYAGEGROUP', 'BYAGERANGE', 'BYCLUBNAME'
    // Allow Pay In Full removed from admin editor
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

    // Confirm modal state (Bootstrap-styled, not browser confirm)
    showConfirmModal = signal(false);
    confirmModalTitle = signal('Confirm Action');
    confirmModalMessage = signal('');
    confirmModalAction = signal<(() => void) | null>(null);

    // Add Field modal state
    isAddFieldModalOpen = signal(false);
    selectedNewFieldName = signal<string | null>(null);
    addFieldPlacement = signal<'public' | 'adminOnly' | 'hidden'>('public');
    availableNewFields = computed(() => {
        const used = new Set((this.currentMetadata()?.fields ?? []).map(f => f.name.toLowerCase()));
        return ALLOWED_PROFILE_FIELDS.filter(f => !used.has(f.name.toLowerCase()));
    });
    selectedNewField = computed<AllowedField | null>(() => {
        const name = this.selectedNewFieldName();
        if (!name) return null;
        return ALLOWED_PROFILE_FIELDS.find(f => f.name === name) ?? null;
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
                // Allow PIF removed; no state to sync
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

    // copySource removed – Available Sources UI removed

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
    // FIELD EDITING
    // ============================================================================

    openEditModal(field: ProfileMetadataField, index: number) {
        this.editingField.set({ ...field }); // Clone to avoid direct mutation
        this.editingFieldIndex.set(index);
        this.isEditModalOpen.set(true);

        // Prepare order options limited to registrant-visible (public) fields
        const metadata = this.currentMetadata();
        if (metadata) {
            const registrant = metadata.fields
                .filter(f => f.visibility === 'public')
                .sort((a, b) => (a.order ?? 0) - (b.order ?? 0));
            this.orderOptions.set(registrant.map((_, i) => i));

            const pos = registrant.findIndex(f => f.name === field.name);
            this.selectedOrderIndex.set(pos >= 0 ? pos : -1);
        } else {
            this.orderOptions.set([]);
            this.selectedOrderIndex.set(-1);
        }
    }

    // Helper to open editing using the field's name (for grouped lists)
    openEditByField(field: ProfileMetadataField) {
        const metadata = this.currentMetadata();
        if (!metadata) return;
        const idx = metadata.fields.findIndex(f => f.name === field.name);
        if (idx >= 0) this.openEditModal(metadata.fields[idx], idx);
    }

    closeEditModal() {
        this.isEditModalOpen.set(false);
        this.editingField.set(null);
        this.editingFieldIndex.set(-1);
    }

    // Drag-and-drop within registrant-visible fields only
    onRegistrantDrop(event: CdkDragDrop<ProfileMetadataField[]>) {
        const metadata = this.currentMetadata();
        if (!metadata) return;

        const registrant = this.registrantFields().slice();
        moveItemInArray(registrant, event.previousIndex, event.currentIndex);

        // Compute min/max order for registrant region from the original list
        const orders = metadata.fields.filter(f => f.visibility === 'public').map(f => f.order ?? 0);
        const minOrder = orders.length ? Math.min(...orders) : 0;
        // Reassign consecutive orders within registrant range
        const updatedFields = metadata.fields.slice();
        let i = 0;
        for (const f of registrant) {
            const idx = updatedFields.findIndex(x => x.name === f.name);
            if (idx >= 0) {
                updatedFields[idx] = { ...updatedFields[idx], order: minOrder + i };
            }
            i++;
        }

        const updatedMetadata: ProfileMetadata = { ...metadata, fields: updatedFields };
        this.saveMetadata(updatedMetadata);
    }

    saveFieldEdit() {
        const field = this.editingField();
        const index = this.editingFieldIndex();
        const metadata = this.currentMetadata();

        if (!field || index < 0 || !metadata) return;

        // Start from a copy of all fields
        const updatedFields = [...metadata.fields];

        // Reorder within registrant-visible (public) region only if applicable
        if (field.visibility === 'public' && this.selectedOrderIndex() >= 0) {
            this.reorderRegistrantForEdit(updatedFields, field.name, this.selectedOrderIndex());
        }

        // Apply other edited field properties back to the array entry
        updatedFields[index] = { ...updatedFields[index], ...field };

        const updatedMetadata: ProfileMetadata = { ...metadata, fields: updatedFields };

        this.saveMetadata(updatedMetadata);
        this.closeEditModal();
    }

    // Extracted helper: reorder registrant-visible fields for saveFieldEdit
    private reorderRegistrantForEdit(updatedFields: ProfileMetadataField[], fieldName: string, targetIdx: number) {
        // Build registrant slice and compute target position
        const registrant = updatedFields
            .filter(f => f.visibility === 'public')
            .sort((a, b) => (a.order ?? 0) - (b.order ?? 0));

        const currentIdxInRegistrant = registrant.findIndex(f => f.name === fieldName);
        if (currentIdxInRegistrant < 0 || targetIdx < 0 || targetIdx >= registrant.length) {
            return;
        }

        // Remove and insert to new position
        const [moved] = registrant.splice(currentIdxInRegistrant, 1);
        registrant.splice(targetIdx, 0, moved);

        // Compute the original min/max order span for registrant to keep them within region
        const orders = updatedFields.filter(f => f.visibility === 'public').map(f => f.order ?? 0);
        const minOrder = Math.min(...orders);
        const maxOrder = Math.max(...orders);

        // Assign new consecutive orders within [minOrder..maxOrder]
        const step = registrant.length > 1 ? Math.max(1, Math.floor((maxOrder - minOrder) / (registrant.length - 1))) : 1;
        let i = 0;
        for (const f of registrant) {
            const idx = updatedFields.findIndex(x => x.name === f.name);
            if (idx >= 0) {
                updatedFields[idx] = { ...updatedFields[idx], order: minOrder + i * step };
            }
            i++;
        }
    }

    addNewField() {
        // Open the New Field modal instead of inserting a placeholder
        this.selectedNewFieldName.set(null);
        this.addFieldPlacement.set('public');
        this.isAddFieldModalOpen.set(true);
    }

    closeAddFieldModal() {
        this.isAddFieldModalOpen.set(false);
        this.selectedNewFieldName.set(null);
    }

    confirmAddSelectedField() {
        const metadata = this.currentMetadata();
        const allowed = this.selectedNewField();
        if (!metadata || !allowed) { this.closeAddFieldModal(); return; }
        const visibility = this.addFieldPlacement();
        // Determine next order within the chosen visibility group
        const groupOrders = (metadata.fields || [])
            .filter(f => f.visibility === visibility)
            .map(f => f.order ?? 0);
        const nextOrder = (groupOrders.length ? Math.max(...groupOrders) : 0) + 1;

        const newField: ProfileMetadataField = {
            name: allowed.name,
            dbColumn: allowed.dbColumn || allowed.name,
            displayName: allowed.displayName,
            inputType: visibility === 'hidden' ? 'HIDDEN' : allowed.inputType,
            order: nextOrder,
            visibility: visibility,
            adminOnly: visibility === 'adminOnly',
            computed: allowed.computed ?? false,
            dataSource: allowed.dataSource
        } as ProfileMetadataField;

        const updatedFields = [...(metadata.fields || []), newField];
        const updatedMetadata: ProfileMetadata = { ...metadata, fields: updatedFields };
        this.currentMetadata.set(updatedMetadata);

        this.closeAddFieldModal();
        // Open edit modal so the user can fine-tune and then Save
        this.openEditModal(newField, updatedFields.length - 1);
    }

    removeField(index: number) {
        const metadata = this.currentMetadata();
        if (!metadata) return;

        const updatedFields = metadata.fields.filter((_, i) => i !== index);
        const updatedMetadata: ProfileMetadata = {
            ...metadata,
            fields: updatedFields
        };

        this.saveMetadata(updatedMetadata);
    }

    // Helper for template: remove by field name (avoids arrow functions in templates)
    removeFieldByName(name: string) {
        const metadata = this.currentMetadata();
        if (!metadata) return;
        const idx = metadata.fields.findIndex(f => f.name === name);
        if (idx >= 0) {
            this.openConfirm(
                'Remove Field',
                `Are you sure you want to remove the field "${name}"? This will update all jobs using this profile. The Jobs.PlayerProfileMetadataJson property will be updated for each job.`,
                () => this.removeField(idx)
            );
        }
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

    openTestModal(fieldName: string) {
        this.testFieldName.set(fieldName);
        this.testValue.set('');
        this.testResult.set(null);
        this.isTestModalOpen.set(true);
    }

    closeTestModal() {
        this.isTestModalOpen.set(false);
        this.testFieldName.set('');
        this.testValue.set('');
        this.testResult.set(null);
    }

    runValidationTest() {
        const metadata = this.currentMetadata();
        const fieldName = this.testFieldName();
        const testValue = this.testValue();

        if (!metadata || !fieldName) return;

        const field = metadata.fields.find(f => f.name === fieldName);
        if (!field) return;

        this.isTesting.set(true);
        this.testResult.set(null);

        this.migrationService.testValidation(
            field,
            testValue,
            (result) => {
                this.testResult.set(result);
                this.isTesting.set(false);
            },
            (error) => {
                const errorResult: ValidationTestResult = {
                    isValid: false,
                    messages: [`Test failed: ${error || 'Unknown error'}`],
                    testValue: testValue,
                    fieldName: fieldName
                };
                this.testResult.set(errorResult);
                this.isTesting.set(false);
            }
        );
    }

    // Helper for template
    trackByIndex(index: number): number {
        return index;
    }

    trackByName(_index: number, item: ProfileMetadataField) { return item.name; }

    // When visibility changes, enforce inputType HIDDEN for hidden fields
    onVisibilityChange(field: ProfileMetadataField) {
        if (!field) return;
        if (field.visibility === 'hidden') { field.inputType = 'HIDDEN'; return; }
        // If previously hidden, restore a sensible default
        if (field.inputType === 'HIDDEN') {
            field.inputType = field.dataSource ? 'SELECT' : 'TEXT';
        }
    }

    // Summarize validation into short badges for the table view
    getValidationBadges(field: ProfileMetadataField): string[] {
        const v = field.validation;
        if (!v) return [];
        const badges: string[] = [];
        if (v.required) badges.push('required');
        if (v.requiredTrue) badges.push('requiredTrue');
        if (v.email || field.inputType === 'EMAIL') badges.push('email');
        if (typeof v.minLength === 'number') badges.push(`minLen:${v.minLength}`);
        if (typeof v.maxLength === 'number') badges.push(`maxLen:${v.maxLength}`);
        if (typeof v.min === 'number') badges.push(`min:${v.min}`);
        if (typeof v.max === 'number') badges.push(`max:${v.max}`);
        if (v.pattern) badges.push('pattern');
        if (v.remote) badges.push('remote');
        return badges;
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
    // Confirm modal helpers
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
