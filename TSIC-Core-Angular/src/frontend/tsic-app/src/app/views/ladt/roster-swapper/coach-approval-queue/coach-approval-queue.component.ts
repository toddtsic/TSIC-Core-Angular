import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { forkJoin } from 'rxjs';
import { ToastService } from '@shared-ui/toast.service';
import {
    RosterSwapperService,
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
    /** registrationId of a coach being bulk-approved. */
    readonly busyCoach = signal<string | null>(null);

    readonly hasRows = computed(() => this.queue().length > 0);

    ngOnInit(): void {
        this.load();
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

    /** Drop one pending team from a coach immutably; remove the coach when none remain. */
    private removeLine(registrationId: string, teamId: string): void {
        this.queue.set(
            this.queue()
                .map(r => r.registrationId === registrationId
                    ? { ...r, pendingTeams: r.pendingTeams.filter(t => t.teamId !== teamId) }
                    : r)
                .filter(r => r.pendingTeams.length > 0)
        );
    }

    private removeRow(registrationId: string): void {
        this.queue.set(this.queue().filter(r => r.registrationId !== registrationId));
    }
}
