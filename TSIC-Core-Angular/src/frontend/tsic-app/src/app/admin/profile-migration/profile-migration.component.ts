import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ProfileMigrationService, ProfileSummary, ProfileMigrationResult, ProfileBatchMigrationReport } from '../../core/services/profile-migration.service';
import { AuthService } from '../../core/services/auth.service';

@Component({
    selector: 'app-profile-migration',
    standalone: true,
    imports: [CommonModule, RouterLink],
    templateUrl: './profile-migration.component.html',
    styleUrls: ['./profile-migration.component.scss']
})
export class ProfileMigrationComponent implements OnInit {
    private readonly migrationService = inject(ProfileMigrationService);
    private readonly authService = inject(AuthService);

    // Navigation
    jobPath = computed(() => this.authService.currentUser()?.jobPath || 'tsic');

    // UI State
    isLoading = signal(false);
    isMigrating = signal(false);
    errorMessage = signal<string | null>(null);
    successMessage = signal<string | null>(null);

    // Data
    profiles = signal<ProfileSummary[]>([]);
    migrationReport = signal<ProfileBatchMigrationReport | null>(null);
    selectedProfile = signal<ProfileSummary | null>(null);
    previewResult = signal<ProfileMigrationResult | null>(null);

    // Confirmation modal
    showConfirmModal = signal(false);
    confirmModalTitle = signal('');
    confirmModalMessage = signal('');
    confirmModalAction = signal<(() => void) | null>(null);

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
        this.isLoading.set(true);
        this.errorMessage.set(null);

        this.migrationService.getProfileSummaries().subscribe({
            next: (profiles) => {
                this.profiles.set(profiles);
                this.isLoading.set(false);
            },
            error: (error) => {
                this.errorMessage.set(error.error?.message || 'Failed to load profiles');
                this.isLoading.set(false);
            }
        });
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

        this.migrationService.migrateAllProfiles(request).subscribe({
            next: (report) => {
                this.migrationReport.set(report);
                this.isMigrating.set(false);
                this.successMessage.set(
                    `Migration complete! ${report.successCount} succeeded, ${report.failureCount} failed, ${report.totalJobsAffected} jobs affected`
                );
                this.loadProfiles(); // Refresh status
            },
            error: (error) => {
                this.errorMessage.set(error.error?.message || 'Migration failed');
                this.isMigrating.set(false);
            }
        });
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

        this.migrationService.migrateProfile(profile.profileType).subscribe({
            next: (result) => {
                this.isMigrating.set(false);
                if (result.success) {
                    this.successMessage.set(
                        `${profile.profileType} migrated successfully! ${result.jobsAffected} jobs updated with ${result.fieldCount} fields`
                    );
                    this.loadProfiles(); // Refresh status
                } else {
                    this.errorMessage.set(result.errorMessage || 'Migration failed');
                }
            },
            error: (error) => {
                this.errorMessage.set(error.error?.message || 'Migration failed');
                this.isMigrating.set(false);
            }
        });
    }

    previewSingle(profile: ProfileSummary): void {
        // Only allow preview for migrated profiles
        if (!profile.allJobsMigrated) {
            return;
        }

        this.isLoading.set(true);
        this.selectedProfile.set(profile);

        this.migrationService.getProfileMetadata(profile.profileType).subscribe({
            next: (metadata) => {
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
                this.isLoading.set(false);
            },
            error: (error) => {
                this.errorMessage.set(error.error?.message || 'Failed to load current metadata');
                this.isLoading.set(false);
            }
        });
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

    clearMessages(): void {
        this.errorMessage.set(null);
        this.successMessage.set(null);
    }
}
