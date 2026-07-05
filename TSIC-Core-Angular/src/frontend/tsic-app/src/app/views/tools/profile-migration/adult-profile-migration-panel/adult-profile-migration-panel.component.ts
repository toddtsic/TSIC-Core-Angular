import { ChangeDetectionStrategy, Component, OnInit, inject, signal, computed, isDevMode } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AdultProfileMigrationService } from '@infrastructure/services/adult-profile-migration.service';
import { AdultProfileSummary } from '@core/api';
import { ProfileMetadata as PreviewMetadata } from '@infrastructure/view-models/profile-migration.models';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { ProfileFormPreviewComponent } from '@shared-ui/components/profile-form-preview/profile-form-preview.component';

/**
 * Adult side of the profile-migration tool. Structurally mirrors the player table, but keyed on the
 * two canonical profiles (AC1/AC2) materialized from legacy Jobs.RegformName_Coach. USLax is shown as
 * a per-profile capability count (not a separate profile) since it derives from a required sportAssnId.
 */
@Component({
    selector: 'app-adult-profile-migration-panel',
    standalone: true,
    imports: [CommonModule, TsicDialogComponent, ProfileFormPreviewComponent],
    templateUrl: './adult-profile-migration-panel.component.html',
    styleUrls: ['../profile-migration.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class AdultProfileMigrationPanelComponent implements OnInit {
    // Reference to satisfy the strict template analyzer for standalone usage.
    private readonly __tsicDialogComponentRef = TsicDialogComponent;
    readonly isDevMode = isDevMode();

    private readonly service = inject(AdultProfileMigrationService);

    // Service-owned state
    readonly profiles = this.service.adultSummaries;
    readonly isLoading = this.service.isLoading;
    readonly isMigrating = this.service.isMigrating;
    readonly previewResult = this.service.previewResult;

    // Component-local UI state
    errorMessage = signal<string | null>(null);
    successMessage = signal<string | null>(null);
    selectedProfile = signal<AdultProfileSummary | null>(null);
    showJsonView = signal(false);
    showUsLaxVariant = signal(false);

    // Confirmation modal
    showConfirmModal = signal(false);
    confirmModalTitle = signal('');
    confirmModalMessage = signal('');
    confirmModalAction = signal<(() => void) | null>(null);

    // Aggregate stats
    totalJobs = computed(() => this.profiles().reduce((sum, p) => sum + p.jobCount, 0));
    migratedJobs = computed(() => this.profiles().reduce((sum, p) => sum + p.migratedJobCount, 0));
    usLaxJobs = computed(() => this.profiles().reduce((sum, p) => sum + p.usLaxJobCount, 0));
    pendingProfiles = computed(() => this.profiles().filter(p => !p.allJobsMigrated));
    migratedProfiles = computed(() => this.profiles().filter(p => p.allJobsMigrated));
    allMigrated = computed(() => {
        const profs = this.profiles();
        return profs.length > 0 && profs.every(p => p.allJobsMigrated);
    });

    ngOnInit(): void {
        this.loadProfiles();
    }

    loadProfiles(): void {
        this.errorMessage.set(null);
        this.service.loadAdultSummaries();
    }

    // --- Migration actions --------------------------------------------------

    migrateAllPending(): void {
        const pending = this.pendingProfiles();
        if (pending.length === 0) {
            this.successMessage.set('All adult profiles already migrated!');
            return;
        }
        const pendingJobs = this.totalJobs() - this.migratedJobs();
        this.confirmModalTitle.set('Migrate All Pending Adult Profiles');
        this.confirmModalMessage.set(
            `Materialize ${pending.length} pending adult profile${pending.length > 1 ? 's' : ''} across ${pendingJobs} job${pendingJobs > 1 ? 's' : ''}? Already-migrated jobs are skipped.`
        );
        this.confirmModalAction.set(() => this.executeMigrateAll(pending.map(p => p.profile), false));
        this.showConfirmModal.set(true);
    }

    reMigrateAll(): void {
        const total = this.profiles().length;
        if (total === 0) {
            this.successMessage.set('No adult profiles available to migrate.');
            return;
        }
        const totalJobs = this.totalJobs();
        this.confirmModalTitle.set('Re-Migrate ALL Adult Profiles');
        this.confirmModalMessage.set(
            `This force-rewrites AdultProfileMetadataJson for all ${total} profile${total > 1 ? 's' : ''} across ${totalJobs} job${totalJobs > 1 ? 's' : ''}, overwriting already-migrated jobs. Continue?`
        );
        this.confirmModalAction.set(() => this.executeMigrateAll(null, true));
        this.showConfirmModal.set(true);
    }

    private executeMigrateAll(profiles: string[] | null, force: boolean): void {
        this.errorMessage.set(null);
        this.service.migrateAllAdult(
            { dryRun: false, force, profiles },
            report => {
                this.successMessage.set(
                    `Adult migration complete! ${report.successCount} succeeded, ${report.failureCount} failed, ${report.totalJobsAffected} jobs affected.`
                );
            },
            error => this.errorMessage.set(error.error?.message || error.error?.error || 'Adult migration failed')
        );
    }

    migrateSingle(profile: AdultProfileSummary): void {
        const force = profile.allJobsMigrated;
        this.confirmModalTitle.set(force ? 'Re-Migrate Adult Profile' : 'Migrate Adult Profile');
        this.confirmModalMessage.set(
            `${force ? 'Re-materialize' : 'Materialize'} ${profile.displayName} (${profile.profile}) across ${profile.jobCount} job${profile.jobCount > 1 ? 's' : ''}` +
            (profile.usLaxJobCount > 0 ? `, ${profile.usLaxJobCount} of which require USA Lacrosse.` : '.')
        );
        this.confirmModalAction.set(() => this.executeMigrateSingle(profile, force));
        this.showConfirmModal.set(true);
    }

    private executeMigrateSingle(profile: AdultProfileSummary, force: boolean): void {
        this.errorMessage.set(null);
        this.service.migrateAdultProfile(
            profile.profile,
            force,
            result => {
                if (result.success) {
                    this.successMessage.set(
                        `${profile.profile} migrated! ${result.jobsAffected} job${result.jobsAffected > 1 ? 's' : ''} updated` +
                        (result.usLaxJobsAffected > 0 ? ` (${result.usLaxJobsAffected} with USA Lacrosse).` : '.')
                    );
                } else {
                    this.errorMessage.set(result.errorMessage || 'Adult migration failed');
                }
            },
            error => this.errorMessage.set(error.error?.message || error.error?.error || 'Adult migration failed')
        );
    }

    // --- Preview ------------------------------------------------------------

    previewSingle(profile: AdultProfileSummary): void {
        this.selectedProfile.set(profile);
        this.showJsonView.set(false);
        this.showUsLaxVariant.set(false);
        this.service.previewAdultProfile(
            profile.profile,
            () => { /* previewResult set on the service signal */ },
            error => this.errorMessage.set(error.error?.message || error.error?.error || 'Adult preview failed')
        );
    }

    closePreview(): void {
        this.selectedProfile.set(null);
        this.showUsLaxVariant.set(false);
        this.service.clearPreviewResult();
    }

    /** Whether this profile has any USLax jobs — drives the coach-variant toggle in the preview. */
    hasUsLaxVariant = computed(() => {
        const r = this.previewResult();
        return !!r?.generatedMetadataUsLax && (this.selectedProfile()?.usLaxJobCount ?? 0) > 0;
    });

    /** Coach block for the preview, swapping to the USLax variant when toggled. */
    coachMeta(): PreviewMetadata | null {
        const r = this.previewResult();
        if (!r) return null;
        const set = this.showUsLaxVariant() && r.generatedMetadataUsLax ? r.generatedMetadataUsLax : r.generatedMetadata;
        return (set?.unassignedAdult ?? null) as unknown as PreviewMetadata | null;
    }

    /** Bridge a generated role block to the preview component's (view-model) metadata type. */
    roleMeta(m: unknown): PreviewMetadata | null {
        return (m ?? null) as PreviewMetadata | null;
    }

    toggleJsonView(): void {
        this.showJsonView.update(v => !v);
    }

    toggleUsLaxVariant(): void {
        this.showUsLaxVariant.update(v => !v);
    }

    // --- Export -------------------------------------------------------------

    exportSql(): void {
        this.errorMessage.set(null);
        this.service.exportAdultSql(
            blob => {
                const url = window.URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = url;
                a.download = `adult-profile-migration-${new Date().toISOString().split('T')[0]}.sql`;
                document.body.appendChild(a);
                a.click();
                document.body.removeChild(a);
                window.URL.revokeObjectURL(url);
                this.successMessage.set('Adult SQL script downloaded successfully.');
            },
            error => this.errorMessage.set(error.error?.message || 'Failed to export adult SQL script')
        );
    }

    // --- Modal plumbing -----------------------------------------------------

    confirmAction(): void {
        this.confirmModalAction()?.();
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
