import { Component, inject, signal, computed, ChangeDetectionStrategy, ElementRef, Injector, afterNextRender, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ToastService } from '@shared-ui/toast.service';
import {
    RosterSwapperService,
    SwapperPoolOptionDto,
    SwapperPlayerDto
} from './services/roster-swapper.service';
import { contrastText } from '../../scheduling/shared/utils/scheduling-helpers';

interface PoolGroup {
    label: string;
    pools: SwapperPoolOptionDto[];
}

type SortDir = 'asc' | 'desc' | null;
type SortColumn = keyof SwapperPlayerDto | null;

@Component({
    selector: 'app-roster-swapper',
    standalone: true,
    imports: [CommonModule, FormsModule, RouterLink],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './roster-swapper.component.html',
    styleUrl: './roster-swapper.component.scss'
})
export class RosterSwapperComponent {
    private readonly swapperService = inject(RosterSwapperService);
    private readonly toast = inject(ToastService);

    // Pool options
    readonly poolOptions = signal<SwapperPoolOptionDto[]>([]);
    readonly groupedPoolOptions = computed<PoolGroup[]>(() => this.groupByCategory(this.poolOptions()));

    // Custom dropdown open state
    readonly sourceDropdownOpen = signal(false);
    readonly targetDropdownOpen = signal(false);

    // Dropdown max-height, computed on open to nearly fill the panel's visible area
    readonly sourceDropdownMaxHeight = signal<number | null>(null);
    readonly targetDropdownMaxHeight = signal<number | null>(null);

    // Source panel
    readonly sourcePoolId = signal<string | null>(null);
    readonly sourcePool = computed(() => this.poolOptions().find(p => p.poolId === this.sourcePoolId()) ?? null);
    readonly sourceRoster = signal<SwapperPlayerDto[]>([]);
    readonly sourceSelected = signal<Set<string>>(new Set());
    readonly sourceFilter = signal('');
    readonly sourceSortCol = signal<SortColumn>(null);
    readonly sourceSortDir = signal<SortDir>(null);
    readonly sortedFilteredSourceRoster = computed(() =>
        this.sortPlayers(this.filterPlayers(this.sourceRoster(), this.sourceFilter()), this.sourceSortCol(), this.sourceSortDir()));
    readonly isSourceUnassigned = computed(() => this.sourcePool()?.isUnassignedAdultsPool ?? false);

    // Target panel
    readonly targetPoolId = signal<string | null>(null);
    readonly targetPool = computed(() => this.poolOptions().find(p => p.poolId === this.targetPoolId()) ?? null);
    readonly targetRoster = signal<SwapperPlayerDto[]>([]);
    readonly targetSelected = signal<Set<string>>(new Set());
    readonly targetFilter = signal('');
    readonly targetSortCol = signal<SortColumn>(null);
    readonly targetSortDir = signal<SortDir>(null);
    readonly sortedFilteredTargetRoster = computed(() =>
        this.sortPlayers(this.filterPlayers(this.targetRoster(), this.targetFilter()), this.targetSortCol(), this.targetSortDir()));
    readonly isTargetUnassigned = computed(() => this.targetPool()?.isUnassignedAdultsPool ?? false);

    // Transfer state
    readonly swappingId = signal<string | null>(null); // registrationId currently being swapped
    // Players moved by the LAST swap (single or batch). The move is instant with no
    // confirmation, so the highlight is the director's only post-move trace — it
    // persists until the next swap or a pool change (deliberately no timed fade).
    readonly justMovedIds = signal<ReadonlySet<string>>(new Set());
    readonly isBatchSwapping = signal(false);

    // AM-053/AM-039: a player moved onto a long roster can land off-screen — the
    // highlight is useless if you can't see it. After the landing panel's reload
    // renders, scroll the first moved row into view. One-shot: queued by the swap
    // success handler, drained by that panel's loadRoster. (Port of Pool
    // Assignment's AM-053 re-open fix — same structure, same hole.)
    private readonly injector = inject(Injector);
    private readonly sourceScroll = viewChild<ElementRef<HTMLElement>>('sourceScroll');
    private readonly targetScroll = viewChild<ElementRef<HTMLElement>>('targetScroll');
    private pendingScroll: { panel: 'source' | 'target'; regId: string } | null = null;

    // General
    readonly isLoading = signal(false);

    constructor() {
        this.loadPoolOptions();
    }

    loadPoolOptions() {
        this.isLoading.set(true);
        this.swapperService.getPoolOptions().subscribe({
            next: pools => {
                this.poolOptions.set(pools);
                this.isLoading.set(false);
                // Auto-select the first team as the source on initial load
                if (!this.sourcePoolId()) {
                    const first = this.groupedPoolOptions()[0]?.pools[0];
                    if (first) this.onSourcePoolChange(first.poolId);
                }
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to load pool options.', 'danger', 4000);
                this.isLoading.set(false);
            }
        });
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

    selectSourcePool(poolId: string): void {
        this.sourceDropdownOpen.set(false);
        this.onSourcePoolChange(poolId);
    }

    selectTargetPool(poolId: string): void {
        this.targetDropdownOpen.set(false);
        this.onTargetPoolChange(poolId);
    }

    closeDropdowns(): void {
        this.sourceDropdownOpen.set(false);
        this.targetDropdownOpen.set(false);
    }

    readonly contrastText = contrastText;

    /** Space from just below the trigger to the bottom of the panel container, minus breathing room. */
    private computeDropdownMaxHeight(trigger: HTMLElement): number {
        const triggerBottom = trigger.getBoundingClientRect().bottom;
        const container = trigger.closest('.roster-swapper-container');
        const containerBottom = container
            ? container.getBoundingClientRect().bottom
            : window.innerHeight;
        return Math.max(160, Math.floor(containerBottom - triggerBottom - 12));
    }

    /** Agegroup/division context for a pool, shown as a prefix on the selected-team trigger. */
    poolGroupLabel(pool: SwapperPoolOptionDto): string {
        if (!pool.agegroupName) return '';
        return pool.divName ? `${pool.agegroupName} / ${pool.divName}` : pool.agegroupName;
    }

    onSourcePoolChange(poolId: string) {
        this.sourcePoolId.set(poolId);
        this.justMovedIds.set(new Set());
        this.sourceSelected.set(new Set());
        this.sourceFilter.set('');
        this.sourceSortCol.set(null);
        this.sourceSortDir.set(null);
        if (!poolId) {
            this.sourceRoster.set([]);
            return;
        }
        this.loadRoster('source', poolId);
    }

    onTargetPoolChange(poolId: string) {
        this.targetPoolId.set(poolId);
        this.justMovedIds.set(new Set());
        this.targetSelected.set(new Set());
        this.targetFilter.set('');
        this.targetSortCol.set(null);
        this.targetSortDir.set(null);
        if (!poolId) {
            this.targetRoster.set([]);
            return;
        }
        this.loadRoster('target', poolId);
    }

    private loadRoster(panel: 'source' | 'target', poolId: string) {
        this.swapperService.getRoster(poolId).subscribe({
            next: roster => {
                if (panel === 'source') this.sourceRoster.set(roster);
                else this.targetRoster.set(roster);
                // AM-053/AM-039: this reload carries the just-moved player — scroll it
                // into view once these rows have rendered. Cleared before scheduling,
                // so it fires exactly once; a row hidden by an active filter no-ops.
                if (this.pendingScroll?.panel === panel) {
                    const pending = this.pendingScroll;
                    this.pendingScroll = null;
                    afterNextRender(() => this.scrollToMovedRow(pending.panel, pending.regId), { injector: this.injector });
                }
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to load roster.', 'danger', 4000);
            }
        });
    }

    // ── Sorting ──

    onSort(panel: 'source' | 'target', col: SortColumn) {
        const currentCol = panel === 'source' ? this.sourceSortCol() : this.targetSortCol();
        const currentDir = panel === 'source' ? this.sourceSortDir() : this.targetSortDir();

        let newDir: SortDir;
        if (currentCol !== col) {
            newDir = 'asc';
        } else if (currentDir === 'asc') {
            newDir = 'desc';
        } else {
            newDir = null;
        }

        if (panel === 'source') {
            this.sourceSortCol.set(newDir ? col : null);
            this.sourceSortDir.set(newDir);
        } else {
            this.targetSortCol.set(newDir ? col : null);
            this.targetSortDir.set(newDir);
        }
    }

    private sortPlayers(roster: SwapperPlayerDto[], col: SortColumn, dir: SortDir): SwapperPlayerDto[] {
        if (!col || !dir) return roster;
        const mult = dir === 'asc' ? 1 : -1;
        return [...roster].sort((a, b) => {
            const aVal = a[col];
            const bVal = b[col];
            if (aVal == null && bVal == null) return 0;
            if (aVal == null) return 1;
            if (bVal == null) return -1;
            if (typeof aVal === 'string' && typeof bVal === 'string') {
                return aVal.localeCompare(bVal) * mult;
            }
            if (typeof aVal === 'number' && typeof bVal === 'number') {
                return (aVal - bVal) * mult;
            }
            if (typeof aVal === 'boolean' && typeof bVal === 'boolean') {
                return ((aVal ? 1 : 0) - (bVal ? 1 : 0)) * mult;
            }
            return String(aVal).localeCompare(String(bVal)) * mult;
        });
    }

    // ── Selection ──

    toggleSourceSelect(regId: string) {
        const current = new Set(this.sourceSelected());
        if (current.has(regId)) current.delete(regId);
        else current.add(regId);
        this.sourceSelected.set(current);
    }

    toggleTargetSelect(regId: string) {
        const current = new Set(this.targetSelected());
        if (current.has(regId)) current.delete(regId);
        else current.add(regId);
        this.targetSelected.set(current);
    }

    selectAllSource() {
        this.sourceSelected.set(new Set(this.sortedFilteredSourceRoster().map(p => p.registrationId)));
    }

    deselectAllSource() {
        this.sourceSelected.set(new Set());
    }

    selectAllTarget() {
        this.targetSelected.set(new Set(this.sortedFilteredTargetRoster().map(p => p.registrationId)));
    }

    deselectAllTarget() {
        this.targetSelected.set(new Set());
    }

    // ── Single-row swap ──

    swapToTarget(player: SwapperPlayerDto) {
        if (!this.targetPoolId() || !this.sourcePoolId()) {
            this.toast.show('Select a target team first.', 'warning');
            return;
        }
        this.executeSwap([player.registrationId], this.sourcePoolId()!, this.targetPoolId()!, player.playerName);
    }

    swapToSource(player: SwapperPlayerDto) {
        if (!this.sourcePoolId() || !this.targetPoolId()) {
            this.toast.show('Select a source team first.', 'warning');
            return;
        }
        this.executeSwap([player.registrationId], this.targetPoolId()!, this.sourcePoolId()!, player.playerName);
    }

    // ── Batch swap ──

    swapSelectedToTarget() {
        if (this.sourceSelected().size === 0 || !this.sourcePoolId() || !this.targetPoolId()) return;
        this.isBatchSwapping.set(true);
        this.executeSwap(
            Array.from(this.sourceSelected()),
            this.sourcePoolId()!,
            this.targetPoolId()!
        );
    }

    swapSelectedToSource() {
        if (this.targetSelected().size === 0 || !this.targetPoolId() || !this.sourcePoolId()) return;
        this.isBatchSwapping.set(true);
        this.executeSwap(
            Array.from(this.targetSelected()),
            this.targetPoolId()!,
            this.sourcePoolId()!
        );
    }

    private executeSwap(regIds: string[], sourcePoolId: string, targetPoolId: string, playerName?: string) {
        if (regIds.length === 1) this.swappingId.set(regIds[0]);

        this.swapperService.executeTransfer({
            registrationIds: regIds,
            sourcePoolId,
            targetPoolId
        }).subscribe({
            next: result => {
                // A transfer can succeed for SOME of the selection: a registrant on an active
                // recurring-billing plan is refused when the target team prices differently,
                // because the plan cannot follow them. So drive everything below off what the
                // server says actually moved — never off regIds, which is only what was asked.
                const moved = result.movedRegistrationIds ?? [];
                const blocked = result.blocked ?? [];

                if (moved.length > 0) {
                    this.toast.show(playerName ? `${playerName} swapped. ${result.message}` : result.message, 'success', 3000);
                }

                // One alert per refusal, each naming its player. 'danger' resolves to timeout 0 in
                // ToastService, so these stack and stay until the director dismisses each one — a
                // refusal that auto-scrolls away is a refusal nobody acted on. Uncapped on purpose:
                // ten blocked players means ten alerts, which is the honest picture.
                for (const b of blocked) {
                    this.toast.show(b.reason, 'danger', undefined, `${b.playerName} — not moved`);
                }

                this.justMovedIds.set(new Set(moved));
                this.queueScrollToMoved(moved, targetPoolId);
                this.swappingId.set(null);
                this.isBatchSwapping.set(false);
                this.sourceSelected.set(new Set());
                this.targetSelected.set(new Set());
                // Reload both rosters
                if (this.sourcePoolId()) this.loadRoster('source', this.sourcePoolId()!);
                if (this.targetPoolId()) this.loadRoster('target', this.targetPoolId()!);
                // Reload pool options to update counts
                this.swapperService.getPoolOptions().subscribe({
                    next: pools => this.poolOptions.set(pools)
                });
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Swap failed.', 'danger', 4000);
                this.swappingId.set(null);
                this.isBatchSwapping.set(false);
            }
        });
    }

    // ── AM-053/AM-039: scroll the just-moved player into view ──

    /** Moved players land in the swap's target pool — the TARGET panel on a
     *  rightward swap, the SOURCE panel on a leftward one. Batch scrolls to the
     *  first moved player (same spec as Pool Assignment's AM-053). */
    private queueScrollToMoved(regIds: string[], landedPoolId: string) {
        const regId = regIds[0];
        if (!regId) return;
        this.pendingScroll = { panel: landedPoolId === this.targetPoolId() ? 'target' : 'source', regId };
    }

    private scrollToMovedRow(panel: 'source' | 'target', regId: string) {
        const wrap = (panel === 'source' ? this.sourceScroll() : this.targetScroll())?.nativeElement;
        const row = wrap?.querySelector(`tr[data-reg-id="${regId}"]`);
        // block:'nearest' keeps the correction minimal and confined — no page jump.
        row?.scrollIntoView({
            behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth',
            block: 'nearest'
        });
    }

    // ── Active toggle ──

    toggleActive(player: SwapperPlayerDto) {
        const newActive = !player.bActive;
        this.swapperService.togglePlayerActive(player.registrationId, newActive).subscribe({
            next: () => {
                const updateRoster = (roster: SwapperPlayerDto[]) =>
                    roster.map(p => p.registrationId === player.registrationId
                        ? { ...p, bActive: newActive }
                        : p);
                this.sourceRoster.update(r => updateRoster(r));
                this.targetRoster.update(r => updateRoster(r));
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to toggle active status.', 'danger', 4000);
            }
        });
    }

    // ── Helpers ──

    capacityPercent(pool: SwapperPoolOptionDto | null): number {
        if (!pool || pool.maxCount === 0) return 0;
        return Math.min(100, Math.round((pool.rosterCount / pool.maxCount) * 100));
    }

    capacityColor(pool: SwapperPoolOptionDto | null): string {
        const pct = this.capacityPercent(pool);
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

    private filterPlayers(roster: SwapperPlayerDto[], filter: string): SwapperPlayerDto[] {
        if (!filter.trim()) return roster;
        const lower = filter.toLowerCase();
        return roster.filter(p =>
            p.playerName.toLowerCase().includes(lower) ||
            (p.school ?? '').toLowerCase().includes(lower) ||
            (p.position ?? '').toLowerCase().includes(lower) ||
            (p.requests ?? '').toLowerCase().includes(lower));
    }

    private groupByCategory(pools: SwapperPoolOptionDto[]): PoolGroup[] {
        const groups: PoolGroup[] = [];

        // Unassigned Adults is intentionally excluded — that flow now lives in
        // LADT Coach Approvals (coach-approval-queue), not the Roster Swapper.
        const teamsByGroup = new Map<string, SwapperPoolOptionDto[]>();
        for (const pool of pools) {
            if (pool.isUnassignedAdultsPool) continue;
            const key = pool.divName
                ? `${pool.agegroupName} / ${pool.divName}`
                : pool.agegroupName ?? 'Other';
            if (!teamsByGroup.has(key)) teamsByGroup.set(key, []);
            teamsByGroup.get(key)!.push(pool);
        }

        for (const [label, teamPools] of teamsByGroup) {
            groups.push({ label, pools: teamPools });
        }

        return groups;
    }
}
