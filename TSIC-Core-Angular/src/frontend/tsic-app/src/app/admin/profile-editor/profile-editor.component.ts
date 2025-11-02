import { Component, signal, computed, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ProfileMigrationService, ProfileMetadata, ProfileMetadataField, ValidationTestResult } from '../../core/services/profile-migration.service';
import { AuthService } from '../../core/services/auth.service';

type FieldType = 'TEXT' | 'TEXTAREA' | 'EMAIL' | 'NUMBER' | 'TEL' | 'DATE' | 'DATETIME' | 'CHECKBOX' | 'SELECT' | 'RADIO';

@Component({
    selector: 'app-profile-editor',
    standalone: true,
    imports: [CommonModule, FormsModule, RouterLink],
    templateUrl: './profile-editor.component.html',
    styleUrl: './profile-editor.component.scss'
})
export class ProfileEditorComponent implements OnInit {
    private readonly migrationService = inject(ProfileMigrationService);
    private readonly authService = inject(AuthService);

    // Navigation
    jobPath = computed(() => this.authService.currentUser()?.jobPath || 'tsic');

    // State signals
    isLoading = signal(false);
    isSaving = signal(false);
    errorMessage = signal<string | null>(null);
    successMessage = signal<string | null>(null);

    // Available profile types
    availableProfiles = signal<Array<{ type: string; display: string }>>([
        { type: 'PlayerProfile', display: 'Player Profile' },
        { type: 'ParentProfile', display: 'Parent Profile' },
        { type: 'CoachProfile', display: 'Coach Profile' }
    ]);

    selectedProfileType = signal<string | null>(null);
    currentMetadata = signal<ProfileMetadata | null>(null);

    // Create new profile modal state
    isCreateModalOpen = signal(false);
    selectedCloneSource = signal<string | null>(null);
    newProfileName = signal<string>('');
    isCloning = signal(false);

    // Edit modal state
    isEditModalOpen = signal(false);
    editingField = signal<ProfileMetadataField | null>(null);
    editingFieldIndex = signal<number>(-1);

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

    ngOnInit() {
        // Auto-select first profile if only one available (unlikely in practice)
        const profiles = this.availableProfiles();
        if (profiles.length === 1) {
            this.loadProfile(profiles[0].type);
        }
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
                console.error('Error loading profile metadata:', error);
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
        // Extract base name and predict next version
        const baseName = sourceProfile.replace(/\d+$/, '');

        // Get all existing profiles with same base
        const existingProfiles = this.availableProfiles()
            .map(p => p.type)
            .filter(type => type.startsWith(baseName));

        // Find max version
        let maxVersion = 0;
        const versionRegex = /(\d+)$/;
        for (const profile of existingProfiles) {
            const match = versionRegex.exec(profile);
            if (match) {
                maxVersion = Math.max(maxVersion, Number.parseInt(match[1], 10));
            }
        }

        // Generate new name
        const newVersion = maxVersion + 1;
        const newName = `${baseName}${newVersion}`;
        this.newProfileName.set(newName);
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

                    // Add to available profiles list
                    const displayName = result.newProfileType.replaceAll(/([A-Z])/g, ' $1').trim();
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
                console.error('Error cloning profile:', error);
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
    }

    closeEditModal() {
        this.isEditModalOpen.set(false);
        this.editingField.set(null);
        this.editingFieldIndex.set(-1);
    }

    saveFieldEdit() {
        const field = this.editingField();
        const index = this.editingFieldIndex();
        const metadata = this.currentMetadata();

        if (!field || index < 0 || !metadata) return;

        // Update the field in the metadata
        const updatedFields = [...metadata.fields];
        updatedFields[index] = field;

        const updatedMetadata: ProfileMetadata = {
            ...metadata,
            fields: updatedFields
        };

        this.saveMetadata(updatedMetadata);
        this.closeEditModal();
    }

    addNewField() {
        const metadata = this.currentMetadata();
        if (!metadata) return;

        const newField: ProfileMetadataField = {
            name: 'NewField',
            dbColumn: 'NewField',
            displayName: 'New Field',
            inputType: 'TEXT',
            order: metadata.fields.length,
            visibility: 'public',
            adminOnly: false,
            computed: false
        };

        const updatedFields = [...metadata.fields, newField];
        const updatedMetadata: ProfileMetadata = {
            ...metadata,
            fields: updatedFields
        };

        this.currentMetadata.set(updatedMetadata);

        // Open edit modal for the new field
        this.openEditModal(newField, updatedFields.length - 1);
    }

    removeField(index: number) {
        const metadata = this.currentMetadata();
        if (!metadata) return;

        if (!confirm('Are you sure you want to remove this field? This will affect all jobs using this profile.')) {
            return;
        }

        const updatedFields = metadata.fields.filter((_, i) => i !== index);
        const updatedMetadata: ProfileMetadata = {
            ...metadata,
            fields: updatedFields
        };

        this.saveMetadata(updatedMetadata);
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
                console.error('Error saving profile metadata:', error);
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
                console.error('Error testing validation:', error);
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
}
