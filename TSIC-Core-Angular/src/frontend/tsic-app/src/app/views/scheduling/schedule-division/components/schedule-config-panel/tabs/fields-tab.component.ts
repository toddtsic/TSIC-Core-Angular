import {
  ChangeDetectionStrategy, Component, computed, inject, input, OnInit, signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ToastService } from '@shared-ui/toast.service';
import { ScheduleCascadeService } from '../../schedule-config/schedule-cascade.service';
import { TimeslotService } from '../../../../timeslots/services/timeslot.service';
import { agTeamCount, contrastText } from '../../../../shared/utils/scheduling-helpers';
import type { AgegroupCanvasReadinessDto, AgegroupWithDivisionsDto, EventFieldSummaryDto, DivisionFieldAssignmentEntry } from '@core/api';

interface FieldColumn {
  fieldId: string;
  fieldName: string;
}

interface AgRow {
  agegroupId: string;
  agegroupName: string;
  color: string | null;
  teamCount: number;
  divisions: DivRow[];
}

interface DivRow {
  divId: string;
  divName: string;
  agegroupId: string;
  teamCount: number;
  color: string | null;
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

  /** Division-level overrides: divId → Set<fieldId> (only populated when different from agegroup) */
  readonly divisionAssignments = signal<Record<string, Set<string>>>({});

  /** Which agegroups are expanded to show divisions */
  readonly expandedAgegroups = signal<Set<string>>(new Set());

  /** Baseline (saved) assignments for dirty tracking */
  private readonly baselineAssignments = signal<Record<string, Set<string>>>({});
  private readonly baselineDivAssignments = signal<Record<string, Set<string>>>({});

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
    const agInput = this.agegroupsInput();
    return cascade.agegroups.map(ag => {
      const inputAg = agInput.find(a => a.agegroupId === ag.agegroupId);
      return {
        agegroupId: ag.agegroupId,
        agegroupName: ag.agegroupName,
        color: meta[ag.agegroupId]?.color ?? null,
        teamCount: meta[ag.agegroupId]?.teamCount ?? 0,
        divisions: (inputAg?.divisions ?? []).map(d => ({
          divId: d.divId,
          divName: d.divName,
          agegroupId: ag.agegroupId,
          teamCount: d.teamCount ?? 0,
          color: meta[ag.agegroupId]?.color ?? null,
        })),
      };
    });
  });

  readonly isDirty = computed(() => {
    // Check agegroup assignments
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
    // Check division assignments
    const curDiv = this.divisionAssignments();
    const baseDiv = this.baselineDivAssignments();
    const divIds = new Set([...Object.keys(curDiv), ...Object.keys(baseDiv)]);
    for (const divId of divIds) {
      const curSet = curDiv[divId] ?? new Set<string>();
      const baseSet = baseDiv[divId] ?? new Set<string>();
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
    const divOverrides = Object.keys(this.divisionAssignments()).length;
    const overrideNote = divOverrides > 0 ? ` (${divOverrides} division override${divOverrides !== 1 ? 's' : ''})` : '';
    return `${assigned} assignment${assigned !== 1 ? 's' : ''} across ${fields.length} field${fields.length !== 1 ? 's' : ''}${overrideNote}`;
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

        // Agegroup-level assignments
        const assignments: Record<string, Set<string>> = {};
        for (const ag of response.agegroups) {
          assignments[ag.agegroupId] = new Set(ag.fieldIds ?? []);
        }
        this.localAssignments.set(assignments);
        this.baselineAssignments.set(this.cloneAssignments(assignments));

        // Division-level overrides
        const divAssignments: Record<string, Set<string>> = {};
        const perDiv = (response as any).fieldIdsPerDivision as Record<string, string[]> | null;
        if (perDiv) {
          for (const [divId, fieldIds] of Object.entries(perDiv)) {
            divAssignments[divId] = new Set(fieldIds);
          }
        }
        this.divisionAssignments.set(divAssignments);
        this.baselineDivAssignments.set(this.cloneAssignments(divAssignments));

        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
        this.toast.show('Failed to load field assignments', 'danger');
      },
    });
  }

  // ── Expand/collapse ──

  isExpanded(agegroupId: string): boolean {
    return this.expandedAgegroups().has(agegroupId);
  }

  toggleExpand(agegroupId: string): void {
    const expanded = new Set(this.expandedAgegroups());
    if (expanded.has(agegroupId)) {
      expanded.delete(agegroupId);
    } else {
      expanded.add(agegroupId);
    }
    this.expandedAgegroups.set(expanded);
  }

  // ── Agegroup-level actions ──

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

  // ── Division-level actions ──

  /** Division check: has explicit override → use it; otherwise inherit from agegroup. */
  isDivChecked(div: DivRow, fieldId: string): boolean {
    const divOverride = this.divisionAssignments()[div.divId];
    if (divOverride) return divOverride.has(fieldId);
    // Inherit from agegroup
    return this.localAssignments()[div.agegroupId]?.has(fieldId) ?? false;
  }

  /** Does this division have an explicit override (different from agegroup)? */
  hasDivOverride(divId: string): boolean {
    return divId in this.divisionAssignments();
  }

  toggleDivAssignment(div: DivRow, fieldId: string): void {
    const current = this.divisionAssignments();
    const agFields = this.localAssignments()[div.agegroupId] ?? new Set<string>();

    // Get or create division override set
    let divSet: Set<string>;
    if (current[div.divId]) {
      divSet = new Set(current[div.divId]);
    } else {
      // First override: clone from agegroup
      divSet = new Set(agFields);
    }

    if (divSet.has(fieldId)) {
      divSet.delete(fieldId);
    } else {
      divSet.add(fieldId);
    }

    // If the override now matches the agegroup exactly, remove it (back to inherited)
    const updated = { ...current };
    if (this.setsEqual(divSet, agFields)) {
      delete updated[div.divId];
    } else {
      updated[div.divId] = divSet;
    }
    this.divisionAssignments.set(updated);
  }

  /** Reset a division's overrides back to inheriting from agegroup. */
  resetDivOverride(divId: string): void {
    const updated = { ...this.divisionAssignments() };
    delete updated[divId];
    this.divisionAssignments.set(updated);
  }

  // ── Save ──

  save(): void {
    const current = this.localAssignments();
    const entries = Object.entries(current).map(([agegroupId, fieldSet]) => ({
      agegroupId,
      fieldIds: [...fieldSet],
    }));

    // Division overrides
    const divCurrent = this.divisionAssignments();
    const divisionEntries: DivisionFieldAssignmentEntry[] = [];
    for (const ag of this.agegroups()) {
      for (const div of ag.divisions) {
        if (divCurrent[div.divId]) {
          divisionEntries.push({
            divisionId: div.divId,
            agegroupId: div.agegroupId,
            fieldIds: [...divCurrent[div.divId]],
          });
        }
      }
    }

    this.isSaving.set(true);
    this.timeslotSvc.saveFieldAssignments({
      entries,
      divisionEntries: divisionEntries.length > 0 ? divisionEntries : undefined,
    } as any).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.baselineAssignments.set(this.cloneAssignments(current));
        this.baselineDivAssignments.set(this.cloneAssignments(divCurrent));
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

  private setsEqual(a: Set<string>, b: Set<string>): boolean {
    if (a.size !== b.size) return false;
    for (const item of a) {
      if (!b.has(item)) return false;
    }
    return true;
  }
}
