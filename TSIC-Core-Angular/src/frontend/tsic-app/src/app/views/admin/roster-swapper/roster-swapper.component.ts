import { Component, inject, signal, computed, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ToastService } from '@shared-ui/toast.service';
import {
    RosterSwapperService,
    SwapperPoolOptionDto,
    SwapperPlayerDto,
    RosterTransferFeePreviewDto
} from './services/roster-swapper.service';

interface PoolGroup {
    label: string;
    pools: SwapperPoolOptionDto[];
}

@Component({
    selector: 'app-roster-swapper',
    standalone: true,
    imports: [CommonModule, FormsModule],
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

    // Source panel
    readonly sourcePoolId = signal<string | null>(null);
    readonly sourcePool = computed(() => this.poolOptions().find(p => p.poolId === this.sourcePoolId()) ?? null);
    readonly sourceRoster = signal<SwapperPlayerDto[]>([]);
    readonly sourceSelected = signal<Set<string>>(new Set());
    readonly sourceFilter = signal('');
    readonly filteredSourceRoster = computed(() =>
        this.filterPlayers(this.sourceRoster(), this.sourceFilter()));
    readonly isSourceUnassigned = computed(() => this.sourcePool()?.isUnassignedAdultsPool ?? false);

    // Target panel
    readonly targetPoolId = signal<string | null>(null);
    readonly targetPool = computed(() => this.poolOptions().find(p => p.poolId === this.targetPoolId()) ?? null);
    readonly targetRoster = signal<SwapperPlayerDto[]>([]);
    readonly targetSelected = signal<Set<string>>(new Set());
    readonly targetFilter = signal('');
    readonly filteredTargetRoster = computed(() =>
        this.filterPlayers(this.targetRoster(), this.targetFilter()));
    readonly isTargetUnassigned = computed(() => this.targetPool()?.isUnassignedAdultsPool ?? false);

    // Transfer state
    readonly feePreview = signal<RosterTransferFeePreviewDto[] | null>(null);
    readonly transferDirection = signal<'source-to-target' | 'target-to-source'>('source-to-target');
    readonly isTransferring = signal(false);
    readonly isLoadingPreview = signal(false);
    readonly showTransferConfirm = signal(false);

    // General
    readonly isLoading = signal(false);

    // Transfer type detection
    readonly transferType = computed(() => {
        const dir = this.transferDirection();
        const srcUnassigned = dir === 'source-to-target' ? this.isSourceUnassigned() : this.isTargetUnassigned();
        const tgtUnassigned = dir === 'source-to-target' ? this.isTargetUnassigned() : this.isSourceUnassigned();
        if (srcUnassigned && !tgtUnassigned) return 'staff-create';
        if (!srcUnassigned && tgtUnassigned) return 'staff-delete';
        return 'player-swap';
    });

    readonly confirmMessage = computed(() => {
        const preview = this.feePreview();
        if (!preview || preview.length === 0) return '';
        const dir = this.transferDirection();
        const srcName = dir === 'source-to-target'
            ? (this.sourcePool()?.poolName ?? 'Source')
            : (this.targetPool()?.poolName ?? 'Target');
        const tgtName = dir === 'source-to-target'
            ? (this.targetPool()?.poolName ?? 'Target')
            : (this.sourcePool()?.poolName ?? 'Source');
        const count = preview.length;
        const type = preview[0]?.transferType ?? 'player-swap';
        switch (type) {
            case 'staff-create':
                return `Assign ${count} coach(es) to ${tgtName}. New Staff registrations will be created. Original Unassigned Adult records are preserved.`;
            case 'staff-delete':
                return `Remove ${count} staff from ${srcName}. Staff registrations will be deleted. Original Unassigned Adult records remain in the pool.`;
            case 'staff-move':
                return `Move ${count} staff from ${srcName} to ${tgtName}.`;
            default:
                return `Move ${count} player(s) from ${srcName} to ${tgtName}. Fees will be recalculated.`;
        }
    });

    constructor() {
        this.loadPoolOptions();
    }

    loadPoolOptions() {
        this.isLoading.set(true);
        this.swapperService.getPoolOptions().subscribe({
            next: pools => {
                this.poolOptions.set(pools);
                this.isLoading.set(false);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to load pool options.', 'danger', 4000);
                this.isLoading.set(false);
            }
        });
    }

    onSourcePoolChange(poolId: string) {
        this.sourcePoolId.set(poolId);
        this.sourceSelected.set(new Set());
        this.sourceFilter.set('');
        this.feePreview.set(null);
        if (!poolId) {
            this.sourceRoster.set([]);
            return;
        }
        this.loadRoster('source', poolId);
    }

    onTargetPoolChange(poolId: string) {
        this.targetPoolId.set(poolId);
        this.targetSelected.set(new Set());
        this.targetFilter.set('');
        this.feePreview.set(null);
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
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to load roster.', 'danger', 4000);
            }
        });
    }

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
        this.sourceSelected.set(new Set(this.filteredSourceRoster().map(p => p.registrationId)));
    }

    deselectAllSource() {
        this.sourceSelected.set(new Set());
    }

    selectAllTarget() {
        this.targetSelected.set(new Set(this.filteredTargetRoster().map(p => p.registrationId)));
    }

    deselectAllTarget() {
        this.targetSelected.set(new Set());
    }

    moveSourceToTarget() {
        if (this.sourceSelected().size === 0 || !this.sourcePoolId() || !this.targetPoolId()) return;
        this.transferDirection.set('source-to-target');
        this.requestPreview(Array.from(this.sourceSelected()), this.sourcePoolId()!, this.targetPoolId()!);
    }

    moveTargetToSource() {
        if (this.targetSelected().size === 0 || !this.targetPoolId() || !this.sourcePoolId()) return;
        this.transferDirection.set('target-to-source');
        this.requestPreview(Array.from(this.targetSelected()), this.targetPoolId()!, this.sourcePoolId()!);
    }

    private requestPreview(regIds: string[], sourcePoolId: string, targetPoolId: string) {
        this.isLoadingPreview.set(true);
        this.swapperService.previewTransfer({
            registrationIds: regIds,
            sourcePoolId,
            targetPoolId
        }).subscribe({
            next: preview => {
                this.feePreview.set(preview);
                this.isLoadingPreview.set(false);
                this.showTransferConfirm.set(true);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to generate transfer preview.', 'danger', 4000);
                this.isLoadingPreview.set(false);
            }
        });
    }

    confirmTransfer() {
        const preview = this.feePreview();
        if (!preview) return;

        const dir = this.transferDirection();
        const sourceId = dir === 'source-to-target' ? this.sourcePoolId()! : this.targetPoolId()!;
        const targetId = dir === 'source-to-target' ? this.targetPoolId()! : this.sourcePoolId()!;
        const regIds = preview
            .filter(p => p.transferType !== 'invalid' && !p.warning?.includes('will be skipped'))
            .map(p => p.registrationId);

        if (regIds.length === 0) {
            this.toast.show('No valid registrations to transfer.', 'warning');
            this.cancelTransfer();
            return;
        }

        this.isTransferring.set(true);
        this.swapperService.executeTransfer({
            registrationIds: regIds,
            sourcePoolId: sourceId,
            targetPoolId: targetId
        }).subscribe({
            next: result => {
                this.toast.show(result.message, 'success', 3000);
                this.isTransferring.set(false);
                this.cancelTransfer();
                // Reload both rosters
                if (this.sourcePoolId()) this.loadRoster('source', this.sourcePoolId()!);
                if (this.targetPoolId()) this.loadRoster('target', this.targetPoolId()!);
                // Reload pool options to update counts
                this.swapperService.getPoolOptions().subscribe({
                    next: pools => this.poolOptions.set(pools)
                });
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Transfer failed.', 'danger', 4000);
                this.isTransferring.set(false);
            }
        });
    }

    cancelTransfer() {
        this.feePreview.set(null);
        this.showTransferConfirm.set(false);
    }

    toggleActive(player: SwapperPlayerDto) {
        const newActive = !player.bActive;
        this.swapperService.togglePlayerActive(player.registrationId, newActive).subscribe({
            next: () => {
                // Update in-place
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

    // Capacity bar helpers
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

    hasFeeImpact(): boolean {
        const preview = this.feePreview();
        if (!preview) return false;
        return preview.some(p => p.feeDelta !== 0);
    }

    private filterPlayers(roster: SwapperPlayerDto[], filter: string): SwapperPlayerDto[] {
        if (!filter.trim()) return roster;
        const lower = filter.toLowerCase();
        return roster.filter(p => p.playerName.toLowerCase().includes(lower));
    }

    private groupByCategory(pools: SwapperPoolOptionDto[]): PoolGroup[] {
        const groups: PoolGroup[] = [];
        const unassigned = pools.filter(p => p.isUnassignedAdultsPool);
        if (unassigned.length > 0) {
            groups.push({ label: 'Unassigned Adults', pools: unassigned });
        }

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
