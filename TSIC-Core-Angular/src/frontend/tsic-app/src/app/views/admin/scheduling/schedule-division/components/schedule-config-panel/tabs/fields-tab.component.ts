import {
  ChangeDetectionStrategy, Component, computed, inject, input, OnInit, signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ToastService } from '@shared-ui/toast.service';
import { ScheduleCascadeService } from '../../schedule-config/schedule-cascade.service';
import { TimeslotService } from '../../../../timeslots/services/timeslot.service';
import { agTeamCount, contrastText } from '../../../../shared/utils/scheduling-helpers';
import type { AgegroupCanvasReadinessDto, AgegroupWithDivisionsDto, EventFieldSummaryDto } from '@core/api';

interface FieldColumn {
  fieldId: string;
  fieldName: string;
}

interface AgRow {
  agegroupId: string;
  agegroupName: string;
  color: string | null;
  teamCount: number;
}

@Component({
  selector: 'app-fields-tab',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './fields-tab.component.html',
  styleUrl: './fields-tab.component.scss',
})
export class FieldsTabComponent implements OnInit {
  private readonly cascadeSvc = inject(ScheduleCascadeService);
  private readonly timeslotSvc = inject(TimeslotService);
  private readonly toast = inject(ToastService);

  /** Agegroup metadata from parent — eliminates per-tab HTTP fetch. */
  readonly agegroupsInput = input<AgegroupWithDivisionsDto[]>([], { alias: 'agegroups' });

  readonly isLoading = signal(false);
  readonly isSaving = signal(false);
  readonly contrastText = contrastText;

  /** Event fields (all available fields) */
  readonly eventFields = signal<FieldColumn[]>([]);

  /** Local assignment matrix: agegroupId → Set<fieldId> */
  readonly localAssignments = signal<Record<string, Set<string>>>({});

  /** Baseline (saved) assignments for dirty tracking */
  private readonly baselineAssignments = signal<Record<string, Set<string>>>({});

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

  readonly isDirty = computed(() => {
    const current = this.localAssignments();
    const baseline = this.baselineAssignments();
    const agIds = new Set([...Object.keys(current), ...Object.keys(baseline)]);

    for (const agId of agIds) {
      const curSet = current[agId] ?? new Set<string>();
      const baseSet = baseline[agId] ?? new Set<string>();
      if (curSet.size !== baseSet.size) return true;
      for (const f of curSet) {
        if (!baseSet.has(f)) return true;
      }
    }
    return false;
  });

  readonly summaryLabel = computed(() => {
    const ags = this.agegroups();
    const fields = this.eventFields();
    if (ags.length === 0) return 'No agegroups configured';
    if (fields.length === 0) return 'No fields available';
    const assigned = Object.values(this.localAssignments())
      .reduce((sum, set) => sum + set.size, 0);
    return `${assigned} assignment${assigned !== 1 ? 's' : ''} across ${fields.length} field${fields.length !== 1 ? 's' : ''}`;
  });

  ngOnInit(): void {
    this.reload();
  }

  // ── Data loading ──

  reload(): void {
    this.isLoading.set(true);
    this.timeslotSvc.getReadiness().subscribe({
      next: (response) => {
        // Event fields
        this.eventFields.set(
          (response.eventFields ?? []).map(f => ({
            fieldId: f.fieldId,
            fieldName: f.fieldName,
          }))
        );

        // Build assignments from readiness
        const assignments: Record<string, Set<string>> = {};
        for (const ag of response.agegroups) {
          assignments[ag.agegroupId] = new Set(ag.fieldIds ?? []);
        }

        this.localAssignments.set(assignments);
        this.baselineAssignments.set(this.cloneAssignments(assignments));
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
        this.toast.show('Failed to load field assignments', 'danger');
      },
    });
  }

  // ── User actions ──

  isChecked(agegroupId: string, fieldId: string): boolean {
    return this.localAssignments()[agegroupId]?.has(fieldId) ?? false;
  }

  toggleAssignment(agegroupId: string, fieldId: string): void {
    const current = this.localAssignments();
    const updated = { ...current };
    const set = new Set(updated[agegroupId] ?? []);

    if (set.has(fieldId)) {
      set.delete(fieldId);
    } else {
      set.add(fieldId);
    }

    updated[agegroupId] = set;
    this.localAssignments.set(updated);
  }

  toggleFieldColumn(fieldId: string): void {
    const ags = this.agegroups();
    const current = this.localAssignments();
    const allChecked = ags.every(ag => current[ag.agegroupId]?.has(fieldId));

    const updated = { ...current };
    for (const ag of ags) {
      const set = new Set(updated[ag.agegroupId] ?? []);
      if (allChecked) {
        set.delete(fieldId);
      } else {
        set.add(fieldId);
      }
      updated[ag.agegroupId] = set;
    }
    this.localAssignments.set(updated);
  }

  toggleAgRow(agegroupId: string): void {
    const fields = this.eventFields();
    const current = this.localAssignments();
    const set = current[agegroupId] ?? new Set<string>();
    const allChecked = fields.every(f => set.has(f.fieldId));

    const updated = { ...current };
    const newSet = new Set(set);
    for (const f of fields) {
      if (allChecked) {
        newSet.delete(f.fieldId);
      } else {
        newSet.add(f.fieldId);
      }
    }
    updated[agegroupId] = newSet;
    this.localAssignments.set(updated);
  }

  fieldColumnState(fieldId: string): 'all' | 'some' | 'none' {
    const ags = this.agegroups();
    if (ags.length === 0) return 'none';
    const current = this.localAssignments();
    let count = 0;
    for (const ag of ags) {
      if (current[ag.agegroupId]?.has(fieldId)) count++;
    }
    if (count === 0) return 'none';
    if (count === ags.length) return 'all';
    return 'some';
  }

  agRowState(agegroupId: string): 'all' | 'some' | 'none' {
    const fields = this.eventFields();
    if (fields.length === 0) return 'none';
    const set = this.localAssignments()[agegroupId] ?? new Set<string>();
    let count = 0;
    for (const f of fields) {
      if (set.has(f.fieldId)) count++;
    }
    if (count === 0) return 'none';
    if (count === fields.length) return 'all';
    return 'some';
  }

  // ── Save ──

  save(): void {
    const current = this.localAssignments();
    const entries = Object.entries(current).map(([agegroupId, fieldSet]) => ({
      agegroupId,
      fieldIds: [...fieldSet],
    }));

    this.isSaving.set(true);
    this.timeslotSvc.saveFieldAssignments({ entries }).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.baselineAssignments.set(this.cloneAssignments(current));
        this.toast.show('Field assignments saved', 'success');
      },
      error: () => {
        this.isSaving.set(false);
        this.toast.show('Failed to save field assignments', 'danger');
      },
    });
  }

  // ── Helpers ──

  private cloneAssignments(source: Record<string, Set<string>>): Record<string, Set<string>> {
    const clone: Record<string, Set<string>> = {};
    for (const [k, v] of Object.entries(source)) {
      clone[k] = new Set(v);
    }
    return clone;
  }
}
