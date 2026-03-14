import {
  Component, ChangeDetectionStrategy, computed, inject, OnInit, signal
} from '@angular/core';
import { ToastService } from '@shared-ui/toast.service';
import { ScheduleCascadeService } from '../../schedule-config/schedule-cascade.service';
import { ScheduleDivisionService } from '../../../services/schedule-division.service';
import { agTeamCount, contrastText } from '../../../../shared/utils/scheduling-helpers';
import type { SaveBatchWavesRequest } from '@core/api';

interface WaveRow {
  id: string;
  name: string;
  level: 'agegroup' | 'division';
  agegroupId: string;
  color: string | null;
  teamCount: number;
}

/**
 * Per-entity wave state: entityId → (isoDate → waveNumber).
 * Agegroup waves set defaults; division waves override specific divisions.
 */
interface WaveState {
  agegroups: Record<string, Record<string, number>>;
  divisions: Record<string, Record<string, number>>;
}

/**
 * Waves tab — per-date wave assignments for divisions, grouped by agegroup.
 *
 * UX: native `<select>` dropdowns per cell, dirty-tracked batch save.
 * Max wave option per date column = current max for that column + 1 (no hard ceiling).
 */
@Component({
  selector: 'app-waves-tab',
  standalone: true,
  imports: [],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './waves-tab.component.html',
  styleUrl: './waves-tab.component.scss',
})
export class WavesTabComponent implements OnInit {
  private readonly cascadeSvc = inject(ScheduleCascadeService);
  private readonly divSvc = inject(ScheduleDivisionService);
  private readonly toast = inject(ToastService);

  readonly cascade = this.cascadeSvc.cascade;
  readonly isSaving = signal(false);
  readonly isEditing = signal(false);

  /** Agegroup color + team count lookup (fetched once on init) */
  private readonly agegroupMeta = signal<Record<string, { color: string | null; teamCount: number; divTeamCounts: Record<string, number> }>>({});

  readonly contrastText = contrastText;

  ngOnInit(): void {
    this.loadAgegroupMeta();
  }

  private loadAgegroupMeta(): void {
    this.divSvc.getAgegroups().subscribe({
      next: (ags) => {
        const meta: Record<string, { color: string | null; teamCount: number; divTeamCounts: Record<string, number> }> = {};
        for (const ag of ags) {
          const divTeamCounts: Record<string, number> = {};
          for (const div of ag.divisions) {
            divTeamCounts[div.divId] = div.teamCount;
          }
          meta[ag.agegroupId] = {
            color: ag.color ?? null,
            teamCount: agTeamCount(ag),
            divTeamCounts,
          };
        }
        this.agegroupMeta.set(meta);
      },
    });
  }

  // ── Dirty State Tracking ──

  /** Baseline (server) wave state — recomputed whenever cascade reloads.
   *  Divisions store OVERRIDES only (dates where div differs from AG). */
  readonly baselineWaves = computed((): WaveState => {
    const snap = this.cascade();
    if (!snap) return { agegroups: {}, divisions: {} };

    const agegroups: Record<string, Record<string, number>> = {};
    const divisions: Record<string, Record<string, number>> = {};

    for (const ag of snap.agegroups) {
      agegroups[ag.agegroupId] = { ...ag.wavesByDate };
      for (const div of ag.divisions) {
        const overrides: Record<string, number> = {};
        for (const [date, wave] of Object.entries(div.effectiveWavesByDate)) {
          const agWave = ag.wavesByDate[date] ?? 1;
          if (wave !== agWave) overrides[date] = wave;
        }
        divisions[div.divisionId] = overrides;
      }
    }
    return { agegroups, divisions };
  });

  /** Pending (local) edits — null when clean. */
  readonly pendingWaves = signal<WaveState | null>(null);

  /** Effective wave state: pending if dirty, otherwise baseline. */
  readonly effectiveWaves = computed((): WaveState => {
    return this.pendingWaves() ?? this.baselineWaves();
  });

  /** Whether there are unsaved changes. */
  readonly isDirty = computed(() => this.pendingWaves() !== null);

  // ── Derived Data ──

  /** All unique play dates across all agegroups, sorted chronologically. */
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

  /** Whether all divisions have Wave 1 on all dates. */
  readonly allWaveOne = computed(() => this.cascadeSvc.hasNoWaves());

  /** Rows for the wave grid: agegroup headers + division rows. */
  readonly rows = computed((): WaveRow[] => {
    const snap = this.cascade();
    if (!snap) return [];

    const meta = this.agegroupMeta();
    const result: WaveRow[] = [];
    for (const ag of snap.agegroups) {
      const agMeta = meta[ag.agegroupId];
      result.push({
        id: ag.agegroupId,
        name: ag.agegroupName,
        level: 'agegroup',
        agegroupId: ag.agegroupId,
        color: agMeta?.color ?? null,
        teamCount: agMeta?.teamCount ?? 0,
      });
      for (const div of ag.divisions) {
        result.push({
          id: div.divisionId,
          name: div.divisionName,
          level: 'division',
          agegroupId: ag.agegroupId,
          color: agMeta?.color ?? null,
          teamCount: agMeta?.divTeamCounts[div.divisionId] ?? 0,
        });
      }
    }
    return result;
  });

  /** Per-date max wave across all resolved values (reactive to pending edits). */
  readonly maxWaveByDate = computed((): Record<string, number> => {
    const result: Record<string, number> = {};
    const dates = this.playDates();
    const allRows = this.rows();

    for (const row of allRows) {
      for (const date of dates) {
        const wave = this.getWave(row, date.iso);
        result[date.iso] = Math.max(result[date.iso] ?? 1, wave);
      }
    }
    return result;
  });

  // ── Accessors ──

  /** Get wave for a row + date. Divisions fall through: override → AG → 1. */
  getWave(row: WaveRow, dateIso: string): number {
    const waves = this.effectiveWaves();
    if (row.level === 'agegroup') {
      return waves.agegroups[row.id]?.[dateIso] ?? 1;
    }
    const override = waves.divisions[row.id]?.[dateIso];
    if (override !== undefined) return override;
    return waves.agegroups[row.agegroupId]?.[dateIso] ?? 1;
  }

  /** Whether a division's wave is inherited (no override for that date). */
  isInherited(row: WaveRow, dateIso: string): boolean {
    if (row.level === 'agegroup') return false;
    return this.effectiveWaves().divisions[row.id]?.[dateIso] === undefined;
  }

  /** Whether a division row has any overrides. */
  hasOverride(row: WaveRow): boolean {
    if (row.level === 'agegroup') return false;
    const overrides = this.effectiveWaves().divisions[row.id];
    return !!overrides && Object.keys(overrides).length > 0;
  }

  /**
   * Dropdown options for a date column: [1..max+1].
   * max+1 so there's always room to grow one tier beyond current usage.
   */
  getWaveOptions(dateIso: string): number[] {
    const max = this.maxWaveByDate()[dateIso] ?? 1;
    return Array.from({ length: max + 1 }, (_, i) => i + 1);
  }

  /** CSS class for wave-number coloring. */
  waveClass(wave: number): string {
    if (wave <= 3) return `wave-${wave}`;
    return 'wave-high';
  }

  // ── Mutations (local only — no API call) ──

  onWaveChange(row: WaveRow, dateIso: string, newValue: number): void {
    const current = structuredClone(this.effectiveWaves());

    if (row.level === 'agegroup') {
      if (!current.agegroups[row.id]) current.agegroups[row.id] = {};
      current.agegroups[row.id][dateIso] = newValue;
    } else {
      // Division: if matches AG value, remove override (= inherit)
      const agWave = current.agegroups[row.agegroupId]?.[dateIso] ?? 1;
      if (!current.divisions[row.id]) current.divisions[row.id] = {};
      if (newValue === agWave) {
        delete current.divisions[row.id][dateIso];
      } else {
        current.divisions[row.id][dateIso] = newValue;
      }
    }

    this.pendingWaves.set(current);
  }

  // ── Save / Discard ──

  saveAll(): void {
    const pending = this.effectiveWaves();
    const request = this.buildSaveRequest(pending);

    this.isSaving.set(true);
    this.cascadeSvc.saveBatchWaves(request).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.pendingWaves.set(null);
        this.toast.show('Wave assignments saved', 'success');
      },
      error: () => {
        this.isSaving.set(false);
        this.toast.show('Failed to save wave assignments', 'danger');
      },
    });
  }

  discard(): void {
    this.pendingWaves.set(null);
  }

  // ── Private Helpers ──

  /**
   * Build API request from effective wave state.
   * - AG waves: omit wave-1 entries (1 is default, no need to store)
   * - Div waves: only include dates where division differs from its agegroup
   */
  private buildSaveRequest(state: WaveState): SaveBatchWavesRequest {
    const agegroupWaves: Record<string, Record<string, number>> = {};
    for (const [agId, dateWaves] of Object.entries(state.agegroups)) {
      const filtered: Record<string, number> = {};
      for (const [date, wave] of Object.entries(dateWaves)) {
        if (wave !== 1) filtered[date] = wave;
      }
      agegroupWaves[agId] = filtered;
    }

    const divisionWaves: Record<string, Record<string, number>> = {};
    for (const [divId, dateWaves] of Object.entries(state.divisions)) {
      // Find this division's agegroup from rows
      const row = this.rows().find(r => r.id === divId);
      const agId = row?.agegroupId;
      const agWaves = agId ? state.agegroups[agId] ?? {} : {};

      const filtered: Record<string, number> = {};
      for (const [date, wave] of Object.entries(dateWaves)) {
        const agWave = agWaves[date] ?? 1;
        if (wave !== agWave) filtered[date] = wave;
      }
      divisionWaves[divId] = filtered;
    }

    return { agegroupWaves, divisionWaves };
  }

  private formatDateLabel(iso: string): string {
    // Backend DateTime keys may include time component (e.g. "2026-06-15T00:00:00")
    const dateOnly = iso.includes('T') ? iso.split('T')[0] : iso;
    const d = new Date(dateOnly + 'T12:00:00');
    if (isNaN(d.getTime())) return iso; // fallback: show raw string
    const dow = d.toLocaleDateString('en-US', { weekday: 'short' });
    const month = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${dow} ${month}/${day}`;
  }
}
