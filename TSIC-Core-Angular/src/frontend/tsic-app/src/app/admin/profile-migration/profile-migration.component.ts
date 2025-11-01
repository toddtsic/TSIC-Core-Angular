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

        if (!confirm(`Migrate ${pending.length} pending profiles affecting ${this.totalJobs - this.migratedJobs} jobs?`)) {
            return;
        }

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
        if (!confirm(`Migrate ${profile.profileType} affecting ${profile.jobCount} jobs?`)) {
            return;
        }

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
        this.isLoading.set(true);
        this.selectedProfile.set(profile);

        this.migrationService.previewProfileMigration(profile.profileType).subscribe({
            next: (result) => {
                this.previewResult.set(result);
                this.isLoading.set(false);
            },
            error: (error) => {
                this.errorMessage.set(error.error?.message || 'Preview failed');
                this.isLoading.set(false);
            }
        });
    }

    closePreview(): void {
        this.previewResult.set(null);
        this.selectedProfile.set(null);
    }

    clearMessages(): void {
        this.errorMessage.set(null);
        this.successMessage.set(null);
    }
}
