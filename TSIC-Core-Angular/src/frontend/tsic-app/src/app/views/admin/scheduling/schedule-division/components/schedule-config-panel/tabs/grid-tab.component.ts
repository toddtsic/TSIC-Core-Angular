import {
  ChangeDetectionStrategy, Component, computed, inject, input, OnInit, signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ToastService } from '@shared-ui/toast.service';
import { ScheduleCascadeService } from '../../schedule-config/schedule-cascade.service';
import { TimeslotService } from '../../../../timeslots/services/timeslot.service';
import { agTeamCount, contrastText } from '../../../../shared/utils/scheduling-helpers';
import type { AgegroupCanvasReadinessDto, AgegroupWithDivisionsDto } from '@core/api';

interface GridRow {
  agegroupId: string;
  agegroupName: string;
  color: string | null;
  teamCount: number;
  isConfigured: boolean;
  gsi: number | null;
  startTime: string | null;
  maxGamesPerField: number | null;
  fieldCount: number;
  dateCount: number;
  totalGameSlots: number;
  totalRounds: number;
  gameGuarantee: number | null;
  daysOfWeek: string[];
}

@Component({
  selector: 'app-grid-tab',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './grid-tab.component.html',
  styleUrl: './grid-tab.component.scss',
})
export class GridTabComponent implements OnInit {
  private readonly cascadeSvc = inject(ScheduleCascadeService);
  private readonly timeslotSvc = inject(TimeslotService);
  private readonly toast = inject(ToastService);

  /** Agegroup metadata from parent — eliminates per-tab HTTP fetch. */
  readonly agegroupsInput = input<AgegroupWithDivisionsDto[]>([], { alias: 'agegroups' });

  readonly isLoading = signal(false);
  readonly contrastText = contrastText;

  private readonly readinessMap = signal<Record<string, AgegroupCanvasReadinessDto>>({});

  /** Agegroup color + team count lookup — derived synchronously from parent input. */
  private readonly agegroupMeta = computed(() => {
    const meta: Record<string, { color: string | null; teamCount: number }> = {};
    for (const ag of this.agegroupsInput()) {
      meta[ag.agegroupId] = {
        color: ag.color ?? null,
        teamCount: agTeamCount(ag),
      };
    }
    return meta;
  });

  // ── Derived ──

  readonly rows = computed((): GridRow[] => {
    const cascade = this.cascadeSvc.cascade();
    const map = this.readinessMap();
    const meta = this.agegroupMeta();
    if (!cascade) return [];

    return cascade.agegroups.map(ag => {
      const r = map[ag.agegroupId];
      return {
        agegroupId: ag.agegroupId,
        agegroupName: ag.agegroupName,
        color: meta[ag.agegroupId]?.color ?? null,
        teamCount: meta[ag.agegroupId]?.teamCount ?? 0,
        isConfigured: r?.isConfigured ?? false,
        gsi: r?.gamestartInterval ?? null,
        startTime: r?.startTime ?? null,
        maxGamesPerField: r?.maxGamesPerField ?? null,
        fieldCount: r?.fieldCount ?? 0,
        dateCount: r?.dateCount ?? 0,
        totalGameSlots: r?.totalGameSlots ?? 0,
        totalRounds: r?.totalRounds ?? 0,
        gameGuarantee: r?.gameGuarantee ?? ag.effectiveGameGuarantee,
        daysOfWeek: r?.daysOfWeek ?? [],
      };
    });
  });

  readonly summaryLabel = computed(() => {
    const r = this.rows();
    if (r.length === 0) return 'No agegroups';
    const configured = r.filter(x => x.isConfigured).length;
    return `${configured}/${r.length} agegroups configured`;
  });

  ngOnInit(): void {
    this.reload();
  }

  // ── Data loading ──

  reload(): void {
    this.isLoading.set(true);
    this.timeslotSvc.getReadiness().subscribe({
      next: (response) => {
        const map: Record<string, AgegroupCanvasReadinessDto> = {};
        for (const ag of response.agegroups) {
          map[ag.agegroupId] = ag;
        }
        this.readinessMap.set(map);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
        this.toast.show('Failed to load grid data', 'danger');
      },
    });
  }

  // ── Helpers ──

  capacityClass(row: GridRow): string {
    if (!row.isConfigured) return '';
    if (row.totalGameSlots === 0) return 'capacity-zero';
    // Check if capacity covers minimum needed games
    // Rough: totalRounds * (fieldCount for games) / 2 ≤ totalGameSlots
    if (row.totalRounds > 0 && row.totalGameSlots >= row.totalRounds) return 'capacity-ok';
    if (row.totalRounds > 0) return 'capacity-warn';
    return '';
  }
}
