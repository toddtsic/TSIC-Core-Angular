import { Component, inject, signal, computed, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ToastService } from '@shared-ui/toast.service';
import {
    PoolAssignmentService,
    PoolDivisionOptionDto,
    PoolTeamDto,
    PoolTransferPreviewResponse
} from './services/pool-assignment.service';

interface AgegroupGroup {
    label: string;
    divisions: PoolDivisionOptionDto[];
}

type SortDir = 'asc' | 'desc' | null;
type SortColumn = keyof PoolTeamDto | null;

@Component({
    selector: 'app-pool-assignment',
    standalone: true,
    imports: [CommonModule, FormsModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './pool-assignment.component.html',
    styleUrl: './pool-assignment.component.scss'
})
export class PoolAssignmentComponent {
    private readonly poolService = inject(PoolAssignmentService);
    private readonly toast = inject(ToastService);

    // Division options
    readonly divisionOptions = signal<PoolDivisionOptionDto[]>([]);
    readonly groupedDivisionOptions = computed<AgegroupGroup[]>(() => this.groupByAgegroup(this.divisionOptions()));

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

    // Symmetrical swap readiness
    readonly canConfirmTransfer = computed(() => {
        const preview = this.transferPreview();
        if (!preview || this.isTransferring()) return false;
        if (preview.requiresSymmetricalSwap) {
            return preview.teams.some(t => t.direction === 'reverse');
        }
        return true;
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

    // ── Division loading ──

    loadDivisionOptions() {
        this.isLoading.set(true);
        this.poolService.getDivisions().subscribe({
            next: divisions => {
                this.divisionOptions.set(divisions);
                this.isLoading.set(false);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to load division options.', 'danger', 4000);
                this.isLoading.set(false);
            }
        });
    }

    onSourceDivChange(divId: string) {
        this.sourceDivId.set(divId);
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
        const sameAgegroup = this.sourceDiv()?.agegroupId === this.targetDiv()?.agegroupId;
        if (sameAgegroup && !team.isScheduled) {
            // Same agegroup, not scheduled → execute immediately
            this.swappingId.set(team.teamId);
            this.executeTransferDirect([team.teamId], [], this.sourceDivId()!, this.targetDivId()!, false, team.teamName);
        } else {
            // Cross-agegroup or scheduled → show preview
            this.sourceSelected.set(new Set([team.teamId]));
            this.requestPreview('source-to-target');
        }
    }

    swapToSource(team: PoolTeamDto) {
        if (!this.sourceDivId() || !this.targetDivId()) {
            this.toast.show('Select a source division first.', 'warning');
            return;
        }
        const sameAgegroup = this.sourceDiv()?.agegroupId === this.targetDiv()?.agegroupId;
        if (sameAgegroup && !team.isScheduled) {
            this.swappingId.set(team.teamId);
            this.executeTransferDirect([team.teamId], [], this.targetDivId()!, this.sourceDivId()!, false, team.teamName);
        } else {
            this.targetSelected.set(new Set([team.teamId]));
            this.requestPreview('target-to-source');
        }
    }

    // ── Batch transfer ──

    moveSelectedToTarget() {
        if (this.sourceSelected().size === 0 || !this.sourceDivId() || !this.targetDivId()) return;
        this.requestPreview('source-to-target');
    }

    moveSelectedToSource() {
        if (this.targetSelected().size === 0 || !this.targetDivId() || !this.sourceDivId()) return;
        this.requestPreview('target-to-source');
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

    updatePreview() {
        const direction = this.transferDirection();
        this.requestPreview(direction);
    }

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
                this.isTransferring.set(false);
                this.transferPreview.set(null);
                this.sourceSelected.set(new Set());
                this.targetSelected.set(new Set());
                if (this.sourceDivId()) this.loadTeams('source', this.sourceDivId()!);
                if (this.targetDivId()) this.loadTeams('target', this.targetDivId()!);
                this.poolService.getDivisions().subscribe({
                    next: divs => this.divisionOptions.set(divs)
                });
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
                this.swappingId.set(null);
                this.sourceSelected.set(new Set());
                this.targetSelected.set(new Set());
                if (this.sourceDivId()) this.loadTeams('source', this.sourceDivId()!);
                if (this.targetDivId()) this.loadTeams('target', this.targetDivId()!);
                this.poolService.getDivisions().subscribe({
                    next: divs => this.divisionOptions.set(divs)
                });
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
            next: () => {
                this.editingDivRankTeamId.set(null);
                if (this.sourceDivId()) this.loadTeams('source', this.sourceDivId()!);
                if (this.targetDivId()) this.loadTeams('target', this.targetDivId()!);
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
            (t.clubName ?? '').toLowerCase().includes(lower));
    }

    private groupByAgegroup(divisions: PoolDivisionOptionDto[]): AgegroupGroup[] {
        const groups = new Map<string, PoolDivisionOptionDto[]>();
        for (const div of divisions) {
            const key = div.agegroupName ?? 'Other';
            if (!groups.has(key)) groups.set(key, []);
            groups.get(key)!.push(div);
        }
        return Array.from(groups.entries()).map(([label, divs]) => ({ label, divisions: divs }));
    }
}
