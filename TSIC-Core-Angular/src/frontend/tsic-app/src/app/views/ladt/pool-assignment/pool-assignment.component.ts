import { Component, inject, signal, computed, ChangeDetectionStrategy, ElementRef, Injector, afterNextRender, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ToastService } from '@shared-ui/toast.service';
import { JobService } from '@infrastructure/services/job.service';
import {
    PoolAssignmentService,
    PoolDivisionOptionDto,
    PoolTeamDto,
    PoolTransferPreviewResponse
} from './services/pool-assignment.service';
import { contrastText } from '../../scheduling/shared/utils/scheduling-helpers';
import { ChecklistBackLinkComponent } from '../../scheduling/shared/components/checklist-back-link/checklist-back-link.component';
import type { NationalRankingDataDto } from '@core/api';

interface AgegroupGroup {
    label: string;
    agegroupColor: string | null;
    divisions: PoolDivisionOptionDto[];
}

type SortDir = 'asc' | 'desc' | null;
type SortColumn = keyof PoolTeamDto | null;
type MoveDirection = 'source-to-target' | 'target-to-source';

/**
 * The swap conversation. A scheduled pool has frozen membership, so a move out of one is not a
 * move at all — it is a trade, and the director has to agree to that before being asked who comes
 * back. Two stages, in that order: the statement, then the question.
 */
interface SwapModalState {
    stage: 'statement' | 'picker';
    direction: MoveDirection;
    /** The team whose arrow was clicked — the one leaving. */
    movingTeam: PoolTeamDto;
    fromDivName: string;
    toDivName: string;
    /** Chosen in the picker stage; the team coming the other way. */
    counterTeam: PoolTeamDto | null;
}

@Component({
    selector: 'app-pool-assignment',
    standalone: true,
    imports: [CommonModule, FormsModule, RouterLink, ChecklistBackLinkComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './pool-assignment.component.html',
    styleUrl: './pool-assignment.component.scss'
})
export class PoolAssignmentComponent {
    private readonly poolService = inject(PoolAssignmentService);
    private readonly toast = inject(ToastService);
    private readonly jobService = inject(JobService);

    readonly isLacrosseJob = computed(() => !!this.jobService.currentJob()?.usLaxNumberValidThroughDate);

    // Division options
    readonly divisionOptions = signal<PoolDivisionOptionDto[]>([]);
    readonly groupedDivisionOptions = computed<AgegroupGroup[]>(() => this.groupByAgegroup(this.divisionOptions()));

    // Custom dropdown open state
    readonly sourceDropdownOpen = signal(false);
    readonly targetDropdownOpen = signal(false);

    // Dropdown max-height, computed on open to nearly fill the panel's visible area
    readonly sourceDropdownMaxHeight = signal<number | null>(null);
    readonly targetDropdownMaxHeight = signal<number | null>(null);

    // Source panel
    readonly sourceDivId = signal<string | null>(null);
    readonly sourceDiv = computed(() => this.divisionOptions().find(d => d.divId === this.sourceDivId()) ?? null);
    readonly sourceTeams = signal<PoolTeamDto[]>([]);
    readonly sourceSelected = signal<Set<string>>(new Set());
    readonly sourceFilter = signal('');
    readonly sourceSortCol = signal<SortColumn>(null);
    readonly sourceSortDir = signal<SortDir>(null);
    readonly sortedFilteredSourceTeams = computed(() =>
        this.sortTeams(this.filterTeams(this.sourceTeams(), this.sourceFilter()), this.sourceSortCol(), this.sourceSortDir()));

    // Target panel
    readonly targetDivId = signal<string | null>(null);
    readonly targetDiv = computed(() => this.divisionOptions().find(d => d.divId === this.targetDivId()) ?? null);
    readonly targetTeams = signal<PoolTeamDto[]>([]);
    readonly targetSelected = signal<Set<string>>(new Set());
    readonly targetFilter = signal('');
    readonly targetSortCol = signal<SortColumn>(null);
    readonly targetSortDir = signal<SortDir>(null);
    readonly sortedFilteredTargetTeams = computed(() =>
        this.sortTeams(this.filterTeams(this.targetTeams(), this.targetFilter()), this.targetSortCol(), this.targetSortDir()));

    // Transfer state
    readonly transferPreview = signal<PoolTransferPreviewResponse | null>(null);
    readonly transferDirection = signal<'source-to-target' | 'target-to-source'>('source-to-target');
    readonly isLoadingPreview = signal(false);
    readonly isTransferring = signal(false);
    readonly swappingId = signal<string | null>(null);
    // AM-053: teams moved by the LAST transfer (single, batch, and both sides of a
    // symmetrical swap). Panels silently reload after a transfer, so the highlight is
    // the director's only post-move trace — it persists until the next transfer or a
    // division change (deliberately no timed fade). Mirrors Roster Swapper AM-039.
    readonly justMovedIds = signal<ReadonlySet<string>>(new Set());

    // AM-053 re-open: the moved team lands at the BOTTOM of its new list — on a long
    // list the highlight sits off-screen and the director sees nothing move. After the
    // landing panel's reload renders, scroll the first moved row into view. One-shot:
    // queued by the transfer success handler, drained by that panel's loadTeams.
    private readonly injector = inject(Injector);
    private readonly sourceScroll = viewChild<ElementRef<HTMLElement>>('sourceScroll');
    private readonly targetScroll = viewChild<ElementRef<HTMLElement>>('targetScroll');
    private pendingScroll: { panel: 'source' | 'target'; teamId: string } | null = null;

    // A preview only comes back for a move the server already permits — the pool gate refuses
    // the rest outright — so there is nothing left to re-check here.
    readonly canConfirmTransfer = computed(() =>
        !!this.transferPreview() && !this.isTransferring());

    // ── Swap conversation + denial ──

    readonly swapModal = signal<SwapModalState | null>(null);
    readonly denyModal = signal<string | null>(null);

    /** The other pool's active teams, in rank order — the candidates to come back. */
    readonly counterTeamCandidates = computed(() => {
        const m = this.swapModal();
        if (!m) return [];
        const pool = m.direction === 'source-to-target' ? this.targetTeams() : this.sourceTeams();
        return pool.filter(t => t.active).sort((a, b) => a.divRank - b.divRank);
    });

    // DivRank inline editing
    readonly editingDivRankTeamId = signal<string | null>(null);
    readonly editingDivRankValue = signal<number>(0);

    // Rank options: 1..N where N = active teams in the panel
    readonly sourceRankOptions = computed(() =>
        Array.from({ length: this.sourceTeams().filter(t => t.active).length }, (_, i) => i + 1));
    readonly targetRankOptions = computed(() =>
        Array.from({ length: this.targetTeams().filter(t => t.active).length }, (_, i) => i + 1));

    // General
    readonly isLoading = signal(false);

    constructor() {
        this.loadDivisionOptions();
    }

    // ── Custom dropdown helpers ──

    toggleSourceDropdown(event: MouseEvent): void {
        const opening = !this.sourceDropdownOpen();
        if (opening) {
            this.sourceDropdownMaxHeight.set(this.computeDropdownMaxHeight(event.currentTarget as HTMLElement));
        }
        this.sourceDropdownOpen.set(opening);
    }

    toggleTargetDropdown(event: MouseEvent): void {
        const opening = !this.targetDropdownOpen();
        if (opening) {
            this.targetDropdownMaxHeight.set(this.computeDropdownMaxHeight(event.currentTarget as HTMLElement));
        }
        this.targetDropdownOpen.set(opening);
    }

    /** Space from just below the trigger to the bottom of the panel container, minus breathing room. */
    private computeDropdownMaxHeight(trigger: HTMLElement): number {
        const triggerBottom = trigger.getBoundingClientRect().bottom;
        const container = trigger.closest('.pool-assignment-container');
        const containerBottom = container
            ? container.getBoundingClientRect().bottom
            : window.innerHeight;
        return Math.max(160, Math.floor(containerBottom - triggerBottom - 12));
    }

    selectSourceDiv(divId: string): void {
        this.sourceDropdownOpen.set(false);
        this.onSourceDivChange(divId);
    }

    selectTargetDiv(divId: string): void {
        this.targetDropdownOpen.set(false);
        this.onTargetDivChange(divId);
    }

    closeDropdowns(): void {
        this.sourceDropdownOpen.set(false);
        this.targetDropdownOpen.set(false);
    }

    readonly contrastText = contrastText;

    // ── Division loading ──

    loadDivisionOptions() {
        this.isLoading.set(true);
        this.poolService.getDivisions().subscribe({
            next: divisions => {
                this.divisionOptions.set(divisions);
                this.isLoading.set(false);
                // Auto-select the first division as the source on initial load
                if (!this.sourceDivId() && divisions.length > 0) {
                    this.onSourceDivChange(divisions[0].divId);
                }
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to load division options.', 'danger', 4000);
                this.isLoading.set(false);
            }
        });
    }

    onSourceDivChange(divId: string) {
        this.sourceDivId.set(divId);
        this.justMovedIds.set(new Set());
        this.sourceSelected.set(new Set());
        this.sourceFilter.set('');
        this.sourceSortCol.set(null);
        this.sourceSortDir.set(null);
        this.cancelTransferPreview();
        if (!divId) {
            this.sourceTeams.set([]);
            return;
        }
        this.loadTeams('source', divId);
    }

    onTargetDivChange(divId: string) {
        this.targetDivId.set(divId);
        this.justMovedIds.set(new Set());
        this.targetSelected.set(new Set());
        this.targetFilter.set('');
        this.targetSortCol.set(null);
        this.targetSortDir.set(null);
        this.cancelTransferPreview();
        if (!divId) {
            this.targetTeams.set([]);
            return;
        }
        this.loadTeams('target', divId);
    }

    private loadTeams(panel: 'source' | 'target', divId: string) {
        this.poolService.getTeams(divId).subscribe({
            next: teams => {
                if (panel === 'source') this.sourceTeams.set(teams);
                else this.targetTeams.set(teams);
                // AM-053: this reload carries the just-moved team — scroll it into
                // view once these rows have rendered. Cleared before scheduling, so
                // it fires exactly once; a row hidden by an active filter no-ops.
                if (this.pendingScroll?.panel === panel) {
                    const pending = this.pendingScroll;
                    this.pendingScroll = null;
                    afterNextRender(() => this.scrollToMovedRow(pending.panel, pending.teamId), { injector: this.injector });
                }
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to load teams.', 'danger', 4000);
            }
        });
    }

    // ── Sorting ──

    onSort(panel: 'source' | 'target', col: SortColumn) {
        const currentCol = panel === 'source' ? this.sourceSortCol() : this.targetSortCol();
        const currentDir = panel === 'source' ? this.sourceSortDir() : this.targetSortDir();

        let newDir: SortDir;
        if (currentCol !== col) newDir = 'asc';
        else if (currentDir === 'asc') newDir = 'desc';
        else newDir = null;

        if (panel === 'source') {
            this.sourceSortCol.set(newDir ? col : null);
            this.sourceSortDir.set(newDir);
        } else {
            this.targetSortCol.set(newDir ? col : null);
            this.targetSortDir.set(newDir);
        }
    }

    private sortTeams(teams: PoolTeamDto[], col: SortColumn, dir: SortDir): PoolTeamDto[] {
        if (!col || !dir) return teams;
        const mult = dir === 'asc' ? 1 : -1;

        // Special handling for nationalRankingData — sort by parsed rank number
        if (col === 'nationalRankingData') {
            return [...teams].sort((a, b) => {
                const aRank = this.getNationalRank(a);
                const bRank = this.getNationalRank(b);
                if (aRank == null && bRank == null) return 0;
                if (aRank == null) return 1;  // unranked always last
                if (bRank == null) return -1;
                return (aRank - bRank) * mult;
            });
        }

        return [...teams].sort((a, b) => {
            const aVal = a[col];
            const bVal = b[col];
            if (aVal == null && bVal == null) return 0;
            if (aVal == null) return 1;
            if (bVal == null) return -1;
            if (typeof aVal === 'string' && typeof bVal === 'string') return aVal.localeCompare(bVal) * mult;
            if (typeof aVal === 'number' && typeof bVal === 'number') return (aVal - bVal) * mult;
            if (typeof aVal === 'boolean' && typeof bVal === 'boolean') return ((aVal ? 1 : 0) - (bVal ? 1 : 0)) * mult;
            return String(aVal).localeCompare(String(bVal)) * mult;
        });
    }

    // ── Selection ──

    toggleSourceSelect(teamId: string) {
        const current = new Set(this.sourceSelected());
        if (current.has(teamId)) current.delete(teamId);
        else current.add(teamId);
        this.sourceSelected.set(current);
    }

    toggleTargetSelect(teamId: string) {
        const current = new Set(this.targetSelected());
        if (current.has(teamId)) current.delete(teamId);
        else current.add(teamId);
        this.targetSelected.set(current);
    }

    selectAllSource() {
        this.sourceSelected.set(new Set(this.sortedFilteredSourceTeams().map(t => t.teamId)));
    }

    deselectAllSource() {
        this.sourceSelected.set(new Set());
    }

    selectAllTarget() {
        this.targetSelected.set(new Set(this.sortedFilteredTargetTeams().map(t => t.teamId)));
    }

    deselectAllTarget() {
        this.targetSelected.set(new Set());
    }

    // ── Single-row swap ──

    swapToTarget(team: PoolTeamDto) {
        if (!this.targetDivId() || !this.sourceDivId()) {
            this.toast.show('Select a target division first.', 'warning');
            return;
        }
        this.beginMove(team, 'source-to-target');
    }

    swapToSource(team: PoolTeamDto) {
        if (!this.sourceDivId() || !this.targetDivId()) {
            this.toast.show('Select a source division first.', 'warning');
            return;
        }
        this.beginMove(team, 'target-to-source');
    }

    /**
     * The arrow's decision, made against POOL state rather than the clicked team's own game rows.
     * Three outcomes: move it (nothing scheduled), refuse it and name the pool to break down, or
     * open the swap conversation (both pools scheduled, equal active size).
     *
     * Deliberately NOT keyed on `team.isScheduled`. A pool's matrix is built for N ranks and does
     * not care which team's id is written where — so a team with no game rows sitting in a
     * scheduled pool is just as frozen as the rest, and the old per-team check let it walk.
     */
    private beginMove(team: PoolTeamDto, direction: MoveDirection) {
        const from = direction === 'source-to-target' ? this.sourceDiv() : this.targetDiv();
        const to = direction === 'source-to-target' ? this.targetDiv() : this.sourceDiv();
        if (!from || !to) return;

        // Neither pool has a board — nothing to protect. Commits on click, as it always has.
        if (!from.isScheduled && !to.isScheduled) {
            this.swappingId.set(team.teamId);
            this.executeTransferDirect([team.teamId], [], from.divId, to.divId, false, team.teamName);
            return;
        }

        const denial = this.denialFor(from, to, 1, 1);
        if (denial) {
            this.denyModal.set(denial);
            return;
        }

        // Both scheduled, equal active size: a trade is possible. State that before asking who.
        this.swapModal.set({
            stage: 'statement',
            direction,
            movingTeam: team,
            fromDivName: from.divName,
            toDivName: to.divName,
            counterTeam: null
        });
    }

    /**
     * Mirrors `EnsurePoolMovementAllowedAsync` on the server, so the screen explains the refusal
     * instead of relaying a red toast. The server remains the enforcement — this is the wording.
     * Returns null when the movement is permitted.
     */
    private denialFor(
        from: PoolDivisionOptionDto, to: PoolDivisionOptionDto,
        teamsOut: number, teamsBack: number): string | null {

        if (!from.isScheduled && !to.isScheduled) return null;

        if (from.isScheduled && !to.isScheduled)
            return `${from.divName} is scheduled. A team cannot leave a scheduled pool — its rank `
                + `is a slot in the pairing matrix, and emptying it leaves those games with no team `
                + `to play them. Break down ${from.divName}'s schedule first, then move the team.`;

        if (!from.isScheduled && to.isScheduled)
            return `${to.divName} is scheduled. A team cannot be added to a scheduled pool — the `
                + `pairing matrix was built without it, so it would sit in ${to.divName} with no `
                + `games. Break down ${to.divName}'s schedule first, then move the team.`;

        if (from.activeTeamCount !== to.activeTeamCount)
            return `${from.divName} and ${to.divName} are both scheduled and hold different numbers `
                + `of active teams (${from.activeTeamCount} and ${to.activeTeamCount}). Teams can `
                + `only be traded between scheduled pools of equal size. Break down BOTH schedules `
                + `before moving teams between them — tearing down only one still leaves the other `
                + `frozen.`;

        if (teamsOut !== 1 || teamsBack !== 1)
            return `${from.divName} and ${to.divName} are both scheduled, so this has to be a `
                + `one-for-one swap: one team out, one team back. Use the swap arrow on a team's `
                + `row to pick the team that comes back.`;

        return null;
    }

    // ── Swap conversation ──

    /** Statement agreed to — now ask which team comes back. */
    proceedToPicker() {
        const m = this.swapModal();
        if (!m) return;
        this.swapModal.set({ ...m, stage: 'picker' });
    }

    backToStatement() {
        const m = this.swapModal();
        if (!m) return;
        this.swapModal.set({ ...m, stage: 'statement', counterTeam: null });
    }

    chooseCounterTeam(team: PoolTeamDto) {
        const m = this.swapModal();
        if (!m) return;
        this.swapModal.set({ ...m, counterTeam: team });
    }

    closeSwapModal() {
        this.swapModal.set(null);
    }

    closeDenyModal() {
        this.denyModal.set(null);
    }

    /**
     * Commit the trade. Server contract: SourceTeamIds land in TargetDivId and TargetTeamIds land
     * in SourceDivId — so "source" here is the pool the clicked team is LEAVING, whichever panel
     * that happens to be.
     */
    confirmSwap() {
        const m = this.swapModal();
        if (!m?.counterTeam || this.isTransferring()) return;

        const leavingDivId = m.direction === 'source-to-target' ? this.sourceDivId()! : this.targetDivId()!;
        const arrivingDivId = m.direction === 'source-to-target' ? this.targetDivId()! : this.sourceDivId()!;

        this.isTransferring.set(true);
        this.poolService.executeTransfer({
            sourceTeamIds: [m.movingTeam.teamId],
            targetTeamIds: [m.counterTeam.teamId],
            sourceDivId: leavingDivId,
            targetDivId: arrivingDivId,
            isSymmetricalSwap: true
        }).subscribe({
            next: result => {
                this.toast.show(
                    `${m.movingTeam.teamName} and ${m.counterTeam!.teamName} swapped pools. ${result.message}`,
                    'success', 5000);
                this.justMovedIds.set(new Set([m.movingTeam.teamId, m.counterTeam!.teamId]));
                this.queueScrollToMoved([m.movingTeam.teamId], arrivingDivId);
                this.isTransferring.set(false);
                this.swapModal.set(null);
                this.reloadAfterTransfer();
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Swap failed.', 'danger', 6000);
                this.isTransferring.set(false);
            }
        });
    }

    private reloadAfterTransfer() {
        this.sourceSelected.set(new Set());
        this.targetSelected.set(new Set());
        if (this.sourceDivId()) this.loadTeams('source', this.sourceDivId()!);
        if (this.targetDivId()) this.loadTeams('target', this.targetDivId()!);
        this.poolService.getDivisions().subscribe({
            next: divs => this.divisionOptions.set(divs)
        });
    }

    // ── Batch transfer ──

    // Server Direction is request-relative; the left-arrow flow swaps the request,
    // so un-flip it back into screen orientation.
    movesRight(tpDirection: string): boolean {
        return (tpDirection === 'source-to-target') === (this.transferDirection() === 'source-to-target');
    }

    moveSelectedToTarget() {
        if (this.sourceSelected().size === 0 || !this.sourceDivId() || !this.targetDivId()) return;
        if (this.refuseBatchIfScheduled('source-to-target')) return;
        this.requestPreview('source-to-target');
    }

    moveSelectedToSource() {
        if (this.targetSelected().size === 0 || !this.targetDivId() || !this.sourceDivId()) return;
        if (this.refuseBatchIfScheduled('target-to-source')) return;
        this.requestPreview('target-to-source');
    }

    /**
     * The batch footer is an unscheduled-pools tool. Once a scheduled pool is involved the only
     * permitted movement is a one-for-one swap, and that has its own conversation — so refuse here
     * and name it, rather than running a second, divergent path to the same server call.
     * Returns true when the move was refused.
     */
    private refuseBatchIfScheduled(direction: MoveDirection): boolean {
        const from = direction === 'source-to-target' ? this.sourceDiv() : this.targetDiv();
        const to = direction === 'source-to-target' ? this.targetDiv() : this.sourceDiv();
        if (!from || !to) return false;
        if (!from.isScheduled && !to.isScheduled) return false;

        const selected = direction === 'source-to-target'
            ? this.sourceSelected().size : this.targetSelected().size;
        // teamsBack = 0: the footer never carries a counter-team, so a both-scheduled pair lands
        // on the one-for-one message, which points at the arrow.
        this.denyModal.set(this.denialFor(from, to, selected, 0)
            ?? `${from.divName} and ${to.divName} are both scheduled. Use the swap arrow on a `
             + `team's row to trade one team for one team.`);
        return true;
    }

    private requestPreview(direction: 'source-to-target' | 'target-to-source') {
        this.isLoadingPreview.set(true);
        this.transferDirection.set(direction);

        const sourceTeamIds = direction === 'source-to-target'
            ? Array.from(this.sourceSelected())
            : Array.from(this.targetSelected());
        const targetTeamIds = direction === 'source-to-target'
            ? Array.from(this.targetSelected())
            : Array.from(this.sourceSelected());
        const sourceDivId = direction === 'source-to-target' ? this.sourceDivId()! : this.targetDivId()!;
        const targetDivId = direction === 'source-to-target' ? this.targetDivId()! : this.sourceDivId()!;

        this.poolService.previewTransfer({
            sourceTeamIds,
            targetTeamIds,
            sourceDivId,
            targetDivId,
            isSymmetricalSwap: targetTeamIds.length > 0
        }).subscribe({
            next: preview => {
                this.transferPreview.set(preview);
                this.isLoadingPreview.set(false);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to preview transfer.', 'danger', 4000);
                this.isLoadingPreview.set(false);
            }
        });
    }

    // updatePreview() removed: it existed only to re-run a preview after the director selected a
    // counter-team in the other panel, which was the flow the swap modal replaced.

    confirmTransfer() {
        const preview = this.transferPreview();
        if (!preview) return;

        const direction = this.transferDirection();
        const sourceTeamIds = direction === 'source-to-target'
            ? Array.from(this.sourceSelected())
            : Array.from(this.targetSelected());
        const targetTeamIds = direction === 'source-to-target'
            ? Array.from(this.targetSelected())
            : Array.from(this.sourceSelected());
        const sourceDivId = direction === 'source-to-target' ? this.sourceDivId()! : this.targetDivId()!;
        const targetDivId = direction === 'source-to-target' ? this.targetDivId()! : this.sourceDivId()!;

        this.isTransferring.set(true);
        this.poolService.executeTransfer({
            sourceTeamIds,
            targetTeamIds,
            sourceDivId,
            targetDivId,
            isSymmetricalSwap: targetTeamIds.length > 0
        }).subscribe({
            next: result => {
                this.toast.show(result.message, 'success', 4000);
                // Both directions highlight: sourceTeamIds landed in the target div,
                // counter-teams of a symmetrical swap landed in the source div.
                this.justMovedIds.set(new Set([...sourceTeamIds, ...targetTeamIds]));
                this.queueScrollToMoved(sourceTeamIds, targetDivId);
                this.isTransferring.set(false);
                this.transferPreview.set(null);
                this.reloadAfterTransfer();
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Transfer failed.', 'danger', 4000);
                this.isTransferring.set(false);
            }
        });
    }

    private executeTransferDirect(sourceTeamIds: string[], targetTeamIds: string[],
        sourceDivId: string, targetDivId: string, isSymmetricalSwap: boolean, teamName?: string) {
        this.poolService.executeTransfer({
            sourceTeamIds,
            targetTeamIds,
            sourceDivId,
            targetDivId,
            isSymmetricalSwap
        }).subscribe({
            next: result => {
                this.toast.show(teamName ? `${teamName} moved. ${result.message}` : result.message, 'success', 3000);
                this.justMovedIds.set(new Set([...sourceTeamIds, ...targetTeamIds]));
                this.queueScrollToMoved(sourceTeamIds, targetDivId);
                this.swappingId.set(null);
                this.reloadAfterTransfer();
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Transfer failed.', 'danger', 4000);
                this.swappingId.set(null);
            }
        });
    }

    cancelTransferPreview() {
        this.transferPreview.set(null);
        this.isLoadingPreview.set(false);
        // Both modals name specific pools. Changing a panel's division makes that naming wrong,
        // so they close with the preview rather than describing a pairing that no longer exists.
        this.swapModal.set(null);
        this.denyModal.set(null);
    }

    // ── AM-053: scroll the just-moved team into view ──

    /** The primary moved teams (`sourceTeamIds`) land in the transfer's target div —
     *  the TARGET panel on a rightward move, the SOURCE panel on a leftward one.
     *  Batch scrolls to the first moved team (Ann's spec); a symmetrical swap's
     *  counter-team is covered by its own highlight on the opposite panel. */
    private queueScrollToMoved(sourceTeamIds: string[], landedDivId: string) {
        const teamId = sourceTeamIds[0];
        if (!teamId) return;
        this.pendingScroll = { panel: landedDivId === this.targetDivId() ? 'target' : 'source', teamId };
    }

    private scrollToMovedRow(panel: 'source' | 'target', teamId: string) {
        const wrap = (panel === 'source' ? this.sourceScroll() : this.targetScroll())?.nativeElement;
        const row = wrap?.querySelector(`tr[data-team-id="${teamId}"]`);
        // block:'nearest' keeps the correction minimal and confined — no page jump.
        row?.scrollIntoView({
            behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth',
            block: 'nearest'
        });
    }

    // ── Active toggle ──

    toggleActive(team: PoolTeamDto) {
        const newActive = !team.active;
        this.poolService.toggleTeamActive(team.teamId, newActive).subscribe({
            next: () => {
                const updateTeams = (teams: PoolTeamDto[]) =>
                    teams.map(t => t.teamId === team.teamId ? { ...t, active: newActive } : t);
                this.sourceTeams.update(r => updateTeams(r));
                this.targetTeams.update(r => updateTeams(r));
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to toggle active status.', 'danger', 4000);
            }
        });
    }

    // ── DivRank editing ──

    startDivRankEdit(team: PoolTeamDto) {
        this.editingDivRankTeamId.set(team.teamId);
        this.editingDivRankValue.set(team.divRank);
    }

    saveDivRankEdit(team: PoolTeamDto) {
        const newRank = this.editingDivRankValue();
        if (newRank === team.divRank) {
            this.editingDivRankTeamId.set(null);
            return;
        }
        this.poolService.updateTeamDivRank(team.teamId, newRank).subscribe({
            next: result => {
                this.editingDivRankTeamId.set(null);
                if (this.sourceDivId()) this.loadTeams('source', this.sourceDivId()!);
                if (this.targetDivId()) this.loadTeams('target', this.targetDivId()!);

                // Say what actually happened. A rank edit is how directors trade two teams'
                // schedules, and this screen used to confirm it with silence — which read the
                // same whether the games had moved or not.
                this.toast.show(
                    result.message,
                    result.gamesReseated > 0 ? 'success' : 'info',
                    6000);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to update rank.', 'danger', 4000);
            }
        });
    }

    cancelDivRankEdit() {
        this.editingDivRankTeamId.set(null);
    }

    // ── Helpers ──

    capacityPercent(div: PoolDivisionOptionDto | null): number {
        if (!div || div.maxTeams === 0) return 0;
        return Math.min(100, Math.round((div.teamCount / div.maxTeams) * 100));
    }

    capacityColor(div: PoolDivisionOptionDto | null): string {
        const pct = this.capacityPercent(div);
        if (pct > 90) return 'var(--bs-danger)';
        if (pct > 75) return 'var(--bs-warning)';
        return 'var(--bs-success)';
    }

    sortIcon(panel: 'source' | 'target', col: SortColumn): string {
        const currentCol = panel === 'source' ? this.sourceSortCol() : this.targetSortCol();
        const currentDir = panel === 'source' ? this.sourceSortDir() : this.targetSortDir();
        if (currentCol !== col || !currentDir) return 'bi-chevron-expand';
        return currentDir === 'asc' ? 'bi-sort-up' : 'bi-sort-down';
    }

    private filterTeams(teams: PoolTeamDto[], filter: string): PoolTeamDto[] {
        if (!filter.trim()) return teams;
        const lower = filter.toLowerCase();
        return teams.filter(t =>
            t.teamName.toLowerCase().includes(lower) ||
            (t.clubName ?? '').toLowerCase().includes(lower) ||
            (t.clubRepName ?? '').toLowerCase().includes(lower));
    }

    /** Extract the national rank number from the JSON field, or null if unranked */
    getNationalRank(team: PoolTeamDto): number | null {
        if (!team.nationalRankingData) return null;
        try {
            const data = JSON.parse(team.nationalRankingData) as NationalRankingDataDto;
            return data.rank ?? null;
        } catch { return null; }
    }

    /**
     * Tooltip for a national rank (rating, record, AGD, schedule) — plus the SEASON it was
     * stamped from. Ranks from different seasons are not comparable, and this column is sorted
     * to seed pools, so which season a number came from has to be visible at the point of use.
     * Stamps written before the season was recorded say so rather than guessing one.
     */
    getRankingTooltip(team: PoolTeamDto): string {
        if (!team.nationalRankingData) return '';
        try {
            const d = JSON.parse(team.nationalRankingData) as NationalRankingDataDto;
            const season = d.season
                ? `${d.season}-${String((Number(d.season) + 1) % 100).padStart(2, '0')} season`
                : 'season not recorded';
            return `${d.team}\nRating: ${d.rating} | Record: ${d.record}\nAGD: ${d.agd} | Sched: ${d.sched}\n${season}`;
        } catch { return ''; }
    }

    private groupByAgegroup(divisions: PoolDivisionOptionDto[]): AgegroupGroup[] {
        const groups = new Map<string, PoolDivisionOptionDto[]>();
        for (const div of divisions) {
            const key = div.agegroupName ?? 'Other';
            if (!groups.has(key)) groups.set(key, []);
            groups.get(key)!.push(div);
        }
        return Array.from(groups.entries()).map(([label, divs]) => ({
            label,
            agegroupColor: divs[0]?.agegroupColor ?? null,
            divisions: divs
        }));
    }
}
