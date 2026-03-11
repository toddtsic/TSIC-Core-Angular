import {
  Component, ChangeDetectionStrategy, computed, inject, signal
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ToastService } from '@shared-ui/toast.service';
import { ScheduleCascadeService } from '../../schedule-config/schedule-cascade.service';

interface WaveRow {
  id: string;
  name: string;
  level: 'agegroup' | 'division';
  agegroupId: string;
  /** Per-date wave values (ISO date string → wave number) */
  waves: Record<string, number>;
  /** Whether this row has any explicit overrides */
  hasOverride: boolean;
}

/**
 * Waves tab — per-date wave assignments for divisions, grouped by agegroup.
 *
 * Layout:
 * - Summary when all divisions are Wave 1 (most common)
 * - Grid: divisions as rows (grouped by agegroup), play-dates as columns
 * - Agegroup header row sets default for all divisions in that AG
 * - Division rows override specific divisions
 * - Color-coded cells by wave number
 */
@Component({
  selector: 'app-waves-tab',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './waves-tab.component.html',
  styleUrl: './waves-tab.component.scss',
})
export class WavesTabComponent {
  private readonly cascadeSvc = inject(ScheduleCascadeService);
  private readonly toast = inject(ToastService);

  readonly cascade = this.cascadeSvc.cascade;
  readonly isSaving = signal(false);
  readonly isEditing = signal(false);

  /** All unique play dates across all agegroups, sorted chronologically */
  readonly playDates = computed((): { iso: string; label: string }[] => {
    const snap = this.cascade();
    if (!snap) return [];

    const dateSet = new Set<string>();
    for (const ag of snap.agegroups) {
      for (const dateKey of Object.keys(ag.wavesByDate)) {
        dateSet.add(dateKey);
      }
      for (const div of ag.divisions) {
        for (const dateKey of Object.keys(div.effectiveWavesByDate)) {
          dateSet.add(dateKey);
        }
      }
    }

    return [...dateSet]
      .sort()
      .map(iso => ({ iso, label: this.formatDateLabel(iso) }));
  });

  /** Whether all divisions have Wave 1 on all dates */
  readonly allWaveOne = computed(() => this.cascadeSvc.hasNoWaves());

  /** Rows for the wave grid: agegroup headers + division rows */
  readonly rows = computed((): WaveRow[] => {
    const snap = this.cascade();
    if (!snap) return [];

    const result: WaveRow[] = [];

    for (const ag of snap.agegroups) {
      // Agegroup header row
      result.push({
        id: ag.agegroupId,
        name: ag.agegroupName,
        level: 'agegroup',
        agegroupId: ag.agegroupId,
        waves: ag.wavesByDate,
        hasOverride: Object.keys(ag.wavesByDate).length > 0,
      });

      // Division rows
      for (const div of ag.divisions) {
        // Determine if division has its own override (not just inherited from AG)
        const divOverrideWaves: Record<string, number> = {};
        let hasDivOverride = false;

        for (const [dateKey, wave] of Object.entries(div.effectiveWavesByDate)) {
          divOverrideWaves[dateKey] = wave;
          // It's an override if the division's effective wave differs from agegroup
          const agWave = ag.wavesByDate[dateKey] ?? 1;
          if (wave !== agWave) hasDivOverride = true;
        }

        result.push({
          id: div.divisionId,
          name: div.divisionName,
          level: 'division',
          agegroupId: ag.agegroupId,
          waves: div.effectiveWavesByDate,
          hasOverride: hasDivOverride,
        });
      }
    }

    return result;
  });

  /** Get wave for a row + date. Returns 1 as default. */
  getWave(row: WaveRow, dateIso: string): number {
    return row.waves[dateIso] ?? 1;
  }

  /** Whether a division's wave is inherited from agegroup (not overridden) */
  isInherited(row: WaveRow, dateIso: string): boolean {
    if (row.level === 'agegroup') return false;

    const snap = this.cascade();
    if (!snap) return true;

    const ag = snap.agegroups.find(a => a.agegroupId === row.agegroupId);
    if (!ag) return true;

    const div = ag.divisions.find(d => d.divisionId === row.id);
    if (!div) return true;

    // Find the raw division wave data — check if there's a division-level override
    // If effectiveWavesByDate matches agWave, it's inherited
    const agWave = ag.wavesByDate[dateIso] ?? 1;
    const divWave = div.effectiveWavesByDate[dateIso] ?? 1;
    return divWave === agWave;
  }

  /** Cycle wave value for a cell: 1 → 2 → 3 → 1 */
  cycleWave(row: WaveRow, dateIso: string): void {
    const current = this.getWave(row, dateIso);
    const next = current >= 3 ? 1 : current + 1;

    this.isSaving.set(true);

    if (row.level === 'agegroup') {
      // Update agegroup wave for this date
      const snap = this.cascade();
      const ag = snap?.agegroups.find(a => a.agegroupId === row.id);
      if (!ag) return;

      const wavesByDate: Record<string, number> = { ...ag.wavesByDate };
      if (next === 1) {
        delete wavesByDate[dateIso]; // Wave 1 = default, no need to store
      } else {
        wavesByDate[dateIso] = next;
      }

      // Convert to string-keyed byte dict for API
      const apiWaves: Record<string, number> = {};
      for (const [k, v] of Object.entries(wavesByDate)) {
        apiWaves[k] = v;
      }

      this.cascadeSvc.saveAgegroupOverride(row.id, {
        gamePlacement: ag.gamePlacementOverride ?? undefined,
        betweenRoundRows: ag.betweenRoundRowsOverride ?? undefined,
        gameGuarantee: ag.gameGuaranteeOverride ?? undefined,
        wavesByDate: Object.keys(apiWaves).length > 0 ? apiWaves : undefined,
      }).subscribe({
        next: () => {
          this.isSaving.set(false);
        },
        error: () => {
          this.isSaving.set(false);
          this.toast.show('Failed to save wave assignment', 'danger');
        },
      });
    } else {
      // Update division wave for this date
      const snap = this.cascade();
      const ag = snap?.agegroups.find(a => a.agegroupId === row.agegroupId);
      const div = ag?.divisions.find(d => d.divisionId === row.id);
      if (!div) return;

      // Build division-level wave overrides
      // We need to figure out which dates have division-specific overrides
      const wavesByDate: Record<string, number> = {};

      // Start with existing effective waves that differ from AG
      for (const [dateKey, wave] of Object.entries(div.effectiveWavesByDate)) {
        const agWave = ag!.wavesByDate[dateKey] ?? 1;
        if (wave !== agWave) {
          wavesByDate[dateKey] = wave;
        }
      }

      // Apply the new change
      const agWave = ag!.wavesByDate[dateIso] ?? 1;
      if (next !== agWave) {
        wavesByDate[dateIso] = next;
      } else {
        delete wavesByDate[dateIso]; // Matches AG = inherit
      }

      this.cascadeSvc.saveDivisionOverride(row.id, {
        gamePlacement: div.gamePlacementOverride ?? undefined,
        betweenRoundRows: div.betweenRoundRowsOverride ?? undefined,
        gameGuarantee: div.gameGuaranteeOverride ?? undefined,
        wavesByDate: Object.keys(wavesByDate).length > 0 ? wavesByDate : undefined,
      }).subscribe({
        next: () => {
          this.isSaving.set(false);
        },
        error: () => {
          this.isSaving.set(false);
          this.toast.show('Failed to save wave assignment', 'danger');
        },
      });
    }
  }

  /** Clear a division's wave override for a date (revert to agegroup) */
  clearDivWave(row: WaveRow, dateIso: string): void {
    if (row.level !== 'division') return;

    const snap = this.cascade();
    const ag = snap?.agegroups.find(a => a.agegroupId === row.agegroupId);
    const div = ag?.divisions.find(d => d.divisionId === row.id);
    if (!div) return;

    // Build wave overrides without this date
    const wavesByDate: Record<string, number> = {};
    for (const [dateKey, wave] of Object.entries(div.effectiveWavesByDate)) {
      if (dateKey === dateIso) continue;
      const agWave = ag!.wavesByDate[dateKey] ?? 1;
      if (wave !== agWave) {
        wavesByDate[dateKey] = wave;
      }
    }

    this.isSaving.set(true);
    this.cascadeSvc.saveDivisionOverride(row.id, {
      gamePlacement: div.gamePlacementOverride ?? undefined,
      betweenRoundRows: div.betweenRoundRowsOverride ?? undefined,
      gameGuarantee: div.gameGuaranteeOverride ?? undefined,
      wavesByDate: Object.keys(wavesByDate).length > 0 ? wavesByDate : undefined,
    }).subscribe({
      next: () => {
        this.isSaving.set(false);
      },
      error: () => {
        this.isSaving.set(false);
        this.toast.show('Failed to clear wave override', 'danger');
      },
    });
  }

  /** CSS class for wave badge */
  waveClass(wave: number): string {
    switch (wave) {
      case 1: return 'wave-1';
      case 2: return 'wave-2';
      case 3: return 'wave-3';
      default: return 'wave-1';
    }
  }

  private formatDateLabel(iso: string): string {
    // Parse ISO date and format as "Mon 06/07"
    const d = new Date(iso + 'T12:00:00');
    const dow = d.toLocaleDateString('en-US', { weekday: 'short' });
    const month = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${dow} ${month}/${day}`;
  }
}
