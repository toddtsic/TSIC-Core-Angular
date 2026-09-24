import { Component, ChangeDetectionStrategy, inject, signal, computed, viewChild, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { GridAllModule, GridComponent, ToolbarItems } from '@syncfusion/ej2-angular-grids';
import type { DirectorClubRepDto, DirectorClubLibraryDto, DirectorClubLibraryTeamDto, ClubTeamEventHistoryDto } from '@core/api';
import { TeamSearchService } from '../teams/services/team-search.service';
import { JobService } from '@infrastructure/services/job.service';
import { ResizablePanelDirective } from '@shared-ui/directives/resizable-panel.directive';
import { formatLop } from '@shared/teams/lop-choices';
import { collapseHistoryPerEvent } from '@shared/teams/club-team-history';

/** Quick filters over the rep list. Each answers one director question. */
type RepFilter = 'all' | 'zero' | 'unregistered' | 'owing';

/**
 * Director's club-rep directory (CTL Phase 4). Registration-driven, so a rep who signed in
 * and registered nothing is a row here — Search Teams is team-centric and can never show
 * them. Each row carries the rep's Club Team Library counts; the Library panel opens a
 * READ-ONLY view of that club's library with this event's status per team and the team's
 * other events. Directors never edit a club's library from here (ruling: Todd 2026-09-22).
 */
@Component({
    selector: 'app-director-club-reps',
    standalone: true,
    imports: [CommonModule, RouterLink, GridAllModule, ResizablePanelDirective],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './director-club-reps.component.html',
    styleUrls: ['./director-club-reps.component.scss'],
})
export class DirectorClubRepsComponent {
    private readonly searchService = inject(TeamSearchService);
    private readonly jobService = inject(JobService);

    readonly jobName = computed(() => this.jobService.currentJob()?.jobName ?? '');

    readonly isLoading = signal(false);
    readonly errorMessage = signal('');
    readonly rows = signal<DirectorClubRepDto[]>([]);
    readonly filter = signal<RepFilter>('all');

    readonly filteredRows = computed(() => {
        const rows = this.rows();
        switch (this.filter()) {
            case 'zero': return rows.filter(r => r.activeTeamCount + r.waitlistedTeamCount === 0);
            case 'unregistered': return rows.filter(r => r.libraryEligibleUnregisteredCount > 0);
            case 'owing': return rows.filter(r => r.owedTotal > 0);
            default: return rows;
        }
    });

    readonly totals = computed(() => {
        const rows = this.rows();
        return {
            reps: rows.length,
            zero: rows.filter(r => r.activeTeamCount + r.waitlistedTeamCount === 0).length,
            active: rows.reduce((s, r) => s + r.activeTeamCount, 0),
            waitlisted: rows.reduce((s, r) => s + r.waitlistedTeamCount, 0),
            unregistered: rows.reduce((s, r) => s + r.libraryUnregisteredCount, 0),
            eligible: rows.reduce((s, r) => s + r.libraryEligibleUnregisteredCount, 0),
            owingReps: rows.filter(r => r.owedTotal > 0).length,
            owed: rows.reduce((s, r) => s + r.owedTotal, 0),
        };
    });

    readonly toolbar: ToolbarItems[] = ['ExcelExport'];
    readonly grid = viewChild.required<GridComponent>('grid');

    // ── Library panel ──
    readonly isPanelOpen = signal(false);
    readonly panelLoading = signal(false);
    readonly panelError = signal('');
    readonly panelRep = signal<DirectorClubRepDto | null>(null);
    readonly library = signal<DirectorClubLibraryDto | null>(null);
    readonly expandedHistory = signal<ReadonlySet<number>>(new Set());
    readonly historyPreviewCount = 3;

    readonly libraryCounts = computed(() => {
        const teams = this.library()?.teams ?? [];
        const count = (s: string) => teams.filter(t => t.eventStatus === s).length;
        return {
            total: teams.filter(t => !t.archived).length,
            registered: count('registered'),
            waitlisted: count('waitlisted'),
            available: count('available'),
            outside: count('outside'),
            dropped: count('dropped'),
            archived: count('archived'),
        };
    });

    constructor() {
        this.load();
    }

    load(): void {
        this.isLoading.set(true);
        this.errorMessage.set('');
        this.searchService.getClubReps().subscribe({
            next: rows => {
                this.rows.set(rows);
                this.isLoading.set(false);
            },
            error: err => {
                this.isLoading.set(false);
                if (err.status === 401) {
                    this.errorMessage.set('You must be logged in to view this screen.');
                } else if (err.status === 403) {
                    this.errorMessage.set('You do not have permission to view this event\'s club reps.');
                } else {
                    this.errorMessage.set(err.error?.message || 'Failed to load club reps.');
                }
            },
        });
    }

    setFilter(f: RepFilter): void {
        this.filter.set(f);
    }

    onToolbarClick(args: { item?: { id?: string } }): void {
        if (args.item?.id?.includes('excelexport')) {
            // NEVER pass `columns` here — see the Syncfusion export trap note.
            this.grid().excelExport();
        }
    }

    openLibrary(rep: DirectorClubRepDto): void {
        this.panelRep.set(rep);
        this.library.set(null);
        this.panelError.set('');
        this.expandedHistory.set(new Set());
        this.isPanelOpen.set(true);
        this.panelLoading.set(true);
        this.searchService.getClubRepLibrary(rep.registrationId).subscribe({
            next: lib => {
                // One chip per event, same collapse as the rep's library page.
                this.library.set(lib ? { ...lib, teams: lib.teams.map(t => ({ ...t, otherEvents: collapseHistoryPerEvent(t.otherEvents) })) } : lib);
                this.panelLoading.set(false);
            },
            error: err => {
                this.panelLoading.set(false);
                this.panelError.set(err.error?.message || 'Failed to load this club\'s library.');
            },
        });
    }

    closePanel(): void {
        this.isPanelOpen.set(false);
        this.panelRep.set(null);
        this.library.set(null);
    }

    @HostListener('document:keydown.escape')
    onEscape(): void {
        if (this.isPanelOpen()) this.closePanel();
    }

    toggleHistory(clubTeamId: number): void {
        const next = new Set(this.expandedHistory());
        if (next.has(clubTeamId)) next.delete(clubTeamId); else next.add(clubTeamId);
        this.expandedHistory.set(next);
    }

    formatLop = formatLop;

    /**
     * Short event names two different jobs share across this club's history ("Summer 2027" at
     * LFTC and at Lax By The Sea). Those chips get the organizer back; same rule as the rep's page.
     */
    private readonly ambiguousEventTails = computed<ReadonlySet<string>>(() => {
        const jobsByTail = new Map<string, Set<string>>();
        for (const team of this.library()?.teams ?? []) {
            for (const h of team.otherEvents) {
                const tail = this.eventTail(h.jobName).toLowerCase();
                const jobs = jobsByTail.get(tail) ?? new Set<string>();
                jobs.add(h.jobId);
                jobsByTail.set(tail, jobs);
            }
        }
        const out = new Set<string>();
        for (const [tail, jobs] of jobsByTail) if (jobs.size > 1) out.add(tail);
        return out;
    });

    /** "Summer 2027", or "LFTC Summer 2027" when another organizer ran a "Summer 2027" too. */
    eventLabel(h: ClubTeamEventHistoryDto): string {
        const tail = this.eventTail(h.jobName);
        if (!this.ambiguousEventTails().has(tail.toLowerCase())) return tail;
        const idx = h.jobName.indexOf(':');
        const org = idx > 0 ? h.jobName.substring(0, idx).trim() : '';
        return org ? `${org} ${tail}` : tail;
    }

    private eventTail(jobName: string): string {
        const idx = jobName.indexOf(':');
        return idx > 0 ? jobName.substring(idx + 1).trim() : jobName;
    }

    /**
     * The name a team carries at an event, ONLY when it differs from its library name. Older
     * events stored "{Club} {LibraryName}", so that form is suppressed too, same as the rep's page.
     */
    eventAlias(t: DirectorClubLibraryTeamDto, eventTeamName: string | null | undefined): string | null {
        const alias = (eventTeamName ?? '').trim();
        if (!alias) return null;
        const norm = (s: string) => s.replace(/\s+/g, ' ').trim().toLowerCase();
        const lib = norm(t.clubTeamName ?? '');
        if (norm(alias) === lib) return null;
        const club = this.library()?.clubName ?? this.panelRep()?.clubName ?? '';
        if (norm(alias) === norm(`${club} ${t.clubTeamName ?? ''}`)) return null;
        return alias;
    }

    trackTeam(_: number, t: DirectorClubLibraryTeamDto): number {
        return t.clubTeamId;
    }
}
