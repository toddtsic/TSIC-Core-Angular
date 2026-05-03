import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReportingService } from '@infrastructure/services/reporting.service';
import { JobService } from '@infrastructure/services/job.service';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { ToastService } from '@shared-ui/toast.service';
import type { ReportCatalogueEntryDto } from '@core/api';
import { TYPE1_REPORT_CATALOG, Type1ReportEntry } from '@core/reporting/type1-report-catalog';
import { buildJobVisibilityContext, passesVisibilityRules } from '@core/reporting/visibility-rules';

interface LibraryCard {
    readonly kind: 'type1' | 'type2';
    readonly id: string;
    readonly title: string;
    readonly description?: string | null;
    readonly iconName?: string | null;
    readonly sortOrder: number;
    // kind === 'type1'
    readonly endpointPath?: string;
    // kind === 'type2'
    readonly storedProcName?: string;
    readonly parametersJson?: string | null;
}

interface SpRunParams {
    bUseJobId: boolean;
    bUseDateUnscheduled: boolean;
}

const SP_RUN_DEFAULTS: SpRunParams = { bUseJobId: true, bUseDateUnscheduled: false };

function parseSpRunParams(parametersJson: string | null | undefined): SpRunParams {
    if (!parametersJson) return SP_RUN_DEFAULTS;
    try {
        const parsed = JSON.parse(parametersJson) as Partial<SpRunParams>;
        return {
            bUseJobId: parsed.bUseJobId ?? SP_RUN_DEFAULTS.bUseJobId,
            bUseDateUnscheduled: parsed.bUseDateUnscheduled ?? SP_RUN_DEFAULTS.bUseDateUnscheduled
        };
    } catch {
        return SP_RUN_DEFAULTS;
    }
}

@Component({
    selector: 'app-reports-library',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './reports-library.component.html',
    styleUrl: './reports-library.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReportsLibraryComponent implements OnInit {
    private readonly reportingService = inject(ReportingService);
    private readonly jobService = inject(JobService);
    private readonly pulseService = inject(JobPulseService);
    private readonly authService = inject(AuthService);
    private readonly toast = inject(ToastService);

    readonly type2Entries = signal<ReportCatalogueEntryDto[]>([]);
    readonly catalogueLoading = signal(false);
    readonly catalogueError = signal<string | null>(null);
    readonly runningId = signal<string | null>(null);
    readonly runError = signal<string | null>(null);
    readonly filterText = signal('');

    readonly cards = computed<LibraryCard[]>(() => {
        const user = this.authService.currentUser();
        const callerRoles = user?.roles ?? (user?.role ? [user.role] : []);
        const ctx = buildJobVisibilityContext(this.jobService.currentJob(), this.pulseService.pulse(), callerRoles);

        const type1Visible = TYPE1_REPORT_CATALOG
            .filter(e => passesVisibilityRules(e.visibilityRules, ctx))
            .map<LibraryCard>(e => ({
                kind: 'type1',
                id: e.id,
                title: e.title,
                description: e.description,
                iconName: e.iconName,
                sortOrder: e.sortOrder,
                endpointPath: e.endpointPath
            }));

        const type2Cards = this.type2Entries().map<LibraryCard>(e => ({
            kind: 'type2',
            id: `t2-${e.reportId}`,
            title: e.title,
            description: e.description,
            iconName: e.iconName,
            sortOrder: e.sortOrder,
            storedProcName: e.storedProcName,
            parametersJson: e.parametersJson
        }));

        const all = [...type2Cards, ...type1Visible].sort((a, b) => a.sortOrder - b.sortOrder);

        const needle = this.filterText().trim().toLowerCase();
        if (!needle) return all;
        return all.filter(c =>
            c.title.toLowerCase().includes(needle)
            || (c.description?.toLowerCase().includes(needle) ?? false)
        );
    });

    ngOnInit(): void {
        this.loadCatalogue();
    }

    retryCatalogue(): void {
        this.loadCatalogue();
    }

    onFilterInput(value: string): void {
        this.filterText.set(value);
    }

    runCard(card: LibraryCard): void {
        this.runError.set(null);
        this.runningId.set(card.id);

        const download$ = card.kind === 'type1'
            ? this.reportingService.downloadReport(card.endpointPath!)
            : (() => {
                const sp = parseSpRunParams(card.parametersJson);
                return this.reportingService.downloadReport('export-sp', {
                    spName: card.storedProcName!,
                    bUseJobId: String(sp.bUseJobId),
                    bUseDateUnscheduled: String(sp.bUseDateUnscheduled)
                });
            })();

        this.toast.show(`Generating ${card.title}...`, 'info', 3000);

        download$.subscribe({
            next: response => {
                const fallback = `TSIC-${card.title.replace(/\W+/g, '-')}`;
                this.reportingService.triggerDownload(response, fallback);
                this.toast.show(`${card.title} downloaded`, 'success');
                this.runningId.set(null);
            },
            error: err => {
                this.runningId.set(null);
                const msg = err?.status === 401 ? 'You must be logged in to run this report.'
                    : err?.status === 403 ? 'You do not have permission to run this report.'
                    : 'Report failed to generate. Please try again.';
                this.runError.set(msg);
                this.toast.show(msg, 'danger');
            }
        });
    }

    private loadCatalogue(): void {
        this.catalogueLoading.set(true);
        this.catalogueError.set(null);

        this.reportingService.getCatalogue().subscribe({
            next: rows => {
                this.type2Entries.set(rows);
                this.catalogueLoading.set(false);
            },
            error: () => {
                this.catalogueLoading.set(false);
                this.catalogueError.set('Could not load the dynamic report catalogue. Showing legacy reports only.');
            }
        });
    }
}
