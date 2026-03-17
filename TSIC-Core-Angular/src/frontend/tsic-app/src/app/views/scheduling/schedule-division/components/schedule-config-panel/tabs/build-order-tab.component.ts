import {
  ChangeDetectionStrategy, Component, computed, inject, input, OnInit, signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { DragDropModule, CdkDragDrop, moveItemInArray } from '@angular/cdk/drag-drop';
import { ToastService } from '@shared-ui/toast.service';
import { ScheduleCascadeService } from '../../schedule-config/schedule-cascade.service';
import { contrastText, agTeamCount } from '../../../../shared/utils/scheduling-helpers';
import type { AgegroupWithDivisionsDto, ProcessingOrderEntryDto } from '@core/api';

interface DivisionOrderItem {
  divId: string;
  divName: string;
  agegroupId: string;
  agegroupName: string;
  agegroupColor: string | null;
  teamCount: number;
  wave: number;
}

interface AgegroupOrderGroup {
  agegroupId: string;
  agegroupName: string;
  agegroupColor: string | null;
  totalTeamCount: number;
  divisions: DivisionOrderItem[];
}

/**
 * Build Order tab — two-tier drag-sort: agegroups (block reorder) + divisions (within agegroup).
 * Persists a flat ProcessingOrderEntryDto[] to the cascade backend.
 */
@Component({
  selector: 'app-build-order-tab',
  standalone: true,
  imports: [CommonModule, DragDropModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './build-order-tab.component.html',
  styleUrl: './build-order-tab.component.scss',
})
export class BuildOrderTabComponent implements OnInit {
  private readonly cascadeSvc = inject(ScheduleCascadeService);
  private readonly toast = inject(ToastService);

  /** Agegroup metadata from parent — eliminates per-tab HTTP fetch. */
  readonly agegroupsInput = input<AgegroupWithDivisionsDto[]>([], { alias: 'agegroups' });

  readonly localOrder = signal<DivisionOrderItem[]>([]);
  private readonly baselineOrder = signal<string[]>([]);
  readonly isLoading = signal(false);
  readonly isSaving = signal(false);
  readonly contrastText = contrastText;

  /** Expand/collapse state for agegroup drill-down. */
  readonly expandedAgs = signal<Set<string>>(new Set());

  /** Agegroup color + team count lookup — derived synchronously from parent input. */
  private readonly agegroupMeta = computed(() => {
    const meta: Record<string, { color: string | null; teamCount: number; divTeamCounts: Record<string, number> }> = {};
    for (const ag of this.agegroupsInput()) {
      const divTeamCounts: Record<string, number> = {};
      for (const d of ag.divisions) {
        divTeamCounts[d.divId] = d.teamCount;
      }
      meta[ag.agegroupId] = {
        color: ag.color ?? null,
        teamCount: agTeamCount(ag),
        divTeamCounts,
      };
    }
    return meta;
  });

  readonly isDirty = computed(() => {
    const current = this.localOrder().map(i => i.divId);
    const baseline = this.baselineOrder();
    if (current.length !== baseline.length) return true;
    return current.some((id, i) => id !== baseline[i]);
  });

  /** Groups localOrder into agegroup blocks, preserving first-occurrence order. */
  readonly agGroups = computed((): AgegroupOrderGroup[] => {
    const items = this.localOrder();
    if (items.length === 0) return [];

    const groupMap = new Map<string, AgegroupOrderGroup>();
    const groupOrder: string[] = [];

    for (const item of items) {
      let group = groupMap.get(item.agegroupId);
      if (!group) {
        group = {
          agegroupId: item.agegroupId,
          agegroupName: item.agegroupName,
          agegroupColor: item.agegroupColor,
          totalTeamCount: 0,
          divisions: [],
        };
        groupMap.set(item.agegroupId, group);
        groupOrder.push(item.agegroupId);
      }
      group.divisions.push(item);
      group.totalTeamCount += item.teamCount;
    }

    return groupOrder.map(id => groupMap.get(id)!);
  });

  readonly summaryLabel = computed(() => {
    const groups = this.agGroups();
    if (groups.length === 0) return 'No divisions';
    const totalDivs = groups.reduce((s, g) => s + g.divisions.length, 0);
    return `${groups.length} agegroup${groups.length !== 1 ? 's' : ''} \u00b7 ${totalDivs} division${totalDivs !== 1 ? 's' : ''}`;
  });

  ngOnInit(): void {
    this.reload();
  }

  reload(): void {
    this.isLoading.set(true);

    this.cascadeSvc.getProcessingOrder().subscribe({
      next: (entries) => {
        this.buildOrderFromEntries(entries);
        this.isLoading.set(false);
      },
      error: () => {
        this.buildOrderFromEntries([]);
        this.isLoading.set(false);
      },
    });
  }

  private buildOrderFromEntries(entries: ProcessingOrderEntryDto[]): void {
    const cascade = this.cascadeSvc.cascade();
    if (!cascade) return;

    const waveMap = this.cascadeSvc.getWaveMap();
    const allDivs: DivisionOrderItem[] = [];

    for (const ag of cascade.agegroups) {
      for (const div of ag.divisions) {
        const meta = this.agegroupMeta()[ag.agegroupId];
        allDivs.push({
          divId: div.divisionId,
          divName: div.divisionName,
          agegroupId: ag.agegroupId,
          agegroupName: ag.agegroupName,
          agegroupColor: meta?.color ?? null,
          teamCount: meta?.divTeamCounts?.[div.divisionId] ?? 0,
          wave: waveMap[div.divisionId] ?? 1,
        });
      }
    }

    if (entries.length > 0) {
      // Sort by persisted order, then re-cluster by agegroup
      const orderMap = new Map(entries.map(e => [e.divisionId, e.sortOrder]));
      allDivs.sort((a, b) =>
        (orderMap.get(a.divId) ?? 9999) - (orderMap.get(b.divId) ?? 9999)
      );

      // Re-cluster: ensure agegroups are contiguous (handles legacy data)
      const seen = new Map<string, DivisionOrderItem[]>();
      const agOrder: string[] = [];
      for (const div of allDivs) {
        if (!seen.has(div.agegroupId)) {
          seen.set(div.agegroupId, []);
          agOrder.push(div.agegroupId);
        }
        seen.get(div.agegroupId)!.push(div);
      }
      allDivs.length = 0;
      for (const agId of agOrder) {
        allDivs.push(...seen.get(agId)!);
      }
    } else {
      // Default: agegroup name → wave → division name
      allDivs.sort((a, b) => {
        const agCmp = a.agegroupName.localeCompare(b.agegroupName);
        if (agCmp !== 0) return agCmp;
        if (a.wave !== b.wave) return a.wave - b.wave;
        return a.divName.localeCompare(b.divName);
      });
    }

    this.localOrder.set(allDivs);
    this.baselineOrder.set(allDivs.map(i => i.divId));
  }

  // ── Expand / Collapse ──

  toggleAg(agId: string): void {
    const expanded = new Set(this.expandedAgs());
    expanded.has(agId) ? expanded.delete(agId) : expanded.add(agId);
    this.expandedAgs.set(expanded);
  }

  isAgExpanded(agId: string): boolean {
    return this.expandedAgs().has(agId);
  }

  // ── Drag-drop: Agegroup level ──

  onDropAgegroup(event: CdkDragDrop<AgegroupOrderGroup[]>): void {
    if (event.previousIndex === event.currentIndex) return;

    const groups = this.agGroups();
    const agIds = groups.map(g => g.agegroupId);
    moveItemInArray(agIds, event.previousIndex, event.currentIndex);

    // Rebuild flat localOrder: walk agegroups in new order
    const groupLookup = new Map(groups.map(g => [g.agegroupId, g]));
    const newOrder: DivisionOrderItem[] = [];
    for (const agId of agIds) {
      newOrder.push(...groupLookup.get(agId)!.divisions);
    }

    this.localOrder.set(newOrder);
  }

  // ── Drag-drop: Division level (within agegroup) ──

  onDropDivision(event: CdkDragDrop<DivisionOrderItem[]>, agegroupId: string): void {
    if (event.previousIndex === event.currentIndex) return;

    const all = this.localOrder().slice();

    // Find indices belonging to this agegroup
    const agIndices: number[] = [];
    const agItems: DivisionOrderItem[] = [];
    for (let i = 0; i < all.length; i++) {
      if (all[i].agegroupId === agegroupId) {
        agIndices.push(i);
        agItems.push(all[i]);
      }
    }

    moveItemInArray(agItems, event.previousIndex, event.currentIndex);

    // Write reordered items back into the flat array
    for (let i = 0; i < agIndices.length; i++) {
      all[agIndices[i]] = agItems[i];
    }

    this.localOrder.set(all);
  }

  // ── Save to DB ──

  save(): void {
    const entries: ProcessingOrderEntryDto[] = this.localOrder().map((item, i) => ({
      divisionId: item.divId,
      sortOrder: i,
    }));

    this.isSaving.set(true);
    this.cascadeSvc.saveProcessingOrder(entries).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.baselineOrder.set(this.localOrder().map(i => i.divId));
        this.toast.show('Processing order saved', 'success');
      },
      error: () => {
        this.isSaving.set(false);
        this.toast.show('Failed to save processing order', 'danger');
      },
    });
  }

  discard(): void {
    const baseline = this.baselineOrder();
    const idxMap = new Map(baseline.map((id, i) => [id, i]));
    const sorted = this.localOrder().slice().sort(
      (a, b) => (idxMap.get(a.divId) ?? 9999) - (idxMap.get(b.divId) ?? 9999)
    );
    this.localOrder.set(sorted);
  }

  resetToDefault(): void {
    this.isSaving.set(true);
    this.cascadeSvc.deleteProcessingOrder().subscribe({
      next: () => {
        this.isSaving.set(false);
        this.buildOrderFromEntries([]);
        this.toast.show('Processing order reset to default', 'success');
      },
      error: () => {
        this.isSaving.set(false);
        this.toast.show('Failed to reset processing order', 'danger');
      },
    });
  }
}
