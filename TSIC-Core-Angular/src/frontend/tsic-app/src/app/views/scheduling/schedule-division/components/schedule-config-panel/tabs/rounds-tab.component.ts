import {
  ChangeDetectionStrategy, Component, computed, inject, input, OnInit, signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ToastService } from '@shared-ui/toast.service';
import { ScheduleCascadeService } from '../../schedule-config/schedule-cascade.service';
import { TimeslotService } from '../../../../timeslots/services/timeslot.service';
import { agTeamCount, contrastText } from '../../../../shared/utils/scheduling-helpers';
import type { AgegroupCanvasReadinessDto, AgegroupWithDivisionsDto, GameDayDto, TimeslotDateDto } from '@core/api';

interface RoundRow {
  agegroupId: string;
  agegroupName: string;
  color: string | null;
  teamCount: number;
  isMultiDay: boolean;
  totalRounds: number;
  /** Per-date start rounds (derived from gameDays or TLSD) */
  startRounds: Record<string, number>;
}

interface DateCol {
  isoDate: string;
  dow: string;
  label: string;
}

@Component({
  selector: 'app-rounds-tab',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './rounds-tab.component.html',
  styleUrl: './rounds-tab.component.scss',
})
export class RoundsTabComponent implements OnInit {
  private readonly cascadeSvc = inject(ScheduleCascadeService);
  private readonly timeslotSvc = inject(TimeslotService);
  private readonly toast = inject(ToastService);

  /** Agegroup metadata from parent — eliminates per-tab HTTP fetch. */
  readonly agegroupsInput = input<AgegroupWithDivisionsDto[]>([], { alias: 'agegroups' });

  readonly isLoading = signal(false);
  readonly isSaving = signal(false);
  readonly contrastText = contrastText;

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

  /** Readiness per agegroup */
  private readonly readinessMap = signal<Record<string, AgegroupCanvasReadinessDto>>({});

  /** Local editable start-round markers: agegroupId → { isoDate → startRound } */
  readonly localRounds = signal<Record<string, Record<string, number>>>({});

  /** Baseline for dirty tracking */
  private readonly baselineRounds = signal<Record<string, Record<string, number>>>({});

  // ── Derived ──

  readonly dateColumns = computed((): DateCol[] => {
    const map = this.readinessMap();
    const dateInfo = new Map<string, string>();

    for (const r of Object.values(map)) {
      if (!r.gameDays) continue;
      for (const gd of r.gameDays) {
        const iso = gd.date.substring(0, 10);
        if (!dateInfo.has(iso)) {
          dateInfo.set(iso, gd.dow);
        }
      }
    }

    return [...dateInfo.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([iso, dow]) => ({
        isoDate: iso,
        dow,
        label: this.formatDateLabel(iso, dow),
      }));
  });

  readonly rows = computed((): RoundRow[] => {
    const cascade = this.cascadeSvc.cascade();
    const map = this.readinessMap();
    const local = this.localRounds();
    const meta = this.agegroupMeta();
    if (!cascade) return [];

    return cascade.agegroups.map(ag => {
      const r = map[ag.agegroupId];
      const gameDays = r?.gameDays ?? [];
      const isMultiDay = gameDays.length > 1;
      const totalRounds = r?.totalRounds ?? 0;
      const startRounds = local[ag.agegroupId] ?? {};

      return {
        agegroupId: ag.agegroupId,
        agegroupName: ag.agegroupName,
        color: meta[ag.agegroupId]?.color ?? null,
        teamCount: meta[ag.agegroupId]?.teamCount ?? 0,
        isMultiDay,
        totalRounds,
        startRounds,
      };
    });
  });

  readonly isDirty = computed(() => {
    const current = this.localRounds();
    const baseline = this.baselineRounds();
    return JSON.stringify(current) !== JSON.stringify(baseline);
  });

  readonly hasMultiDay = computed(() => this.rows().some(r => r.isMultiDay));

  ngOnInit(): void {
    this.reload();
  }

  // ── Data loading ──

  reload(): void {
    this.isLoading.set(true);
    this.timeslotSvc.getReadiness().subscribe({
      next: (response) => {
        const map: Record<string, AgegroupCanvasReadinessDto> = {};
        const rounds: Record<string, Record<string, number>> = {};

        for (const ag of response.agegroups) {
          map[ag.agegroupId] = ag;

          // Derive start-round markers from gameDays
          const startRounds: Record<string, number> = {};
          if (ag.gameDays && ag.gameDays.length > 0) {
            const sorted = [...ag.gameDays].sort((a, b) =>
              a.date.localeCompare(b.date)
            );
            let cumulativeRound = 1;
            for (const gd of sorted) {
              const iso = gd.date.substring(0, 10);
              startRounds[iso] = cumulativeRound;
              cumulativeRound += gd.roundCount || 1;
            }
          }
          rounds[ag.agegroupId] = startRounds;
        }

        this.readinessMap.set(map);
        this.localRounds.set(rounds);
        this.baselineRounds.set(JSON.parse(JSON.stringify(rounds)));
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
        this.toast.show('Failed to load round distribution', 'danger');
      },
    });
  }

  // ── User actions ──

  hasDate(agegroupId: string, isoDate: string): boolean {
    const r = this.readinessMap()[agegroupId];
    if (!r?.gameDays) return false;
    return r.gameDays.some(gd => gd.date.substring(0, 10) === isoDate);
  }

  getStartRound(agegroupId: string, isoDate: string): number {
    return this.localRounds()[agegroupId]?.[isoDate] ?? 1;
  }

  setStartRound(agegroupId: string, isoDate: string, value: number): void {
    const current = this.localRounds();
    const updated = { ...current };
    updated[agegroupId] = { ...(updated[agegroupId] ?? {}), [isoDate]: Math.max(1, value) };
    this.localRounds.set(updated);
  }

  // ── Save ──

  save(): void {
    const current = this.localRounds();
    const baseline = this.baselineRounds();

    // Find changed agegroups
    const changedAgIds: string[] = [];
    for (const agId of Object.keys(current)) {
      if (JSON.stringify(current[agId]) !== JSON.stringify(baseline[agId])) {
        changedAgIds.push(agId);
      }
    }

    if (changedAgIds.length === 0) return;

    this.isSaving.set(true);
    let completed = 0;
    let hasError = false;

    // For each changed agegroup, load its TLSD config to get ai values, then update
    for (const agId of changedAgIds) {
      this.timeslotSvc.getConfiguration(agId).subscribe({
        next: (config) => {
          this.updateRoundMarkers(agId, config.dates ?? [], current[agId] ?? {})
            .then(() => {
              completed++;
              if (completed === changedAgIds.length) this.finishSave(hasError);
            })
            .catch(() => {
              hasError = true;
              completed++;
              if (completed === changedAgIds.length) this.finishSave(hasError);
            });
        },
        error: () => {
          hasError = true;
          completed++;
          if (completed === changedAgIds.length) this.finishSave(hasError);
        },
      });
    }
  }

  private async updateRoundMarkers(
    agId: string,
    tlsdRows: TimeslotDateDto[],
    newStartRounds: Record<string, number>,
  ): Promise<void> {
    // Match TLSD rows by date and update Rnd values
    for (const row of tlsdRows) {
      const iso = new Date(row.gDate).toISOString().substring(0, 10);
      const newRnd = newStartRounds[iso];
      if (newRnd !== undefined && newRnd !== row.rnd) {
        await new Promise<void>((resolve, reject) => {
          this.timeslotSvc.editDate({
            ai: row.ai,
            gDate: row.gDate,
            rnd: newRnd,
          }).subscribe({
            next: () => resolve(),
            error: () => reject(),
          });
        });
      }
    }
  }

  private finishSave(hasError: boolean): void {
    this.isSaving.set(false);
    if (hasError) {
      this.toast.show('Some round markers failed to save', 'danger');
    } else {
      this.toast.show('Round distribution saved', 'success');
      this.reload();
    }
  }

  // ── Helpers ──

  private formatDateLabel(isoDate: string, dow: string): string {
    const parts = isoDate.split('-');
    if (parts.length === 3) {
      return `${dow.substring(0, 3)} ${parts[1]}/${parts[2]}`;
    }
    return `${dow.substring(0, 3)} ${isoDate}`;
  }
}
