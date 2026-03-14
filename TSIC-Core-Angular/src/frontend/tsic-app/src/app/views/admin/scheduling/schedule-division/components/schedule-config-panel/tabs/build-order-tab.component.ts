import {
  ChangeDetectionStrategy, Component, computed, inject, OnInit, signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { DragDropModule, CdkDragDrop, moveItemInArray } from '@angular/cdk/drag-drop';
import { ToastService } from '@shared-ui/toast.service';
import { ScheduleCascadeService } from '../../schedule-config/schedule-cascade.service';
import { ScheduleDivisionService } from '../../../services/schedule-division.service';
import { contrastText, agTeamCount } from '../../../../shared/utils/scheduling-helpers';
import type { ProcessingOrderEntryDto } from '@core/api';

interface DivisionOrderItem {
  divId: string;
  divName: string;
  agegroupId: string;
  agegroupName: string;
  agegroupColor: string | null;
  teamCount: number;
  wave: number;
  playDates: string[];
}

interface DayGroup {
  isoDate: string;
  dow: string;
  dateLabel: string;
  waveGroups: { wave: number; items: DivisionOrderItem[] }[];
  singleWave: boolean;
}

/**
 * Build Order tab — DB-persisted drag-sort of divisions within day+wave groups.
 * Replaces the old processing-order-section that used ephemeral localStorage.
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
  private readonly divSvc = inject(ScheduleDivisionService);
  private readonly toast = inject(ToastService);

  readonly localOrder = signal<DivisionOrderItem[]>([]);
  private readonly baselineOrder = signal<string[]>([]);
  readonly isLoading = signal(false);
  readonly isSaving = signal(false);
  readonly contrastText = contrastText;

  private readonly agegroupMeta = signal<Record<string, { color: string | null; teamCount: number; divTeamCounts: Record<string, number> }>>({});

  readonly isDirty = computed(() => {
    const current = this.localOrder().map(i => i.divId);
    const baseline = this.baselineOrder();
    if (current.length !== baseline.length) return true;
    return current.some((id, i) => id !== baseline[i]);
  });

  readonly dayGroups = computed((): DayGroup[] => {
    const items = this.localOrder();
    if (items.length === 0) return [];

    const dateSet = new Map<string, string>();
    for (const item of items) {
      for (const d of item.playDates) {
        if (!dateSet.has(d)) {
          dateSet.set(d, this.getDowForDate(d));
        }
      }
    }

    const sortedDates = [...dateSet.entries()].sort(([a], [b]) => a.localeCompare(b));

    return sortedDates.map(([isoDate, dow]) => {
      const dayItems = items.filter(i => i.playDates.includes(isoDate));

      const waveMap = new Map<number, DivisionOrderItem[]>();
      for (const item of dayItems) {
        const arr = waveMap.get(item.wave) ?? [];
        arr.push(item);
        waveMap.set(item.wave, arr);
      }

      const waveGroups = [...waveMap.entries()]
        .sort(([a], [b]) => a - b)
        .map(([wave, waveItems]) => ({ wave, items: waveItems }));

      return {
        isoDate,
        dow,
        dateLabel: this.formatDateLabel(isoDate, dow),
        waveGroups,
        singleWave: waveGroups.length <= 1,
      };
    });
  });

  readonly summaryLabel = computed(() => {
    const items = this.localOrder();
    if (items.length === 0) return 'No divisions';
    const dayCount = new Set(items.flatMap(i => i.playDates)).size;
    if (dayCount <= 1) return `${items.length} divisions`;
    return `${items.length} divisions across ${dayCount} days`;
  });

  ngOnInit(): void {
    this.isLoading.set(true);
    // Load agegroup metadata first so colors are available when building order
    this.divSvc.getAgegroups().subscribe(ags => {
      const meta: Record<string, { color: string | null; teamCount: number; divTeamCounts: Record<string, number> }> = {};
      for (const ag of ags) {
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
      this.agegroupMeta.set(meta);
      this.reload();
    });
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
      const playDates = Object.keys(ag.wavesByDate).map(d => this.toDateOnly(d));
      for (const div of ag.divisions) {
        const divPlayDates = Object.keys(div.effectiveWavesByDate).map(d => this.toDateOnly(d));
        const meta = this.agegroupMeta()[ag.agegroupId];
        allDivs.push({
          divId: div.divisionId,
          divName: div.divisionName,
          agegroupId: ag.agegroupId,
          agegroupName: ag.agegroupName,
          agegroupColor: meta?.color ?? null,
          teamCount: meta?.divTeamCounts?.[div.divisionId] ?? 0,
          wave: waveMap[div.divisionId] ?? 1,
          playDates: divPlayDates.length > 0 ? divPlayDates : playDates,
        });
      }
    }

    // Sort by persisted order if available, else wave → AG name → div name
    if (entries.length > 0) {
      const orderMap = new Map(entries.map(e => [e.divisionId, e.sortOrder]));
      allDivs.sort((a, b) => {
        const aIdx = orderMap.get(a.divId) ?? 9999;
        const bIdx = orderMap.get(b.divId) ?? 9999;
        return aIdx - bIdx;
      });
    } else {
      allDivs.sort((a, b) => {
        if (a.wave !== b.wave) return a.wave - b.wave;
        const agCmp = a.agegroupName.localeCompare(b.agegroupName);
        return agCmp !== 0 ? agCmp : a.divName.localeCompare(b.divName);
      });
    }

    this.localOrder.set(allDivs);
    this.baselineOrder.set(allDivs.map(i => i.divId));
  }

  // ── Drag-drop ──

  onDrop(event: CdkDragDrop<DivisionOrderItem[]>, isoDate: string, wave: number): void {
    if (event.previousIndex === event.currentIndex) return;

    const all = this.localOrder().slice();
    const matchIndices: number[] = [];
    const matchItems: DivisionOrderItem[] = [];

    for (let i = 0; i < all.length; i++) {
      if (all[i].wave === wave && all[i].playDates.includes(isoDate)) {
        matchIndices.push(i);
        matchItems.push(all[i]);
      }
    }

    moveItemInArray(matchItems, event.previousIndex, event.currentIndex);

    for (let i = 0; i < matchIndices.length; i++) {
      all[matchIndices[i]] = matchItems[i];
    }

    this.localOrder.set(all);
  }

  // ── Save to DB ──

  save(): void {
    const dayGs = this.dayGroups();
    const seen = new Set<string>();
    const entries: ProcessingOrderEntryDto[] = [];
    let sortOrder = 0;

    for (const day of dayGs) {
      for (const wg of day.waveGroups) {
        for (const item of wg.items) {
          if (!seen.has(item.divId)) {
            seen.add(item.divId);
            entries.push({ divisionId: item.divId, sortOrder: sortOrder++ });
          }
        }
      }
    }

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

  // ── Helpers ──

  dropListId(isoDate: string, wave: number): string {
    return `tab-order-${isoDate}-w${wave}`;
  }

  /** Normalize DateTime strings (e.g. "2026-03-15T00:00:00") to date-only "2026-03-15" */
  private toDateOnly(dateStr: string): string {
    return dateStr.length > 10 ? dateStr.substring(0, 10) : dateStr;
  }

  private getDowForDate(isoDate: string): string {
    const d = new Date(this.toDateOnly(isoDate) + 'T12:00:00');
    return d.toLocaleDateString('en-US', { weekday: 'long' });
  }

  private formatDateLabel(isoDate: string, dow: string): string {
    const datePart = this.toDateOnly(isoDate);
    const parts = datePart.split('-');
    if (parts.length === 3) {
      return `${dow} ${parts[1]}/${parts[2]}/${parts[0]}`;
    }
    return `${dow} ${datePart}`;
  }
}
