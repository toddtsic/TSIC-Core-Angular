import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReportingService } from '@infrastructure/services/reporting.service';
import { JobService } from '@infrastructure/services/job.service';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';
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

    readonly type2Entries = signal<ReportCatalogueEntryDto[]>([]);
    readonly catalogueLoading = signal(false);
    readonly catalogueError = signal<string | null>(null);
    readonly runningId = signal<string | null>(null);
    readonly runError = signal<string | null>(null);
    readonly filterText = signal('');

    readonly cards = computed<LibraryCard[]>(() => {
        const ctx = buildJobVisibilityContext(this.jobService.currentJob(), this.pulseService.pulse());

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
            storedProcName: e.storedProcName
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
            : this.reportingService.downloadReport('export-sp', {
                spName: card.storedProcName!,
                bUseJobId: 'true'
            });

        download$.subscribe({
            next: response => {
                const fallback = `TSIC-${card.title.replace(/\W+/g, '-')}`;
                this.reportingService.triggerDownload(response, fallback);
                this.runningId.set(null);
            },
            error: err => {
                this.runningId.set(null);
                if (err.status === 401) {
                    this.runError.set('You must be logged in to run this report.');
                } else if (err.status === 403) {
                    this.runError.set('You do not have permission to run this report.');
                } else {
                    this.runError.set('Report failed to generate. Please try again.');
                }
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
