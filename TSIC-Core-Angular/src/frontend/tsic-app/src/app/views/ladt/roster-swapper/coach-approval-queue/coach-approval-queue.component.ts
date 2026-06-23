import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { forkJoin } from 'rxjs';
import { ToastService } from '@shared-ui/toast.service';
import {
    RosterSwapperService,
    SwapperPoolOptionDto,
    UnassignedAdultQueueRowDto
} from '../services/roster-swapper.service';
import type { UnassignedAdultRequestDto } from '@core/api';

/**
 * Director approval queue for coach (UnassignedAdult) team requests.
 *
 * Each coach is a single UnassignedAdult registration carrying 1-to-many non-binding
 * team REQUESTS. The director reviews recognition context (prior Staff history is the
 * lead signal; family linkage is secondary) and Approves/Denies each requested team.
 * Approve mints the per-team Staff row via the Roster Swapper FLOW-2 path; the
 * UnassignedAdult row remains as the source of further acceptances. Deny just drops the
 * team from the coach's request list. A coach disappears from the queue once they have
 * no pending requests left.
 *
 * Coaches who registered with only a LEGACY FREE-TEXT request (no structured team
 * selection) arrive with no pending teams — their row instead offers a team picker so the
 * director can MANUALLY place them on a team (same FLOW-2 approve path). They retire from
 * the queue once placed.
 */
@Component({
    selector: 'app-coach-approval-queue',
    standalone: true,
    imports: [CommonModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './coach-approval-queue.component.html',
    styleUrl: './coach-approval-queue.component.scss'
})
export class CoachApprovalQueueComponent implements OnInit {
    private readonly swapperService = inject(RosterSwapperService);
    private readonly toast = inject(ToastService);

    readonly queue = signal<UnassignedAdultQueueRowDto[]>([]);
    readonly isLoading = signal(false);
    /** `${registrationId}:${teamId}` of a line being approved/denied. */
    readonly busyLine = signal<string | null>(null);
    /** registrationId of a coach being bulk-approved or manually placed. */
    readonly busyCoach = signal<string | null>(null);
    /** Team pool options for the manual-placement picker (Unassigned Adults pool excluded). */
    readonly teamPools = signal<SwapperPoolOptionDto[]>([]);
    /** registrationId → chosen teamId for placing a free-text coach on a team. */
    readonly placementChoice = signal<Record<string, string>>({});

    readonly hasRows = computed(() => this.queue().length > 0);

    ngOnInit(): void {
        this.loadPools();
        this.load();
    }

    private loadPools(): void {
        this.swapperService.getPoolOptions().subscribe({
            next: pools => this.teamPools.set(pools.filter(p => !p.isUnassignedAdultsPool)),
            // Non-fatal: the queue still loads; manual-placement rows just won't have options.
            error: () => { /* ignore */ }
        });
    }

    load(): void {
        this.isLoading.set(true);
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

    lineKey(row: UnassignedAdultQueueRowDto, team: UnassignedAdultRequestDto): string {
        return `${row.registrationId}:${team.teamId}`;
    }

    isLineBusy(row: UnassignedAdultQueueRowDto, team: UnassignedAdultRequestDto): boolean {
        return this.busyLine() === this.lineKey(row, team) || this.busyCoach() === row.registrationId;
    }

    contactLine(row: UnassignedAdultQueueRowDto): string {
        const cityState = [row.city, row.state].filter(Boolean).join(', ');
        return [row.email, row.cellphone, cityState].filter(Boolean).join(' · ');
    }

    approve(row: UnassignedAdultQueueRowDto, team: UnassignedAdultRequestDto): void {
        if (this.isLineBusy(row, team)) return;
        this.busyLine.set(this.lineKey(row, team));
        this.swapperService.approveRequest(row.registrationId, team.teamId).subscribe({
            next: result => {
                this.removeLine(row.registrationId, team.teamId);
                this.toast.show(`${row.playerName} approved for ${team.displayText}. ${result.message}`, 'success', 3000);
                this.busyLine.set(null);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Approval failed.', 'danger', 4000);
                this.busyLine.set(null);
            }
        });
    }

    deny(row: UnassignedAdultQueueRowDto, team: UnassignedAdultRequestDto): void {
        if (this.isLineBusy(row, team)) return;
        this.busyLine.set(this.lineKey(row, team));
        this.swapperService.denyRequest(row.registrationId, team.teamId).subscribe({
            next: () => {
                this.removeLine(row.registrationId, team.teamId);
                this.toast.show(`Denied ${row.playerName}'s request for ${team.displayText}.`, 'info', 3000);
                this.busyLine.set(null);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Could not deny the request.', 'danger', 4000);
                this.busyLine.set(null);
            }
        });
    }

    approveAll(row: UnassignedAdultQueueRowDto): void {
        if (this.busyCoach() === row.registrationId || row.pendingTeams.length === 0) return;
        this.busyCoach.set(row.registrationId);
        forkJoin(row.pendingTeams.map(t => this.swapperService.approveRequest(row.registrationId, t.teamId))).subscribe({
            next: () => {
                this.removeRow(row.registrationId);
                this.toast.show(`${row.playerName} approved for ${row.pendingTeams.length} team(s).`, 'success', 3000);
                this.busyCoach.set(null);
            },
            error: err => {
                // Partial success possible — reload to reflect the true state.
                this.toast.show(err?.error?.message || 'Some approvals failed — refreshing.', 'danger', 4000);
                this.busyCoach.set(null);
                this.load();
            }
        });
    }

    /** Record the director's team choice for a free-text coach awaiting manual placement. */
    chooseTeam(registrationId: string, teamId: string): void {
        this.placementChoice.set({ ...this.placementChoice(), [registrationId]: teamId });
    }

    /** Place a free-text coach (no requested team) on the chosen team — mints Staff via FLOW 2. */
    placeOnTeam(row: UnassignedAdultQueueRowDto): void {
        const teamId = this.placementChoice()[row.registrationId];
        if (!teamId || this.busyCoach() === row.registrationId) return;
        this.busyCoach.set(row.registrationId);
        this.swapperService.approveRequest(row.registrationId, teamId).subscribe({
            next: result => {
                this.removeRow(row.registrationId);
                const label = this.teamPools().find(p => p.poolId === teamId)?.poolName ?? 'the team';
                this.toast.show(`${row.playerName} placed on ${label}. ${result.message}`, 'success', 3000);
                this.busyCoach.set(null);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Could not place this coach.', 'danger', 4000);
                this.busyCoach.set(null);
            }
        });
    }

    /**
     * Drop one pending team from the acted-on coach immutably; remove that coach when none
     * remain. Only the matched coach is touched — free-text rows (empty pendingTeams) for
     * OTHER coaches are left in place.
     */
    private removeLine(registrationId: string, teamId: string): void {
        this.queue.set(
            this.queue().flatMap(r => {
                if (r.registrationId !== registrationId) return [r];
                const pendingTeams = r.pendingTeams.filter(t => t.teamId !== teamId);
                return pendingTeams.length > 0 ? [{ ...r, pendingTeams }] : [];
            })
        );
    }

    private removeRow(registrationId: string): void {
        this.queue.set(this.queue().filter(r => r.registrationId !== registrationId));
    }
}
