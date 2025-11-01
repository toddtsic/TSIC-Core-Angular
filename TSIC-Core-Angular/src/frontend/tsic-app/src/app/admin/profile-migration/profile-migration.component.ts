import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ProfileMigrationService, ProfileSummary, ProfileMigrationResult, ProfileBatchMigrationReport } from '../../core/services/profile-migration.service';
import { AuthService } from '../../core/services/auth.service';
import { ProfileFormPreviewComponent } from '../../shared/components/profile-form-preview/profile-form-preview.component';

@Component({
    selector: 'app-profile-migration',
    standalone: true,
    imports: [CommonModule, RouterLink, ProfileFormPreviewComponent],
    templateUrl: './profile-migration.component.html',
    styleUrls: ['./profile-migration.component.scss']
})
export class ProfileMigrationComponent implements OnInit {
    private readonly migrationService = inject(ProfileMigrationService);
    private readonly authService = inject(AuthService);

    // Navigation
    jobPath = computed(() => this.authService.currentUser()?.jobPath || 'tsic');

    // Use service signals
    profiles = this.migrationService.profileSummaries;
    isLoading = this.migrationService.isLoading;

    // Component-specific UI State
    isMigrating = signal(false);
    errorMessage = signal<string | null>(null);
    successMessage = signal<string | null>(null);

    // Data
    migrationReport = signal<ProfileBatchMigrationReport | null>(null);
    selectedProfile = signal<ProfileSummary | null>(null);
    previewResult = signal<ProfileMigrationResult | null>(null);

    // Confirmation modal
    showConfirmModal = signal(false);
    confirmModalTitle = signal('');
    confirmModalMessage = signal('');
    confirmModalAction = signal<(() => void) | null>(null);

    // Preview toggle
    showJsonView = signal(false);

    // Computed sorted job names for dropdown
    sortedAffectedJobs = computed(() => {
        const jobs = this.previewResult()?.affectedJobNames || [];
        return [...jobs].sort((a, b) => a.localeCompare(b));
    });

    // Computed
    get totalJobs(): number {
        return this.profiles().reduce((sum, p) => sum + p.jobCount, 0);
    }

    get migratedJobs(): number {
        return this.profiles().reduce((sum, p) => sum + p.migratedJobCount, 0);
    }

    get pendingProfiles(): ProfileSummary[] {
        return this.profiles().filter(p => !p.allJobsMigrated);
    }

    get migratedProfiles(): ProfileSummary[] {
        return this.profiles().filter(p => p.allJobsMigrated);
    }

    ngOnInit(): void {
        this.loadProfiles();
    }

    loadProfiles(): void {
        this.errorMessage.set(null);
        this.migrationService.loadProfileSummaries();
    }

    migrateAllPending(): void {
        const pending = this.pendingProfiles;
        if (pending.length === 0) {
            this.successMessage.set('All profiles already migrated!');
            return;
        }

        // Show confirmation modal
        this.confirmModalTitle.set('Migrate All Pending Profiles');
        this.confirmModalMessage.set(
            `Are you sure you want to migrate ${pending.length} pending profile${pending.length > 1 ? 's' : ''} affecting ${this.totalJobs - this.migratedJobs} job${(this.totalJobs - this.migratedJobs) > 1 ? 's' : ''}?`
        );
        this.confirmModalAction.set(() => this.executeMigrateAllPending());
        this.showConfirmModal.set(true);
    }

    private executeMigrateAllPending(): void {
        const pending = this.pendingProfiles;
        this.isMigrating.set(true);
        this.errorMessage.set(null);

        const request = {
            dryRun: false,
            profileTypes: pending.map((p: ProfileSummary) => p.profileType)
        };

        this.migrationService.migrateAllProfiles(
            request,
            (report) => {
                this.migrationReport.set(report);
                this.isMigrating.set(false);
                this.successMessage.set(
                    `Migration complete! ${report.successCount} succeeded, ${report.failureCount} failed, ${report.totalJobsAffected} jobs affected`
                );
            },
            (error) => {
                this.errorMessage.set(error.error?.message || 'Migration failed');
                this.isMigrating.set(false);
            }
        );
    }

    migrateSingle(profile: ProfileSummary): void {
        // Show confirmation modal
        this.confirmModalTitle.set('Migrate Profile');
        this.confirmModalMessage.set(
            `Are you sure you want to migrate ${profile.profileType} affecting ${profile.jobCount} job${profile.jobCount > 1 ? 's' : ''}?`
        );
        this.confirmModalAction.set(() => this.executeMigrateSingle(profile));
        this.showConfirmModal.set(true);
    }

    private executeMigrateSingle(profile: ProfileSummary): void {
        this.isMigrating.set(true);
        this.errorMessage.set(null);

        this.migrationService.migrateProfile(
            profile.profileType,
            (result) => {
                this.isMigrating.set(false);
                if (result.success) {
                    this.successMessage.set(
                        `${profile.profileType} migrated successfully! ${result.jobsAffected} jobs updated with ${result.fieldCount} fields`
                    );
                } else {
                    this.errorMessage.set(result.errorMessage || 'Migration failed');
                }
            },
            (error) => {
                this.errorMessage.set(error.error?.message || 'Migration failed');
                this.isMigrating.set(false);
            }
        );
    }

    previewSingle(profile: ProfileSummary): void {
        // Only allow preview for migrated profiles
        if (!profile.allJobsMigrated) {
            return;
        }

        this.selectedProfile.set(profile);
        this.showJsonView.set(false); // Default to form view

        this.migrationService.getProfileMetadata(
            profile.profileType,
            (metadata) => {
                // Create a result object that shows current metadata
                const result: ProfileMigrationResult = {
                    profileType: profile.profileType,
                    success: true,
                    fieldCount: metadata.fields.length,
                    jobsAffected: profile.jobCount,
                    affectedJobIds: [],
                    affectedJobNames: profile.sampleJobNames,
                    generatedMetadata: metadata,
                    warnings: []
                };
                this.previewResult.set(result);
            },
            (error) => {
                this.errorMessage.set(error.error?.message || 'Failed to load current metadata');
            }
        );
    }

    closePreview(): void {
        this.previewResult.set(null);
        this.selectedProfile.set(null);
    }

    confirmAction(): void {
        const action = this.confirmModalAction();
        if (action) {
            action();
        }
        this.closeConfirmModal();
    }

    closeConfirmModal(): void {
        this.showConfirmModal.set(false);
        this.confirmModalTitle.set('');
        this.confirmModalMessage.set('');
        this.confirmModalAction.set(null);
    }

    toggleJsonView(): void {
        this.showJsonView.update(current => !current);
    }

    clearMessages(): void {
        this.errorMessage.set(null);
        this.successMessage.set(null);
    }
}
