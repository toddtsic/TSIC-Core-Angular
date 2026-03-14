import {
  ChangeDetectionStrategy, Component, computed, inject, input, OnInit, signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastService } from '@shared-ui/toast.service';
import { ScheduleCascadeService } from '../../schedule-config/schedule-cascade.service';
import { TimeslotService } from '../../../../timeslots/services/timeslot.service';
import { DateAdderModalComponent } from './date-adder-modal.component';
import { agTeamCount, contrastText } from '../../../../shared/utils/scheduling-helpers';
import type { AgegroupCanvasReadinessDto, AgegroupWithDivisionsDto, BulkDateAgegroupEntry } from '@core/api';

interface DateColumn {
  isoDate: string;
  dow: string;
  label: string;
  isNew: boolean;
}

interface AgRow {
  agegroupId: string;
  agegroupName: string;
  color: string | null;
  teamCount: number;
}

@Component({
  selector: 'app-dates-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, DateAdderModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dates-tab.component.html',
  styleUrl: './dates-tab.component.scss',
})
export class DatesTabComponent implements OnInit {
  private readonly cascadeSvc = inject(ScheduleCascadeService);
  private readonly timeslotSvc = inject(TimeslotService);
  private readonly toast = inject(ToastService);

  /** Agegroup metadata from parent — eliminates per-tab HTTP fetch. */
  readonly agegroupsInput = input<AgegroupWithDivisionsDto[]>([], { alias: 'agegroups' });

  readonly isLoading = signal(false);
  readonly isSaving = signal(false);
  readonly contrastText = contrastText;

  /** Readiness snapshot keyed by agegroupId */
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

  /** Local assignment matrix: agegroupId → Set<isoDate> */
  readonly localAssignments = signal<Record<string, Set<string>>>({});

  /** Baseline (saved) assignments for dirty tracking */
  private readonly baselineAssignments = signal<Record<string, Set<string>>>({});

  /** Manually added dates not yet in readiness */
  readonly addedDates = signal<string[]>([]);

  /** Whether the date adder modal is open */
  readonly showDateAdder = signal(false);

  /** Which date column is being edited (null = none) */
  readonly editingDate = signal<string | null>(null);

  /** New date value during inline edit */
  editNewDate = '';

  // ── Derived ──

  readonly agegroups = computed((): AgRow[] => {
    const cascade = this.cascadeSvc.cascade();
    if (!cascade) return [];
    const meta = this.agegroupMeta();
    return cascade.agegroups.map(ag => ({
      agegroupId: ag.agegroupId,
      agegroupName: ag.agegroupName,
      color: meta[ag.agegroupId]?.color ?? null,
      teamCount: meta[ag.agegroupId]?.teamCount ?? 0,
    }));
  });

  readonly dateColumns = computed((): DateColumn[] => {
    const map = this.readinessMap();
    const added = this.addedDates();
    const dateInfo = new Map<string, { dow: string }>();

    // Gather dates from readiness
    for (const r of Object.values(map)) {
      if (!r.gameDays) continue;
      for (const gd of r.gameDays) {
        const iso = gd.date.substring(0, 10);
        if (!dateInfo.has(iso)) {
          dateInfo.set(iso, { dow: gd.dow });
        }
      }
    }

    // Build columns from readiness dates
    const columns: DateColumn[] = [...dateInfo.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([iso, info]) => ({
        isoDate: iso,
        dow: info.dow,
        label: this.formatDateLabel(iso, info.dow),
        isNew: false,
      }));

    // Add manually-added dates not already in readiness
    for (const iso of added) {
      if (!dateInfo.has(iso)) {
        const dow = this.getDowForDate(iso);
        columns.push({
          isoDate: iso,
          dow,
          label: this.formatDateLabel(iso, dow),
          isNew: true,
        });
      }
    }

    columns.sort((a, b) => a.isoDate.localeCompare(b.isoDate));
    return columns;
  });

  readonly isDirty = computed(() => {
    const current = this.localAssignments();
    const baseline = this.baselineAssignments();
    const agIds = new Set([...Object.keys(current), ...Object.keys(baseline)]);

    for (const agId of agIds) {
      const curSet = current[agId] ?? new Set<string>();
      const baseSet = baseline[agId] ?? new Set<string>();
      if (curSet.size !== baseSet.size) return true;
      for (const d of curSet) {
        if (!baseSet.has(d)) return true;
      }
    }
    return false;
  });

  readonly summaryLabel = computed(() => {
    const ags = this.agegroups();
    const dates = this.dateColumns();
    if (ags.length === 0) return 'No agegroups configured';
    if (dates.length === 0) return 'No dates assigned';
    const assigned = Object.values(this.localAssignments())
      .reduce((sum, set) => sum + set.size, 0);
    return `${assigned} assignment${assigned !== 1 ? 's' : ''} across ${dates.length} date${dates.length !== 1 ? 's' : ''}`;
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
        const assignments: Record<string, Set<string>> = {};

        for (const ag of response.agegroups) {
          map[ag.agegroupId] = ag;
          const dates = new Set<string>();
          if (ag.gameDays) {
            for (const gd of ag.gameDays) {
              dates.add(gd.date.substring(0, 10));
            }
          }
          assignments[ag.agegroupId] = dates;
        }

        this.readinessMap.set(map);
        this.localAssignments.set(assignments);
        this.baselineAssignments.set(this.cloneAssignments(assignments));
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
        this.toast.show('Failed to load date assignments', 'danger');
      },
    });
  }

  // ── User actions ──

  isChecked(agegroupId: string, isoDate: string): boolean {
    return this.localAssignments()[agegroupId]?.has(isoDate) ?? false;
  }

  toggleAssignment(agegroupId: string, isoDate: string): void {
    const current = this.localAssignments();
    const updated = { ...current };
    const set = new Set(updated[agegroupId] ?? []);

    if (set.has(isoDate)) {
      set.delete(isoDate);
    } else {
      set.add(isoDate);
    }

    updated[agegroupId] = set;
    this.localAssignments.set(updated);
  }

  toggleDateColumn(isoDate: string): void {
    const ags = this.agegroups();
    const current = this.localAssignments();
    const allChecked = ags.every(ag => current[ag.agegroupId]?.has(isoDate));

    const updated = { ...current };
    for (const ag of ags) {
      const set = new Set(updated[ag.agegroupId] ?? []);
      if (allChecked) {
        set.delete(isoDate);
      } else {
        set.add(isoDate);
      }
      updated[ag.agegroupId] = set;
    }
    this.localAssignments.set(updated);
  }

  toggleAgRow(agegroupId: string): void {
    const dates = this.dateColumns();
    const current = this.localAssignments();
    const set = current[agegroupId] ?? new Set<string>();
    const allChecked = dates.every(d => set.has(d.isoDate));

    const updated = { ...current };
    const newSet = new Set(set);
    for (const d of dates) {
      if (allChecked) {
        newSet.delete(d.isoDate);
      } else {
        newSet.add(d.isoDate);
      }
    }
    updated[agegroupId] = newSet;
    this.localAssignments.set(updated);
  }

  dateColumnState(isoDate: string): 'all' | 'some' | 'none' {
    const ags = this.agegroups();
    if (ags.length === 0) return 'none';
    const current = this.localAssignments();
    let count = 0;
    for (const ag of ags) {
      if (current[ag.agegroupId]?.has(isoDate)) count++;
    }
    if (count === 0) return 'none';
    if (count === ags.length) return 'all';
    return 'some';
  }

  agRowState(agegroupId: string): 'all' | 'some' | 'none' {
    const dates = this.dateColumns();
    if (dates.length === 0) return 'none';
    const set = this.localAssignments()[agegroupId] ?? new Set<string>();
    let count = 0;
    for (const d of dates) {
      if (set.has(d.isoDate)) count++;
    }
    if (count === 0) return 'none';
    if (count === dates.length) return 'all';
    return 'some';
  }

  /** Existing date ISO strings for the modal's duplicate prevention. */
  readonly existingDateIsos = computed(() =>
    this.dateColumns().map(d => d.isoDate)
  );

  onDatesAdded(newDates: string[]): void {
    if (newDates.length === 0) {
      this.showDateAdder.set(false);
      return;
    }
    this.addedDates.set([...this.addedDates(), ...newDates]);
    this.showDateAdder.set(false);
  }

  removeNewDate(isoDate: string): void {
    // Remove from addedDates and uncheck all agegroups for this date
    this.addedDates.set(this.addedDates().filter(d => d !== isoDate));

    const current = this.localAssignments();
    const updated = { ...current };
    for (const agId of Object.keys(updated)) {
      const set = new Set(updated[agId]);
      set.delete(isoDate);
      updated[agId] = set;
    }
    this.localAssignments.set(updated);
  }

  editDate(isoDate: string): void {
    this.editingDate.set(isoDate);
    this.editNewDate = isoDate;
  }

  confirmEditDate(): void {
    const oldDate = this.editingDate();
    if (!oldDate || !this.editNewDate) return;

    // No-op if same date
    if (this.editNewDate === oldDate) {
      this.editingDate.set(null);
      return;
    }

    // Client-side duplicate check
    if (this.dateColumns().some(d => d.isoDate === this.editNewDate)) {
      this.toast.show('Date already exists', 'warning');
      return;
    }

    this.isSaving.set(true);
    this.timeslotSvc.cascadeEditDate({
      oldDate: oldDate,
      newDate: this.editNewDate,
    }).subscribe({
      next: (result) => {
        this.isSaving.set(false);
        this.editingDate.set(null);
        const parts: string[] = [];
        if (result.dateRowsUpdated) parts.push(`${result.dateRowsUpdated} date rows`);
        if (result.wavesMigrated) parts.push(`${result.wavesMigrated} waves`);
        if (result.gamesUpdated) parts.push(`${result.gamesUpdated} games`);
        if (result.fieldTimeslotsCreated) parts.push(`${result.fieldTimeslotsCreated} field timeslots created`);
        this.toast.show(
          parts.length > 0
            ? `Date changed — ${parts.join(', ')} updated`
            : 'Date changed',
          'success'
        );
        this.reload();
      },
      error: (err: HttpErrorResponse) => {
        this.isSaving.set(false);
        if (err.status === 409) {
          this.toast.show(err.error?.message ?? 'Date already exists', 'warning');
        } else {
          this.toast.show('Failed to change date', 'danger');
        }
      },
    });
  }

  cancelEditDate(): void {
    this.editingDate.set(null);
  }

  deleteDate(isoDate: string): void {
    const dow = this.getDowForDate(isoDate);
    const label = this.formatDateLabel(isoDate, dow);
    if (!confirm(`Remove ${label}?\n\nAll agegroup assignments, wave configurations, and scheduled games for this date will be permanently deleted.`)) {
      return;
    }

    this.isSaving.set(true);
    this.timeslotSvc.cascadeDeleteDate({ date: isoDate }).subscribe({
      next: (result) => {
        this.isSaving.set(false);
        const parts: string[] = [];
        if (result.dateRowsDeleted) parts.push(`${result.dateRowsDeleted} date rows`);
        if (result.gamesDeleted) parts.push(`${result.gamesDeleted} games`);
        this.toast.show(
          parts.length > 0
            ? `Date removed — ${parts.join(', ')} deleted`
            : 'Date removed',
          'success'
        );
        this.reload();
      },
      error: () => {
        this.isSaving.set(false);
        this.toast.show('Failed to remove date', 'danger');
      },
    });
  }

  discard(): void {
    this.localAssignments.set(this.cloneAssignments(this.baselineAssignments()));
    this.addedDates.set([]);
    this.showDateAdder.set(false);
  }

  // ── Save ──

  apply(): void {
    const current = this.localAssignments();
    const baseline = this.baselineAssignments();
    const ags = this.agegroups();
    const dates = this.dateColumns();

    // Build per-date change sets
    const changes: { isoDate: string; added: string[]; removed: string[] }[] = [];
    for (const date of dates) {
      const added: string[] = [];
      const removed: string[] = [];
      for (const ag of ags) {
        const isNow = current[ag.agegroupId]?.has(date.isoDate) ?? false;
        const wasBefore = baseline[ag.agegroupId]?.has(date.isoDate) ?? false;
        if (isNow && !wasBefore) added.push(ag.agegroupId);
        if (!isNow && wasBefore) removed.push(ag.agegroupId);
      }
      if (added.length > 0 || removed.length > 0) {
        changes.push({ isoDate: date.isoDate, added, removed });
      }
    }

    if (changes.length === 0) return;

    this.isSaving.set(true);
    let completed = 0;
    let hasError = false;

    for (const change of changes) {
      // Get timing defaults from readiness (use first agegroup's config for this date, else defaults)
      const defaults = this.getDateDefaults(change.isoDate);

      const entries: BulkDateAgegroupEntry[] = change.added.map(agId => ({
        agegroupId: agId,
      }));

      this.timeslotSvc.bulkAssignDate({
        gDate: change.isoDate,
        startTime: defaults.startTime,
        gamestartInterval: defaults.gsi,
        maxGamesPerField: defaults.maxGamesPerField,
        entries,
        agegroupIds: change.added.length > 0 ? change.added : null,
        removedAgegroupIds: change.removed.length > 0 ? change.removed : null,
      }).subscribe({
        next: () => {
          completed++;
          if (completed === changes.length) {
            this.isSaving.set(false);
            if (!hasError) {
              this.toast.show('Date assignments updated', 'success');
              // Reload to get fresh state
              this.addedDates.set([]);
              this.reload();
            }
          }
        },
        error: () => {
          hasError = true;
          completed++;
          if (completed === changes.length) {
            this.isSaving.set(false);
            this.toast.show('Some date assignments failed to save', 'danger');
          }
        },
      });
    }
  }

  // ── Helpers ──

  private getDateDefaults(isoDate: string): { startTime: string; gsi: number; maxGamesPerField: number } {
    const map = this.readinessMap();
    for (const r of Object.values(map)) {
      if (!r.gameDays) continue;
      for (const gd of r.gameDays) {
        if (gd.date.substring(0, 10) === isoDate) {
          return { startTime: gd.startTime, gsi: gd.gsi, maxGamesPerField: 5 };
        }
      }
    }
    return { startTime: '08:00 AM', gsi: 60, maxGamesPerField: 5 };
  }

  private getDowForDate(isoDate: string): string {
    const d = new Date(isoDate + 'T12:00:00');
    return d.toLocaleDateString('en-US', { weekday: 'long' });
  }

  private formatDateLabel(isoDate: string, dow: string): string {
    const parts = isoDate.split('-');
    if (parts.length === 3) {
      return `${dow.substring(0, 3)} ${parts[1]}/${parts[2]}/${parts[0]}`;
    }
    return `${dow.substring(0, 3)} ${isoDate}`;
  }

  private cloneAssignments(source: Record<string, Set<string>>): Record<string, Set<string>> {
    const clone: Record<string, Set<string>> = {};
    for (const [k, v] of Object.entries(source)) {
      clone[k] = new Set(v);
    }
    return clone;
  }
}
