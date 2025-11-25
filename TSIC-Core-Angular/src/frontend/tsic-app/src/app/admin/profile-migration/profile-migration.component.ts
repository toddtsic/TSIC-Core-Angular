import { FormsModule } from '@angular/forms';
import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ProfileMigrationService } from '../../core/services/profile-migration.service';
import { ProfileSummary } from '../../core/models/profile-migration.models';
import { TsicDialogComponent } from '../../shared/components/tsic-dialog/tsic-dialog.component';
import { AuthService } from '../../core/services/auth.service';
import { ProfileFormPreviewComponent } from '../../shared/components/profile-form-preview/profile-form-preview.component';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';

@Component({
    selector: 'app-profile-migration',
    standalone: true,
    imports: [CommonModule, RouterLink, ProfileFormPreviewComponent, FormsModule, TsicDialogComponent, MatButtonModule, MatCardModule],
    templateUrl: './profile-migration.component.html',
    styleUrls: ['./profile-migration.component.scss']
})
export class ProfileMigrationComponent implements OnInit {
    // Reference to satisfy strict template analyzer for standalone component usage.
    private readonly __tsicDialogComponentRef = TsicDialogComponent;
    // For dropdown filtering (signal-based)
    selectedProfileType = signal<string | null>(null);
    filteredProfiles = computed(() => {
        const all = this.profiles();
        const selected = this.selectedProfileType();
        if (!selected) return all;
        return all.filter((p: any) => p.profileType === selected);
    });
    onProfileTypeChange(event: Event) {
        const value = (event.target as HTMLSelectElement).value;
        this.selectedProfileType.set(value || null);
    }
    private readonly migrationService = inject(ProfileMigrationService);
    private readonly authService = inject(AuthService);

    // Navigation
    jobPath = computed(() => this.authService.currentUser()?.jobPath || 'tsic');

    // Use service signals
    profiles = this.migrationService.profileSummaries;
    isLoading = this.migrationService.isLoading;
    isMigrating = this.migrationService.isMigrating;

    // Component-specific UI State
    errorMessage = signal<string | null>(null);
    successMessage = signal<string | null>(null);

    // Data
    migrationReport = this.migrationService.batchMigrationReport;
    selectedProfile = signal<ProfileSummary | null>(null);
    previewResult = this.migrationService.previewResult;

    // Confirmation modal
    showConfirmModal = signal(false);
    confirmModalTitle = signal('');
    confirmModalMessage = signal('');
    confirmModalAction = signal<(() => void) | null>(null);

    // Preview toggle
    showJsonView = signal(false);

    // Job-specific preview
    selectedJobId = signal<string | null>(null);
    jobSpecificOptions = signal<Record<string, any> | null>(null);

    // Computed sorted job names for dropdown (filtered to current year ± 1)
    sortedAffectedJobs = computed(() => {
        const result = this.previewResult();
        if (!result) return [];

        const currentYear = new Date().getFullYear();
        const minYear = currentYear - 1;
        const maxYear = currentYear + 1;

        const jobs = result.affectedJobNames || [];
        const years = result.affectedJobYears || [];

        // Filter jobs by year range and create tuples
        const filteredJobsWithIndex = jobs
            .map((name, index) => ({ name, year: years[index], index }))
            .filter(item => {
                if (!item.year) return true; // Include jobs with no year
                const year = Number.parseInt(item.year, 10);
                return year >= minYear && year <= maxYear;
            });

        // Sort by name and return
        const sorted = [...filteredJobsWithIndex].sort((a, b) => a.name.localeCompare(b.name));
        return sorted.map(item => ({ name: item.name, originalIndex: item.index }));
    });

    // Computed: Get job ID from filtered job (using original index)
    getJobIdFromFilteredIndex(filteredIndex: number): string | null {
        const filtered = this.sortedAffectedJobs();
        if (filteredIndex < 0 || filteredIndex >= filtered.length) return null;

        const originalIndex = filtered[filteredIndex].originalIndex;
        const jobIds = this.previewResult()?.affectedJobIds;
        return jobIds?.[originalIndex] ?? null;
    }

    // Computed: All affected jobs (full list, not filtered)
    allAffectedJobs = computed(() => {
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
        this.errorMessage.set(null);

        const request = {
            dryRun: false,
            profileTypes: pending.map((p: ProfileSummary) => p.profileType)
        };

        this.migrationService.migrateAllProfiles(
            request,
            (report) => {
                this.successMessage.set(
                    `Migration complete! ${report.successCount} succeeded, ${report.failureCount} failed, ${report.totalJobsAffected} jobs affected`
                );
            },
            (error) => {
                this.errorMessage.set(error.error?.message || 'Migration failed');
            }
        );
    }

    reMigrateAll(): void {
        // Always allow forcing a full re-migration of all profiles
        const totalProfiles = this.profiles().length;
        if (totalProfiles === 0) {
            this.successMessage.set('No profiles available to migrate.');
            return;
        }

        const totalJobs = this.totalJobs;
        this.confirmModalTitle.set('Re-Migrate ALL Profiles');
        this.confirmModalMessage.set(
            `This will re-fetch GitHub POCOs and re-apply metadata for all ${totalProfiles} profile${totalProfiles > 1 ? 's' : ''} across ${totalJobs} job${totalJobs > 1 ? 's' : ''}. Continue?`
        );
        this.confirmModalAction.set(() => this.executeReMigrateAll());
        this.showConfirmModal.set(true);
    }

    private executeReMigrateAll(): void {
        this.errorMessage.set(null);

        const request = {
            dryRun: false
            // profileTypes omitted intentionally to migrate ALL profiles
        };

        this.migrationService.migrateAllProfiles(
            request,
            (report) => {
                this.successMessage.set(
                    `Re-migration complete! ${report.successCount} succeeded, ${report.failureCount} failed, ${report.totalJobsAffected} jobs affected`
                );
            },
            (error) => {
                this.errorMessage.set(error.error?.message || 'Re-migration failed');
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
        this.errorMessage.set(null);

        this.migrationService.migrateProfile(
            profile.profileType,
            (result) => {
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

        // Use the preview endpoint to get full job list and metadata
        this.migrationService.previewProfileMigration(
            profile.profileType,
            (_result) => { },
            (error) => {
                this.errorMessage.set(error || 'Failed to load profile metadata');
            }
        );
    }

    closePreview(): void {
        this.selectedProfile.set(null);
        this.selectedJobId.set(null);
        this.jobSpecificOptions.set(null);
    }

    /**
     * Handle job selection change in preview modal
     * Fetches job-specific JsonOptions and enriches the metadata
     */
    onJobSelected(event: Event): void {
        const select = event.target as HTMLSelectElement;
        const selectedIndex = select.selectedIndex;

        if (selectedIndex === 0) {
            // "Select a job..." option selected - clear job-specific data
            this.selectedJobId.set(null);
            this.jobSpecificOptions.set(null);
            return;
        }

        // Get job ID from the filtered job list (subtract 1 for placeholder option)
        const jobId = this.getJobIdFromFilteredIndex(selectedIndex - 1);
        if (!jobId) {
            // Silently ignore if mapping fails (dropdown may be stale after reload)
            return;
        }

        this.selectedJobId.set(jobId);

        const profile = this.selectedProfile();
        if (!profile) return;

        // Fetch metadata with job-specific options
        this.migrationService.getProfileMetadataWithJobOptions(
            profile.profileType,
            jobId,
            (result) => {
                // Update the preview with job-specific options
                this.jobSpecificOptions.set(result.jsonOptions ?? null);
            },
            (error) => {
                this.errorMessage.set('Failed to load job-specific options');
            }
        );
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
