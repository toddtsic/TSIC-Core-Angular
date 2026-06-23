import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { forkJoin } from 'rxjs';
import { MultiSelectModule, CheckBoxSelectionService } from '@syncfusion/ej2-angular-dropdowns';
import type { MultiSelectChangeEventArgs } from '@syncfusion/ej2-angular-dropdowns';
import { ToastService } from '@shared-ui/toast.service';
import { InfoTooltipComponent } from '@shared-ui/components/info-tooltip.component';
import {
    RosterSwapperService,
    SwapperPoolOptionDto,
    UnassignedAdultQueueRowDto
} from '../services/roster-swapper.service';

/**
 * Director approval queue / team editor for coach (UnassignedAdult) registrations.
 *
 * Each coach is a single UnassignedAdult registration that can be placed on MANY teams — each
 * placement is a distinct Staff registration (Roster Swapper FLOW 2); the UnassignedAdult row
 * remains. The screen shows INTENT vs REALITY side by side:
 *
 * - Recorded teams = the coach's append-only JSON record (their own asks ★self ∪ director
 *   grants), immutable history.
 * - Assigned teams = live Staff rows = what's granted RIGHT NOW = the checked boxes.
 *
 * Every coach gets ONE checkbox dropdown of all teams, pre-checked with current assignments.
 * Checking a team grants it (mints Staff + appends to the record as admin); un-checking deletes
 * just that Staff row (the record entry stays). Deny removes ALL the coach's assignments and
 * deactivates them (bActive=0) — the only way a coach leaves the queue; nothing auto-retires.
 * After each change the queue is silently re-fetched so state stays truthful.
 */
@Component({
    selector: 'app-coach-approval-queue',
    standalone: true,
    imports: [CommonModule, MultiSelectModule, InfoTooltipComponent],
    providers: [CheckBoxSelectionService],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './coach-approval-queue.component.html',
    styleUrl: './coach-approval-queue.component.scss'
})
export class CoachApprovalQueueComponent implements OnInit {
    private readonly swapperService = inject(RosterSwapperService);
    private readonly toast = inject(ToastService);

    readonly queue = signal<UnassignedAdultQueueRowDto[]>([]);
    readonly isLoading = signal(false);
    /** Team pool options for the dropdown (Unassigned Adults pool excluded). */
    readonly teamPools = signal<SwapperPoolOptionDto[]>([]);
    /** registrationId of a coach whose assignments are being mutated. */
    readonly busyCoach = signal<string | null>(null);
    /** registrationIds whose "Coached before" accordion is expanded (collapsed by default). */
    readonly expandedPrior = signal<ReadonlySet<string>>(new Set());
    /** registrationId awaiting a second click to confirm Deny (guards the destructive action). */
    readonly confirmingDeny = signal<string | null>(null);

    readonly hasRows = computed(() => this.queue().length > 0);

    /** Syncfusion MultiSelect config — groupBy renders agegroup headers. */
    readonly teamFields = { text: 'label', value: 'teamId', groupBy: 'agegroup' };

    /** Flat team list shaped for the checkbox dropdown; label carries club + agegroup so the
     *  default filter matches club, agegroup, and team-name typeahead. */
    readonly teamsDataSource = computed(() =>
        this.teamPools()
            .map(p => ({
                teamId: p.poolId,
                agegroup: p.agegroupName ?? 'Other',
                label: p.agegroupName ? `${p.poolName} · ${p.agegroupName}` : p.poolName,
            }))
            .sort((a, b) => a.label.localeCompare(b.label)),
    );

    ngOnInit(): void {
        this.loadPools();
        this.load();
    }

    private loadPools(): void {
        this.swapperService.getPoolOptions().subscribe({
            next: pools => this.teamPools.set(pools.filter(p => !p.isUnassignedAdultsPool)),
            // Non-fatal: the queue still loads; the dropdown just won't have options.
            error: () => { /* ignore */ }
        });
    }

    load(silent = false): void {
        if (!silent) this.isLoading.set(true);
        this.swapperService.getUnassignedQueue().subscribe({
            next: rows => {
                this.queue.set(rows);
                this.isLoading.set(false);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to load the coach approval queue.', 'danger', 4000);
                this.isLoading.set(false);
            }
        });
    }

    contactLine(row: UnassignedAdultQueueRowDto): string {
        const cityState = [row.city, row.state].filter(Boolean).join(', ');
        return [row.email, row.cellphone, cityState].filter(Boolean).join(' · ');
    }

    /** Team ids the coach is currently assigned to — pre-checks the dropdown. */
    assignedTeamIds(row: UnassignedAdultQueueRowDto): string[] {
        return row.assignedTeams.map(t => t.teamId);
    }

    /** True when a recorded team is currently granted (a live Staff row exists for it). */
    isGranted(row: UnassignedAdultQueueRowDto, teamId: string): boolean {
        return row.assignedTeams.some(t => t.teamId === teamId);
    }

    togglePrior(registrationId: string): void {
        const next = new Set(this.expandedPrior());
        if (next.has(registrationId)) {
            next.delete(registrationId);
        } else {
            next.add(registrationId);
        }
        this.expandedPrior.set(next);
    }

    isPriorExpanded(registrationId: string): boolean {
        return this.expandedPrior().has(registrationId);
    }

    /**
     * Fires once when the dropdown closes (changeOnBlur), carrying the final checked set — the
     * coach's desired team assignments. Diff it against the current assignments and commit in
     * one batch: newly-checked teams are placed (FLOW 2), newly-unchecked teams are removed
     * (FLOW 3). Ignores non-interactive value resets (e.g. our own silent re-fetch).
     */
    onTeamsChange(row: UnassignedAdultQueueRowDto, e: MultiSelectChangeEventArgs): void {
        if (!e.isInteracted) return;
        const newIds = Array.isArray(e.value) ? (e.value as string[]) : [];
        const current = this.assignedTeamIds(row);

        const added = newIds.filter(id => !current.includes(id));
        const removed = current.filter(id => !newIds.includes(id));
        if (added.length === 0 && removed.length === 0) return;

        const ops = [
            ...added.map(id => this.swapperService.approveRequest(row.registrationId, id)),
            ...removed.map(id => {
                const staffRegId = row.assignedTeams.find(t => t.teamId === id)?.staffRegistrationId;
                return this.swapperService.removeStaffFromTeam(staffRegId!, id);
            }),
        ];

        this.busyCoach.set(row.registrationId);
        forkJoin(ops).subscribe({
            next: () => {
                const parts = [
                    added.length ? `placed on ${added.length} team(s)` : '',
                    removed.length ? `removed from ${removed.length} team(s)` : '',
                ].filter(Boolean).join(', ');
                this.toast.show(`${row.playerName} ${parts}.`, 'success', 3000);
                // Re-fetch silently so assignments + Staff ids stay truthful (e.g. dup-skips).
                this.busyCoach.set(null);
                this.load(true);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Could not update this coach — refreshing.', 'danger', 4000);
                this.busyCoach.set(null);
                this.load(true);
            }
        });
    }

    /** First Deny click arms the confirm; a second confirms. Cancel disarms. */
    requestDeny(row: UnassignedAdultQueueRowDto): void {
        this.confirmingDeny.set(row.registrationId);
    }

    cancelDeny(): void {
        this.confirmingDeny.set(null);
    }

    /** Deny the coach: deletes ALL their Staff rows + deactivates them (bActive=0). Destructive. */
    confirmDeny(row: UnassignedAdultQueueRowDto): void {
        if (this.busyCoach() === row.registrationId) return;
        this.confirmingDeny.set(null);
        this.busyCoach.set(row.registrationId);
        this.swapperService.denyCoach(row.registrationId).subscribe({
            next: () => {
                this.queue.set(this.queue().filter(r => r.registrationId !== row.registrationId));
                this.toast.show(`${row.playerName} denied — removed from all teams and deactivated.`, 'info', 3500);
                this.busyCoach.set(null);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Could not deny this coach.', 'danger', 4000);
                this.busyCoach.set(null);
            }
        });
    }
}
