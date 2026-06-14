import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
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
    getCategoryMeta,
    normalizeReportCategory
} from '@core/reporting/report-categories';

interface LibraryEntry {
    readonly isCrystal: boolean;          // true = still served by Crystal (CR); false = SP-Excel or Bold
    readonly isMigrated?: boolean;        // TEMP: Crystal-kind action actually rendered natively (EF + Syncfusion); drives the "SF" badge. Remove once all reports are off Crystal.
    readonly roles: readonly string[];    // assigned role names — populated for the SU all-roles view only
    readonly id: string;
    readonly title: string;
    readonly description?: string | null;
    readonly iconName?: string | null;
    readonly category: string | null;
    readonly sortOrder: number;
    readonly endpointPath?: string;       // crystal run target (controller action)
    readonly storedProcName?: string;     // sp-excel run target
    readonly parametersJson?: string | null; // sp-excel run params
    readonly boldReportName?: string;     // bold (RDL → PDF) run target — RDL filestem
    readonly spaRoute?: string;           // SpaComponent: in-app route (jobPath-relative path) to navigate to instead of downloading
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

// TEMP (CR retirement): Crystal-kind catalogue actions that are actually rendered
// natively by EF + Syncfusion — the controller action calls our *PdfService, not the
// Crystal engine. They intentionally keep Kind='CrystalReport' (the named-endpoint
// routing bucket), so dispatch is unchanged; this set only drives the distinct "SF"
// badge + tint. Remove this set + the badge markup once every report is off Crystal.
const MIGRATED_EF_ACTIONS = new Set<string>([
    'AmericanSelectEvaluation',
    'AmericanSelectMainEventRosters',
    'PlayerStats_E120',
    'Get_JobPlayers_TSICDAILY',
    'Get_Invoices_LastMonth',
    'Get_Invoices_LastMonthSummariesOnly',
    'TSICFeesYTDByCustomerAndJob',
    'TSICFeesYTDByCustomer',
    'Schedule_ByAgegroup',
    'TournamentRecruitingReportASL',
    'TournamentRecruitingReportUSL',
    'camp_excelexport_summer_pdf',
    'FieldUtilizationWithNominations',
    'ScheduleByClubAgTPerPage',
    'Schedule_Gamecards',
]);

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

/**
 * Parses a Bold Reports Action string from `reporting.JobReports` into the
 * RDL filestem. Action format:
 *   ExportBoldReport?reportName=TournamentRosterPacked
 */
function parseBoldReportAction(action: string | null | undefined): { reportName: string } | null {
    if (!action) return null;
    const qIdx = action.indexOf('?');
    if (qIdx < 0) return null;
    const params = new URLSearchParams(action.substring(qIdx + 1));
    const reportName = params.get('reportName');
    return reportName ? { reportName } : null;
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
    private readonly router = inject(Router);

    readonly type2Entries = signal<JobReportEntryDto[]>([]);
    readonly catalogueLoading = signal(false);
    readonly catalogueError = signal<string | null>(null);
    readonly runningId = signal<string | null>(null);
    readonly runError = signal<string | null>(null);
    readonly searchText = signal('');
    readonly selectedTab = signal<CategoryTab>('all');
    readonly recentIds = signal<readonly string[]>([]);

    /** SuperUser sees every role's reports + role-assignment chips (drives the all-roles view). */
    readonly isSuperuser = computed(() => {
        const user = this.authService.currentUser();
        const roles = user?.roles ?? (user?.role ? [user.role] : []);
        return roles.includes('Superuser');
    });

    /** Visible-to-this-user reports, unfiltered. */
    private readonly allEntries = computed<readonly LibraryEntry[]>(() => {
        const user = this.authService.currentUser();
        const callerRoles = user?.roles ?? (user?.role ? [user.role] : []);
        const ctx = buildJobVisibilityContext(this.jobService.currentJob(), this.pulseService.pulse(), callerRoles);

        // SuperUser: source BOTH kinds from the DB catalogue (all roles), deduped by
        // report identity with role chips. The global hard-coded Type-1 catalog is
        // suppressed for SU to avoid duplicating the DB's Crystal rows (and to preview
        // retiring that hard-coded source).
        if (callerRoles.includes('Superuser')) {
            return this.buildSuperuserEntries(this.type2Entries());
        }

        const type1: LibraryEntry[] = TYPE1_REPORT_CATALOG
            .filter(e => passesVisibilityRules(e.visibilityRules, ctx))
            .map(e => ({
                isCrystal: true,
                isMigrated: MIGRATED_EF_ACTIONS.has(e.endpointPath ?? ''),
                roles: [],
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
                    isCrystal: false,
                    roles: [],
                    id: `t2-${e.jobReportId}`,
                    title: e.title,
                    description: null,
                    iconName: e.iconName,
                    category: normalizeReportCategory(e.groupLabel),
                    sortOrder: e.sortOrder,
                    storedProcName: parsed?.spName ?? '',
                    parametersJson: parsed?.parametersJson ?? null,
                };
            });

        // Bold Reports (RDL → PDF) — the Crystal replacement target. Same
        // (Job, Role) gating as the SP rows; differs only in dispatcher branch.
        const bold: LibraryEntry[] = this.type2Entries()
            .filter(e => e.kind === 'BoldReport')
            .map(e => {
                const parsed = parseBoldReportAction(e.action);
                return {
                    isCrystal: false,
                    roles: [],
                    id: `bold-${e.jobReportId}`,
                    title: e.title,
                    description: null,
                    iconName: e.iconName,
                    category: normalizeReportCategory(e.groupLabel),
                    sortOrder: e.sortOrder,
                    boldReportName: parsed?.reportName ?? '',
                };
            });

        // SpaComponent (interactive tools) — Action is an in-app route (jobPath-relative
        // path). Dispatched via router.navigate, not a download. Lets interactive features
        // (PackedRoster Designer, check-in, …) live in the same role-gated catalogue.
        const spa: LibraryEntry[] = this.type2Entries()
            .filter(e => e.kind === 'SpaComponent')
            .map(e => ({
                isCrystal: false,
                roles: [],
                id: `spa-${e.jobReportId}`,
                title: e.title,
                description: null,
                iconName: e.iconName,
                category: normalizeReportCategory(e.groupLabel),
                sortOrder: e.sortOrder,
                spaRoute: e.action ?? '',
            }));

        return [...type1, ...type2, ...bold, ...spa];
    });

    /**
     * SuperUser view: collapse the all-roles catalogue (both kinds) into one entry per
     * report, keyed by Controller+Action, aggregating assigned role names into `roles`
     * for chip display. Lowest SortOrder wins for placement + display metadata.
     */
    private buildSuperuserEntries(rows: readonly JobReportEntryDto[]): readonly LibraryEntry[] {
        const byReport = new Map<string, { base: JobReportEntryDto; roles: Set<string> }>();
        for (const r of rows) {
            const key = `${r.controller}::${r.action}`.toLowerCase();
            const existing = byReport.get(key);
            if (existing) {
                if (r.roleName) existing.roles.add(r.roleName);
                if (r.sortOrder < existing.base.sortOrder) existing.base = r;
            } else {
                const roles = new Set<string>();
                if (r.roleName) roles.add(r.roleName);
                byReport.set(key, { base: r, roles });
            }
        }

        const entries: LibraryEntry[] = [];
        for (const { base, roles } of byReport.values()) {
            const isBold = base.kind === 'BoldReport';
            const isSp = base.kind === 'StoredProcedure';
            const isSpa = base.kind === 'SpaComponent';
            const isCrystal = !isBold && !isSp && !isSpa;
            const spParsed = isSp ? parseStoredProcAction(base.action) : null;
            const boldParsed = isBold ? parseBoldReportAction(base.action) : null;
            entries.push({
                isCrystal,
                isMigrated: isCrystal && MIGRATED_EF_ACTIONS.has(base.action),
                roles: [...roles].sort(),
                id: `su-${base.jobReportId}`,
                title: base.title,
                description: null,
                iconName: base.iconName,
                category: normalizeReportCategory(base.groupLabel),
                sortOrder: base.sortOrder,
                endpointPath: isCrystal ? base.action : undefined,
                storedProcName: spParsed?.spName ?? undefined,
                parametersJson: spParsed?.parametersJson ?? null,
                boldReportName: boldParsed?.reportName ?? undefined,
                spaRoute: isSpa ? base.action : undefined,
            });
        }
        return entries;
    }

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

        // Interactive (SpaComponent) entries navigate in-app instead of downloading.
        if (entry.spaRoute) {
            this.pushRecent(entry.id);
            this.navigateToSpa(entry.spaRoute);
            return;
        }

        this.runningId.set(entry.id);

        const download$ = entry.boldReportName
            ? this.reportingService.downloadReport('export-bold', { reportName: entry.boldReportName })
            : entry.isCrystal
                ? this.reportingService.downloadReport(entry.endpointPath!)
                : (() => {
                    const sp = parseSpRunParams(entry.parametersJson);
                    return this.reportingService.downloadReport('export-sp', {
                        spName: entry.storedProcName!,
                        bUseJobId: String(sp.bUseJobId),
                        bUseDateUnscheduled: String(sp.bUseDateUnscheduled)
                    });
                })();

        // Sticky progress toast (timeout 0 = no auto-dismiss). Held until the
        // response arrives so there's no awkward gap between "Generating…"
        // disappearing and the file landing — especially for Bold PDFs which
        // can take 10+ seconds. Dismissed explicitly in both next/error.
        const progressId = this.toast.show(`Generating ${entry.title}...`, 'info', 0);

        download$.subscribe({
            next: response => {
                const fallback = `TSIC-${entry.title.replace(/\W+/g, '-')}`;
                this.reportingService.triggerDownload(response, fallback);
                this.toast.dismiss(progressId);
                this.toast.show(`${entry.title} downloaded`, 'success');
                this.runningId.set(null);
                this.pushRecent(entry.id);
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

    /**
     * Navigates to an in-app SpaComponent route. The catalogue stores `Action` as the
     * jobPath-relative path (e.g. "reporting/packed-roster-designer"); we prepend the
     * caller's jobPath so the `:jobPath` prefix is preserved.
     */
    private navigateToSpa(route: string): void {
        const jobPath = this.authService.currentUser()?.jobPath;
        if (!jobPath) {
            this.toast.show('No job context — cannot open this tool.', 'danger');
            return;
        }
        const segments = route.split('/').filter(Boolean);
        this.router.navigate(['/', jobPath, ...segments]);
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
