import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { ReportingService } from '@infrastructure/services/reporting.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { ToastService } from '@shared-ui/toast.service';
import { X_JOB_REPORT_SECTIONS, type XJobReportEntry } from '@core/reporting/x-job-report-catalog';

/**
 * X-Job Report Library — SuperUser-only home for cross-job (platform-wide)
 * reports and accounting tools. The per-job Reports Library is fed by
 * `reporting.JobReports`, which is intentionally per-job; everything here
 * aggregates across ALL jobs, so it lives in a hard-coded catalogue instead
 * (x-job-report-catalog.ts) — change-by-commit, like the SU nav manifest.
 *
 * Reached via the SU nav's "X-Job Report Library" item (route seeded in
 * scripts/5) Re-Set Nav System.sql). Runs from any job — jobPath only matters
 * for the in-app 'route' tiles, which navigate jobPath-relative.
 */
@Component({
    selector: 'app-x-job-reports-library',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './x-job-reports-library.component.html',
    // Shares the per-job library's stylesheet so both surfaces stay visually identical.
    styleUrl: '../reports-library/reports-library.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class XJobReportsLibraryComponent {
    private readonly reportingService = inject(ReportingService);
    private readonly authService = inject(AuthService);
    private readonly toast = inject(ToastService);
    private readonly router = inject(Router);

    readonly sections = X_JOB_REPORT_SECTIONS;
    readonly runningId = signal<string | null>(null);
    readonly runError = signal<string | null>(null);

    runEntry(entry: XJobReportEntry): void {
        this.runError.set(null);

        if (entry.kind === 'route') {
            this.navigateToTool(entry.route!);
            return;
        }

        this.runningId.set(entry.id);

        const download$ = entry.kind === 'endpoint'
            ? this.reportingService.downloadReport(entry.endpointPath!)
            : this.reportingService.downloadReport('export-sp', {
                spName: entry.spName!,
                bUseJobId: 'false',
                bUseDateUnscheduled: 'false'
            });

        // Sticky progress toast (timeout 0 = no auto-dismiss); dismissed in next/error.
        const progressId = this.toast.show(`Generating ${entry.title}...`, 'info', 0);

        download$.subscribe({
            next: response => {
                const fallback = `TSIC-${entry.title.replace(/\W+/g, '-')}`;
                this.reportingService.triggerDownload(response, fallback);
                this.toast.dismiss(progressId);
                this.toast.show(`${entry.title} downloaded`, 'success');
                this.runningId.set(null);
            },
            error: err => {
                this.runningId.set(null);
                this.toast.dismiss(progressId);
                const msg = err?.status === 401 ? 'You must be logged in to run this report.'
                    : err?.status === 403 ? 'You do not have permission to run this report.'
                    : 'Report failed to generate. Please try again.';
                this.runError.set(msg);
                this.toast.show(msg, 'danger');
            }
        });
    }

    /** Navigates to an in-app tool, preserving the `:jobPath` prefix. */
    private navigateToTool(route: string): void {
        const jobPath = this.authService.currentUser()?.jobPath;
        if (!jobPath) {
            this.toast.show('No job context — cannot open this tool.', 'danger');
            return;
        }
        const segments = route.split('/').filter(Boolean);
        this.router.navigate(['/', jobPath, ...segments]);
    }
}
