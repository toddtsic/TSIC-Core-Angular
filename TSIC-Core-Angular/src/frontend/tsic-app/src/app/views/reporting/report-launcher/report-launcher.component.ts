import { Component, inject, signal, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ReportingService } from '@infrastructure/services/reporting.service';
import { ToastService } from '@shared-ui/toast.service';

/**
 * Direct-URL fallback for `reporting/:action` (bookmarks, pasted URLs).
 * Menu clicks are intercepted in client-menu.component.ts and never reach this view.
 * On success/error, fires a toast and auto-navigates back so the page never lingers.
 */
@Component({
    selector: 'app-report-launcher',
    standalone: true,
    templateUrl: './report-launcher.component.html',
    styleUrl: './report-launcher.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReportLauncherComponent implements OnInit {
    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    private readonly reportingService = inject(ReportingService);
    private readonly toast = inject(ToastService);

    readonly loading = signal(false);
    readonly error = signal<string | null>(null);
    readonly reportAction = signal<string>('');

    ngOnInit(): void {
        const action = this.route.snapshot.paramMap.get('action');
        if (!action) {
            this.error.set('No report action specified.');
            return;
        }

        this.reportAction.set(action);
        this.downloadReport(action);
    }

    private downloadReport(action: string): void {
        this.loading.set(true);
        this.error.set(null);

        const queryParams: Record<string, string> = {};
        const snapshot = this.route.snapshot.queryParamMap;
        for (const key of snapshot.keys) {
            const value = snapshot.get(key);
            if (value) {
                queryParams[key] = value;
            }
        }

        this.toast.show(`Generating ${action}...`, 'info', 3000);

        this.reportingService.downloadReport(action, queryParams).subscribe({
            next: (response) => {
                const lower = action.toLowerCase();
                const fallback = lower.includes('excel') ? `TSIC-${action}.xlsx`
                    : lower.includes('ical') ? `TSIC-${action}.ics`
                    : `TSIC-${action}.pdf`;
                this.reportingService.triggerDownload(response, fallback);
                this.toast.show(`${action} downloaded`, 'success');
                this.loading.set(false);
                this.bounceBack();
            },
            error: (err) => {
                this.loading.set(false);
                const msg = err?.status === 401 ? 'You must be logged in to access this report.'
                    : err?.status === 403 ? 'You do not have permission to access this report.'
                    : 'An error occurred while generating the report. Please try again.';
                this.error.set(msg);
                this.toast.show(msg, 'danger');
            }
        });
    }

    retry(): void {
        const action = this.reportAction();
        if (action) {
            this.downloadReport(action);
        }
    }

    goBack(): void {
        window.history.back();
    }

    /**
     * After a successful direct-URL download, navigate back so the launcher view never lingers.
     * Falls back to the job home if there's no meaningful history to return to.
     */
    private bounceBack(): void {
        if (window.history.length > 1) {
            window.history.back();
        } else {
            const jobPath = this.route.snapshot.parent?.paramMap.get('jobPath')
                ?? window.location.pathname.split('/').filter(Boolean)[0]
                ?? '';
            this.router.navigateByUrl(jobPath ? `/${jobPath}` : '/');
        }
    }
}
