import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { ChecklistBackLinkComponent } from '../../scheduling/shared/components/checklist-back-link/checklist-back-link.component';
import { ReportingService } from '@infrastructure/services/reporting.service';
import { JobService } from '@infrastructure/services/job.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { ToastService } from '@shared-ui/toast.service';
import type { JobReportEntryDto, ReportLibraryEntryDto } from '@core/api';
import {
    REPORT_CATEGORIES,
    UNCATEGORIZED_META,
    type ReportCategoryMeta,
    getCategoryMeta,
    normalizeReportCategory
} from '@core/reporting/report-categories';

/**
 * One row on screen, in either mode.
 *   shelf  — a reporting.JobReports row for (this job, the caller's role): runnable, removable.
 *   browse — a reporting.ReportLibrary entry the caller's shelf may be stocked from: addable.
 */
interface LibraryEntry {
    readonly isCrystal: boolean;          // true = still served by Crystal (CR); false = SP-Excel or Bold
    readonly isMigrated?: boolean;        // TEMP: Crystal-kind action actually rendered natively (EF + Syncfusion); drives the "SF" badge. Remove once all reports are off Crystal.
    readonly roles: readonly string[];    // assigned role names — populated for the SU all-roles view only
    readonly id: string;
    readonly title: string;
    readonly description?: string | null;
    readonly tags?: string | null;        // library search terms ('excel,roster,medical'); never shown
    readonly iconName?: string | null;
    readonly category: string | null;
    readonly sortOrder: number;
    readonly endpointPath?: string;       // crystal run target (controller action)
    readonly storedProcName?: string;     // sp-excel run target
    readonly parametersJson?: string | null; // sp-excel run params
    readonly boldReportName?: string;     // bold (RDL → PDF) run target — RDL filestem
    readonly spaRoute?: string;           // SpaComponent: in-app route (jobPath-relative path) to navigate to instead of downloading
    readonly jobReportId?: string;        // shelf rows: the row Remove deletes
    readonly reportLibraryId?: string | null; // both modes: the library entry (shelf rows: what it was stocked from)
    readonly onShelf?: boolean;           // browse rows: already on the caller's shelf
    readonly scope?: string;              // browse rows: JobOnly | CrossJob | CrossWebsite — a fact about the query
    readonly minRoleName?: string;        // browse rows: the minimum role that may hold it
}

interface CategoryGroup {
    readonly meta: ReportCategoryMeta;
    readonly entries: readonly LibraryEntry[];
}

type CategoryTab = 'all' | string; // 'all' | ReportCategory code | '__other__'
type LibraryMode = 'shelf' | 'browse';

interface SpRunParams {
    bUseJobId: boolean;
    bUseDateUnscheduled: boolean;
}

const SP_RUN_DEFAULTS: SpRunParams = { bUseJobId: true, bUseDateUnscheduled: false };
const RECENTS_LIMIT = 5;
const RECENTS_KEY_PREFIX = 'tsic-reports-recents';

// Actions that were BORN native — Kind='CrystalReport' (the named-endpoint routing bucket)
// but never served by Crystal, so they earn neither the "Crystal" badge nor the "SF" migrated
// marker and render neutral like SP rows. Cosmetic only; reporting.JobReports is the entitlement.
const NATIVE_DB_ACTIONS = new Set<string>([
    'ThirdPartyRosterExport',
]);

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
    'Job_Club_Rosters',
    'Job_Rosters_NoMedical',
    'clubrostersNoMedicalII',
    'Club_AllJobs_Rosters_NoMedical',
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

/** Run-target fields for a row of the given Kind + Action (shared by shelf and SU union rows). */
function runTargets(kind: string, action: string): Pick<LibraryEntry, 'isCrystal' | 'isMigrated' | 'endpointPath' | 'storedProcName' | 'parametersJson' | 'boldReportName' | 'spaRoute'> {
    if (kind === 'StoredProcedure') {
        const parsed = parseStoredProcAction(action);
        return { isCrystal: false, storedProcName: parsed?.spName ?? '', parametersJson: parsed?.parametersJson ?? null };
    }
    if (kind === 'BoldReport') {
        return { isCrystal: false, boldReportName: parseBoldReportAction(action)?.reportName ?? '' };
    }
    if (kind === 'SpaComponent') {
        // Action is an in-app route; dispatched via router.navigate, not a download.
        return { isCrystal: false, spaRoute: action ?? '' };
    }
    // Crystal-kind: Action is a bare controller action. Every active one now renders
    // natively, so isCrystal (the "Crystal" badge/tint) is reserved for anything that
    // still doesn't. NATIVE_DB_ACTIONS marks rows that were BORN native (no badge at all).
    const bornNative = NATIVE_DB_ACTIONS.has(action);
    const migrated = MIGRATED_EF_ACTIONS.has(action);
    return { isCrystal: !bornNative && !migrated, isMigrated: migrated, endpointPath: action };
}

@Component({
    selector: 'app-reports-library',
    standalone: true,
    imports: [CommonModule, ChecklistBackLinkComponent],
    templateUrl: './reports-library.component.html',
    styleUrl: './reports-library.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReportsLibraryComponent implements OnInit {
    private readonly reportingService = inject(ReportingService);
    private readonly jobService = inject(JobService);
    private readonly authService = inject(AuthService);
    private readonly toast = inject(ToastService);
    private readonly router = inject(Router);
    private readonly route = inject(ActivatedRoute);

    // ── Shelf (reporting.JobReports for this job + the caller's role) ──
    readonly type2Entries = signal<JobReportEntryDto[]>([]);
    readonly catalogueLoading = signal(false);
    readonly catalogueError = signal<string | null>(null);

    // ── Library (reporting.ReportLibrary, gated to what this shelf may hold) ──
    readonly libraryEntries = signal<ReportLibraryEntryDto[]>([]);
    readonly libraryLoading = signal(false);
    readonly libraryLoaded = signal(false);
    readonly libraryError = signal<string | null>(null);

    /** 'shelf' = what I have (run / remove); 'browse' = what I could add. */
    readonly mode = signal<LibraryMode>('shelf');

    /** Superuser only: swap the SU shelf for the read-only union of every role's rows. */
    readonly showAllRoles = signal(false);

    readonly runningId = signal<string | null>(null);
    /** Row with an add / remove in flight (one at a time — the buttons disable on it). */
    readonly busyId = signal<string | null>(null);
    readonly runError = signal<string | null>(null);
    readonly searchText = signal('');
    readonly selectedTab = signal<CategoryTab>('all');
    readonly recentIds = signal<readonly string[]>([]);

    /** The last row removed from the shelf — drives the Undo banner. Cleared on undo, dismiss, or the next remove. */
    readonly lastRemoved = signal<{ readonly title: string; readonly reportLibraryId: string | null } | null>(null);

    readonly isSuperuser = computed(() => {
        const user = this.authService.currentUser();
        const roles = user?.roles ?? (user?.role ? [user.role] : []);
        return roles.includes('Superuser');
    });

    /** The SU all-roles union: role chips, no Add / Remove (it is not one shelf). */
    readonly isUnionView = computed(() => this.isSuperuser() && this.showAllRoles());

    readonly isBrowsing = computed(() => this.mode() === 'browse');

    /** Shelf rows as they render. The library row behind each one supplies its description. */
    private readonly shelfEntries = computed<readonly LibraryEntry[]>(() => {
        if (this.isUnionView()) {
            return this.buildUnionEntries(this.type2Entries());
        }

        // The library IS reporting.JobReports for this job and this caller's role. Row
        // existence is the entitlement, for EVERY Kind. A role with no rows correctly shows
        // an empty shelf — and Browse is where it goes to fill it.
        return this.type2Entries().map(e => ({
            roles: [] as readonly string[],
            id: `t2-${e.jobReportId}`,
            title: e.title,
            description: e.description ?? null,
            iconName: e.iconName ?? null,
            category: normalizeReportCategory(e.groupLabel),
            sortOrder: e.sortOrder,
            jobReportId: e.jobReportId,
            reportLibraryId: e.reportLibraryId ?? null,
            ...runTargets(e.kind, e.action),
        }));
    });

    /**
     * SuperUser union view: collapse the all-roles catalogue into one entry per report,
     * keyed by Controller+Action, aggregating assigned role names into `roles` for chip
     * display. Lowest SortOrder wins for placement + display metadata. Read-only.
     */
    private buildUnionEntries(rows: readonly JobReportEntryDto[]): readonly LibraryEntry[] {
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
            entries.push({
                roles: [...roles].sort(),
                id: `su-${base.jobReportId}`,
                title: base.title,
                description: base.description ?? null,
                iconName: base.iconName,
                category: normalizeReportCategory(base.groupLabel),
                sortOrder: base.sortOrder,
                reportLibraryId: base.reportLibraryId ?? null,
                ...runTargets(base.kind, base.action),
            });
        }
        return entries;
    }

    /** Library rows as they render in Browse: addable, or already on the shelf. Never runnable from here. */
    private readonly browseEntries = computed<readonly LibraryEntry[]>(() =>
        this.libraryEntries().map((l, i) => ({
            isCrystal: false,
            roles: [] as readonly string[],
            id: `lib-${l.reportLibraryId}`,
            title: l.title,
            description: l.description ?? null,
            tags: l.tags ?? null,
            iconName: l.iconName ?? null,
            category: normalizeReportCategory(l.categoryCode),
            sortOrder: i,   // server order: category, SortOrder, title
            reportLibraryId: l.reportLibraryId,
            jobReportId: l.shelfJobReportId ?? undefined,
            onShelf: !!l.shelfJobReportId,
            scope: l.scope,
            minRoleName: l.minRoleName,
        })));

    /** Whichever list the current mode shows. Tabs, groups and search all derive from this. */
    private readonly activeEntries = computed<readonly LibraryEntry[]>(() =>
        this.isBrowsing() ? this.browseEntries() : this.shelfEntries());

    /** Search-filtered flat list across ALL entries of the current mode (search ignores tab). Wildcard = substring over name, description, function (category + tags). */
    readonly searchResults = computed<readonly LibraryEntry[]>(() => {
        const needle = this.searchText().trim().toLowerCase();
        if (!needle) return [];
        return this.activeEntries()
            .filter(e =>
                e.title.toLowerCase().includes(needle)
                || (e.description?.toLowerCase().includes(needle) ?? false)
                || (e.tags?.toLowerCase().includes(needle) ?? false)
                || getCategoryMeta(e.category).label.toLowerCase().includes(needle)
            )
            .slice()
            .sort((a, b) => a.sortOrder - b.sortOrder);
    });

    /** Search active flag — replaces tab content when true. */
    readonly isSearching = computed(() => this.searchText().trim().length > 0);

    /** Counts per tab (for badges). 'all' = total in the current mode. */
    readonly tabCounts = computed<ReadonlyMap<CategoryTab, number>>(() => {
        const counts = new Map<CategoryTab, number>();
        const all = this.activeEntries();
        counts.set('all', all.length);
        for (const e of all) {
            const key = e.category ?? '__other__';
            counts.set(key, (counts.get(key) ?? 0) + 1);
        }
        return counts;
    });

    /** How many library entries are not yet on the shelf — the Browse control's badge. */
    readonly addableCount = computed(() => this.browseEntries().filter(e => !e.onShelf).length);

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

    /** Recents row, derived from recentIds + the shelf (never Browse — you run from the shelf). */
    readonly recentEntries = computed<readonly LibraryEntry[]>(() => {
        const ids = this.recentIds();
        if (ids.length === 0 || this.isBrowsing()) return [];
        const byId = new Map(this.shelfEntries().map(e => [e.id, e] as const));
        return ids
            .map(id => byId.get(id))
            .filter((e): e is LibraryEntry => e !== undefined);
    });

    /** Entries within the currently-selected tab (ignores search). */
    readonly tabEntries = computed<readonly LibraryEntry[]>(() => {
        const tab = this.selectedTab();
        const all = this.activeEntries();
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

    /**
     * Deep-link target from `?tab=`, e.g. the Scheduling Checklist sending a director straight
     * to Schedules. Read once from the snapshot — this page is not re-entered without a fresh
     * construction, and a subscription would fight the user's own tab clicks.
     *
     * Held rather than applied here: the tab strip only shows categories with entries, and the
     * catalogue has not loaded at construction. It is applied in loadCatalogue once the counts
     * are real.
     */
    private pendingTab: CategoryTab | null = (() => {
        const requested = this.route.snapshot.queryParamMap.get('tab');
        return requested && REPORT_CATEGORIES.some(c => c.code === requested)
            ? requested as CategoryTab
            : null;
    })();

    ngOnInit(): void {
        this.loadCatalogue();
        this.recentIds.set(this.readRecentsFromStorage());
    }

    retryCatalogue(): void {
        this.loadCatalogue();
    }

    retryLibrary(): void {
        this.loadLibrary();
    }

    onSearchInput(value: string): void {
        this.searchText.set(value);
    }

    clearSearch(): void {
        this.searchText.set('');
    }

    selectTab(tab: CategoryTab): void {
        // A click beats the ?tab= deep link, even if the catalogue is still loading.
        this.pendingTab = null;
        this.selectedTab.set(tab);
        // Clear search so the user sees the chosen tab's content, not stale results.
        if (this.searchText()) this.searchText.set('');
    }

    /** Switch between the shelf and the library. The library loads on first visit only; adds keep it current after that. */
    setMode(mode: LibraryMode): void {
        if (this.mode() === mode) return;
        this.mode.set(mode);
        this.selectedTab.set('all');
        if (this.searchText()) this.searchText.set('');
        if (mode === 'browse' && !this.libraryLoaded() && !this.libraryLoading()) {
            this.loadLibrary();
        }
    }

    /** Superuser: flip between the SU shelf and the read-only all-roles union. */
    toggleAllRoles(): void {
        if (!this.isSuperuser()) return;
        this.showAllRoles.set(!this.showAllRoles());
        this.loadCatalogue();
    }

    categoryMeta(code: string | null | undefined): ReportCategoryMeta {
        return getCategoryMeta(code);
    }

    /** Short label for a non-job-scoped library entry, so a reader sees what "cross-job" means before adding it. */
    scopeLabel(scope: string | undefined): string | null {
        if (scope === 'CrossJob') return 'All jobs of this customer';
        if (scope === 'CrossWebsite') return 'Across the whole site';
        return null;
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

        // Dispatch on endpointPath (set for Crystal AND born-native DB entries) rather
        // than isCrystal, which is now purely the badge/tint semantic.
        const download$ = entry.boldReportName
            ? this.reportingService.downloadReport('export-bold', { reportName: entry.boldReportName })
            : entry.endpointPath
                ? this.reportingService.downloadReport(entry.endpointPath)
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

    // ── Add / Remove — the caller's own shelf only ────────────────────────────

    /** Browse: stock the shelf with this library entry. The server re-checks every gate. */
    addEntry(entry: LibraryEntry): void {
        if (!entry.reportLibraryId || entry.onShelf || this.busyId()) return;
        this.addToShelf(entry.reportLibraryId, entry.id, entry.title);
    }

    /** Undo banner: the removed row goes back on the shelf as a fresh add. */
    undoRemove(): void {
        const removed = this.lastRemoved();
        if (!removed?.reportLibraryId || this.busyId()) return;
        this.addToShelf(removed.reportLibraryId, `lib-${removed.reportLibraryId}`, removed.title);
    }

    dismissUndo(): void {
        this.lastRemoved.set(null);
    }

    private addToShelf(reportLibraryId: string, busyId: string, title: string): void {
        this.busyId.set(busyId);
        this.reportingService.addLibraryReportToShelf(reportLibraryId).subscribe({
            next: row => {
                this.busyId.set(null);
                this.type2Entries.set([...this.type2Entries(), row]);
                this.markOnShelf(reportLibraryId, row.jobReportId);
                if (this.lastRemoved()?.reportLibraryId === reportLibraryId) this.lastRemoved.set(null);
                this.toast.show(`${title} added to your shelf`, 'success');
            },
            error: err => {
                this.busyId.set(null);
                if (err?.status === 409) {
                    // Already there (a second tab, or a race). Reconcile rather than argue.
                    this.toast.show(`${title} is already on your shelf`, 'info');
                    this.loadCatalogue();
                    this.loadLibrary();
                    return;
                }
                const msg = err?.status === 403 ? `You are not permitted to add ${title} to this shelf.`
                    : err?.status === 404 ? `${title} is no longer in the library.`
                    : `Could not add ${title}. Please try again.`;
                this.toast.show(msg, 'danger');
            }
        });
    }

    /** Shelf: delete this row. Reversible from the Undo banner (it is an add again), so no confirm dialog. */
    removeEntry(entry: LibraryEntry): void {
        if (!entry.jobReportId || this.isUnionView() || this.busyId()) return;
        const jobReportId = entry.jobReportId;
        this.busyId.set(entry.id);
        this.reportingService.removeFromShelf(jobReportId).subscribe({
            next: () => {
                this.busyId.set(null);
                this.type2Entries.set(this.type2Entries().filter(r => r.jobReportId !== jobReportId));
                this.recentIds.set(this.recentIds().filter(id => id !== entry.id));
                if (entry.reportLibraryId) this.markOnShelf(entry.reportLibraryId, null);
                this.lastRemoved.set({ title: entry.title, reportLibraryId: entry.reportLibraryId ?? null });
            },
            error: err => {
                this.busyId.set(null);
                const msg = err?.status === 404 ? `${entry.title} is not on your shelf any more.`
                    : `Could not remove ${entry.title}. Please try again.`;
                this.toast.show(msg, 'danger');
                if (err?.status === 404) this.loadCatalogue();
            }
        });
    }

    /** Keep the Browse list honest after an add / remove without a round trip. New array — never mutate a signal's value. */
    private markOnShelf(reportLibraryId: string, shelfJobReportId: string | null): void {
        this.libraryEntries.set(this.libraryEntries().map(l =>
            l.reportLibraryId === reportLibraryId
                ? { ...l, shelfJobReportId, onShelf: shelfJobReportId !== null }
                : l));
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

        this.reportingService.getCatalogue(this.isUnionView()).subscribe({
            next: rows => {
                this.type2Entries.set(rows);
                this.catalogueLoading.set(false);
                this.applyPendingTab();
            },
            error: () => {
                this.catalogueLoading.set(false);
                this.catalogueError.set('Could not load your reports. Please retry.');
                this.applyPendingTab();
            }
        });
    }

    private loadLibrary(): void {
        this.libraryLoading.set(true);
        this.libraryError.set(null);

        this.reportingService.getLibrary().subscribe({
            next: rows => {
                this.libraryEntries.set(rows);
                this.libraryLoading.set(false);
                this.libraryLoaded.set(true);
            },
            error: () => {
                this.libraryLoading.set(false);
                this.libraryError.set('Could not load the report library. Please retry.');
            }
        });
    }

    /**
     * Honour `?tab=` once the counts are real, and only if that tab actually has reports.
     * Selecting an empty category would light nothing in the strip; "All" is the honest
     * fallback. Consumes the deep link either way, so a retry after a catalogue error cannot
     * yank the user off a tab they picked in the meantime.
     */
    private applyPendingTab(): void {
        const tab = this.pendingTab;
        this.pendingTab = null;

        if (tab && (this.tabCounts().get(tab) ?? 0) > 0) {
            this.selectedTab.set(tab);
        }
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
