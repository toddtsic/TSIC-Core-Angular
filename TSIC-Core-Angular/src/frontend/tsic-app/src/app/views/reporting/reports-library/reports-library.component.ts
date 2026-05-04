import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReportingService } from '@infrastructure/services/reporting.service';
import { JobService } from '@infrastructure/services/job.service';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { ToastService } from '@shared-ui/toast.service';
import type { JobReportEntryDto } from '@core/api';
import { TYPE1_REPORT_CATALOG } from '@core/reporting/type1-report-catalog';
import { buildJobVisibilityContext, passesVisibilityRules } from '@core/reporting/visibility-rules';
import {
    REPORT_CATEGORIES,
    UNCATEGORIZED_META,
    type ReportCategoryMeta,
    getCategoryMeta
} from '@core/reporting/report-categories';

interface LibraryEntry {
    readonly kind: 'type1' | 'type2';
    readonly id: string;
    readonly title: string;
    readonly description?: string | null;
    readonly iconName?: string | null;
    readonly category: string | null;
    readonly sortOrder: number;
    readonly endpointPath?: string;       // type1
    readonly storedProcName?: string;     // type2
    readonly parametersJson?: string | null; // type2
}

interface CategoryGroup {
    readonly meta: ReportCategoryMeta;
    readonly entries: readonly LibraryEntry[];
}

type CategoryTab = 'all' | string; // 'all' | ReportCategory code | '__other__'

interface SpRunParams {
    bUseJobId: boolean;
    bUseDateUnscheduled: boolean;
}

const SP_RUN_DEFAULTS: SpRunParams = { bUseJobId: true, bUseDateUnscheduled: false };
const RECENTS_LIMIT = 5;
const RECENTS_KEY_PREFIX = 'tsic-reports-recents';

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

/**
 * Parses a stored-proc Action string from `reporting.JobReports` into the
 * spName + run-params shape the existing executor needs. Action format:
 *   ExportStoredProcedureResults?spName=[reporting].[Foo]&bUseJobId=true
 */
function parseStoredProcAction(action: string | null | undefined): { spName: string; parametersJson: string } | null {
    if (!action) return null;
    const qIdx = action.indexOf('?');
    if (qIdx < 0) return null;
    const params = new URLSearchParams(action.substring(qIdx + 1));
    const spName = params.get('spName');
    if (!spName) return null;
    const bUseJobId = params.get('bUseJobId') === 'true';
    const bUseDateUnscheduled = params.get('bUseDateUnscheduled') === 'true';
    return { spName, parametersJson: JSON.stringify({ bUseJobId, bUseDateUnscheduled }) };
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

    readonly type2Entries = signal<JobReportEntryDto[]>([]);
    readonly catalogueLoading = signal(false);
    readonly catalogueError = signal<string | null>(null);
    readonly runningId = signal<string | null>(null);
    readonly runError = signal<string | null>(null);
    readonly searchText = signal('');
    readonly selectedTab = signal<CategoryTab>('all');
    readonly recentIds = signal<readonly string[]>([]);

    /** Visible-to-this-user reports (Type 1 + Type 2), unfiltered. */
    private readonly allEntries = computed<readonly LibraryEntry[]>(() => {
        const user = this.authService.currentUser();
        const callerRoles = user?.roles ?? (user?.role ? [user.role] : []);
        const ctx = buildJobVisibilityContext(this.jobService.currentJob(), this.pulseService.pulse(), callerRoles);

        const type1: LibraryEntry[] = TYPE1_REPORT_CATALOG
            .filter(e => passesVisibilityRules(e.visibilityRules, ctx))
            .map(e => ({
                kind: 'type1',
                id: e.id,
                title: e.title,
                description: e.description,
                iconName: e.iconName,
                category: e.category,
                sortOrder: e.sortOrder,
                endpointPath: e.endpointPath
            }));

        // Stored-proc entries from reporting.JobReports — Crystal Reports are skipped
        // here because TYPE1_REPORT_CATALOG already covers them (avoids duplicates
        // until the full FE rewire retires the hardcoded TYPE1 source).
        // Categories: GroupLabel from legacy menus doesn't yet map to REPORT_CATEGORIES
        // codes — most rows fall into 'Other' until the category bridge lands.
        const type2: LibraryEntry[] = this.type2Entries()
            .filter(e => e.kind === 'StoredProcedure')
            .map(e => {
                const parsed = parseStoredProcAction(e.action);
                return {
                    kind: 'type2',
                    id: `t2-${e.jobReportId}`,
                    title: e.title,
                    description: null,
                    iconName: e.iconName,
                    category: e.groupLabel ?? null,
                    sortOrder: e.sortOrder,
                    storedProcName: parsed?.spName ?? '',
                    parametersJson: parsed?.parametersJson ?? null,
                };
            });

        return [...type1, ...type2];
    });

    /** Search-filtered flat list across ALL entries (search ignores tab). */
    readonly searchResults = computed<readonly LibraryEntry[]>(() => {
        const needle = this.searchText().trim().toLowerCase();
        if (!needle) return [];
        return this.allEntries()
            .filter(e =>
                e.title.toLowerCase().includes(needle)
                || (e.description?.toLowerCase().includes(needle) ?? false)
            )
            .slice()
            .sort((a, b) => a.sortOrder - b.sortOrder);
    });

    /** Search active flag — replaces tab content when true. */
    readonly isSearching = computed(() => this.searchText().trim().length > 0);

    /** Counts per tab (for badges). 'all' = total visible to user. */
    readonly tabCounts = computed<ReadonlyMap<CategoryTab, number>>(() => {
        const counts = new Map<CategoryTab, number>();
        const all = this.allEntries();
        counts.set('all', all.length);
        for (const e of all) {
            const key = e.category ?? '__other__';
            counts.set(key, (counts.get(key) ?? 0) + 1);
        }
        return counts;
    });

    /** Tab strip definitions in canonical order; only categories with >0 entries. */
    readonly availableTabs = computed<readonly { tab: CategoryTab; meta: ReportCategoryMeta | null; label: string; iconName: string; count: number }[]>(() => {
        const counts = this.tabCounts();
        const tabs: { tab: CategoryTab; meta: ReportCategoryMeta | null; label: string; iconName: string; count: number }[] = [
            { tab: 'all', meta: null, label: 'All', iconName: 'collection', count: counts.get('all') ?? 0 }
        ];
        for (const meta of REPORT_CATEGORIES) {
            const c = counts.get(meta.code) ?? 0;
            if (c > 0) tabs.push({ tab: meta.code, meta, label: meta.label, iconName: meta.iconName, count: c });
        }
        const otherCount = counts.get('__other__') ?? 0;
        if (otherCount > 0) {
            tabs.push({ tab: '__other__', meta: UNCATEGORIZED_META, label: UNCATEGORIZED_META.label, iconName: UNCATEGORIZED_META.iconName, count: otherCount });
        }
        return tabs;
    });

    /** Recents row, derived from recentIds + currently-visible entries. */
    readonly recentEntries = computed<readonly LibraryEntry[]>(() => {
        const ids = this.recentIds();
        if (ids.length === 0) return [];
        const byId = new Map(this.allEntries().map(e => [e.id, e] as const));
        return ids
            .map(id => byId.get(id))
            .filter((e): e is LibraryEntry => e !== undefined);
    });

    /** Entries within the currently-selected tab (ignores search). */
    readonly tabEntries = computed<readonly LibraryEntry[]>(() => {
        const tab = this.selectedTab();
        const all = this.allEntries();
        const filtered = tab === 'all'
            ? all
            : all.filter(e => (e.category ?? '__other__') === tab);
        return filtered.slice().sort((a, b) => a.sortOrder - b.sortOrder);
    });

    /** Grouped-by-category sections — only used on the "All" tab for browse-by-category structure. */
    readonly categoryGroups = computed<readonly CategoryGroup[]>(() => {
        if (this.selectedTab() !== 'all') return [];
        const buckets = new Map<string, LibraryEntry[]>();
        for (const e of this.tabEntries()) {
            const key = e.category ?? '__other__';
            const bucket = buckets.get(key);
            if (bucket) bucket.push(e); else buckets.set(key, [e]);
        }
        for (const list of buckets.values()) {
            list.sort((a, b) => a.sortOrder - b.sortOrder);
        }

        const groups: CategoryGroup[] = [];
        for (const meta of REPORT_CATEGORIES) {
            const entries = buckets.get(meta.code);
            if (entries && entries.length > 0) {
                groups.push({ meta, entries });
            }
        }
        const other = buckets.get('__other__');
        if (other && other.length > 0) {
            groups.push({ meta: UNCATEGORIZED_META, entries: other });
        }
        return groups;
    });

    ngOnInit(): void {
        this.loadCatalogue();
        this.recentIds.set(this.readRecentsFromStorage());
    }

    retryCatalogue(): void {
        this.loadCatalogue();
    }

    onSearchInput(value: string): void {
        this.searchText.set(value);
    }

    clearSearch(): void {
        this.searchText.set('');
    }

    selectTab(tab: CategoryTab): void {
        this.selectedTab.set(tab);
        // Clear search so the user sees the chosen tab's content, not stale results.
        if (this.searchText()) this.searchText.set('');
    }

    categoryMeta(code: string | null | undefined): ReportCategoryMeta {
        return getCategoryMeta(code);
    }

    runEntry(entry: LibraryEntry): void {
        this.runError.set(null);
        this.runningId.set(entry.id);

        const download$ = entry.kind === 'type1'
            ? this.reportingService.downloadReport(entry.endpointPath!)
            : (() => {
                const sp = parseSpRunParams(entry.parametersJson);
                return this.reportingService.downloadReport('export-sp', {
                    spName: entry.storedProcName!,
                    bUseJobId: String(sp.bUseJobId),
                    bUseDateUnscheduled: String(sp.bUseDateUnscheduled)
                });
            })();

        this.toast.show(`Generating ${entry.title}...`, 'info', 3000);

        download$.subscribe({
            next: response => {
                const fallback = `TSIC-${entry.title.replace(/\W+/g, '-')}`;
                this.reportingService.triggerDownload(response, fallback);
                this.toast.show(`${entry.title} downloaded`, 'success');
                this.runningId.set(null);
                this.pushRecent(entry.id);
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

    // ── Recents (localStorage, keyed per user+job) ────────────────────────────

    private recentsStorageKey(): string | null {
        const user = this.authService.currentUser();
        const regId = user?.regId;
        const jobPath = user?.jobPath;
        if (!regId || !jobPath) return null;
        return `${RECENTS_KEY_PREFIX}:${regId}:${jobPath}`;
    }

    private readRecentsFromStorage(): readonly string[] {
        const key = this.recentsStorageKey();
        if (!key) return [];
        try {
            const raw = localStorage.getItem(key);
            if (!raw) return [];
            const parsed = JSON.parse(raw);
            return Array.isArray(parsed) ? parsed.filter((x): x is string => typeof x === 'string') : [];
        } catch {
            return [];
        }
    }

    private pushRecent(id: string): void {
        const key = this.recentsStorageKey();
        if (!key) return;
        const current = this.recentIds();
        const next = [id, ...current.filter(x => x !== id)].slice(0, RECENTS_LIMIT);
        this.recentIds.set(next);
        try { localStorage.setItem(key, JSON.stringify(next)); } catch { /* quota / disabled */ }
    }
}
