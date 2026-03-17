import {
  Component, ChangeDetectionStrategy, computed, inject, input, OnInit, signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ToastService } from '@shared-ui/toast.service';
import { ScheduleCascadeService } from '../../schedule-config/schedule-cascade.service';
import { agTeamCount, contrastText } from '../../../../shared/utils/scheduling-helpers';
import type { AgegroupWithDivisionsDto } from '@core/api';

interface RuleValues {
  gamePlacement: string;
  betweenRoundRows: number;
  gameGuarantee: number;
}

interface AgOverride {
  gamePlacement: string | null;
  betweenRoundRows: number | null;
  gameGuarantee: number | null;
}

@Component({
  selector: 'app-build-rules-tab',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './build-rules-tab.component.html',
  styleUrl: './build-rules-tab.component.scss',
})
export class BuildRulesTabComponent implements OnInit {
  private readonly cascadeSvc = inject(ScheduleCascadeService);
  private readonly toast = inject(ToastService);

  readonly agegroupsInput = input<AgegroupWithDivisionsDto[]>([], { alias: 'agegroups' });
  readonly cascade = this.cascadeSvc.cascade;
  readonly isSaving = signal(false);
  readonly contrastText = contrastText;

  readonly agegroupMeta = computed(() => {
    const meta: Record<string, { color: string | null; teamCount: number; divTeamCounts: Record<string, number> }> = {};
    for (const ag of this.agegroupsInput()) {
      const divTeamCounts: Record<string, number> = {};
      for (const div of ag.divisions) {
        divTeamCounts[div.divId] = div.teamCount;
      }
      meta[ag.agegroupId] = { color: ag.color ?? null, teamCount: agTeamCount(ag), divTeamCounts };
    }
    return meta;
  });

  readonly expandedAgs = signal<Set<string>>(new Set());

  // ── Local editable state ──
  readonly eventDefaults = signal<RuleValues>({ gamePlacement: 'H', betweenRoundRows: 0, gameGuarantee: 3 });
  readonly agOverrides = signal<Record<string, AgOverride>>({});
  readonly divOverrides = signal<Record<string, AgOverride>>({});

  // ── Baseline snapshots for dirty tracking ──
  private baselineEvent = signal<string>('');
  private baselineAg = signal<string>('');
  private baselineDiv = signal<string>('');

  readonly isDirty = computed(() => {
    return JSON.stringify(this.eventDefaults()) !== this.baselineEvent()
      || JSON.stringify(this.agOverrides()) !== this.baselineAg()
      || JSON.stringify(this.divOverrides()) !== this.baselineDiv();
  });

  ngOnInit(): void {
    this.reload();
  }

  reload(): void {
    const snap = this.cascade();
    if (!snap) return;

    const ev: RuleValues = {
      gamePlacement: snap.eventDefaults.gamePlacement,
      betweenRoundRows: snap.eventDefaults.betweenRoundRows,
      gameGuarantee: snap.eventDefaults.gameGuarantee,
    };
    this.eventDefaults.set(ev);

    const agOvr: Record<string, AgOverride> = {};
    for (const ag of snap.agegroups) {
      if (ag.gamePlacementOverride != null || ag.betweenRoundRowsOverride != null || ag.gameGuaranteeOverride != null) {
        agOvr[ag.agegroupId] = {
          gamePlacement: ag.gamePlacementOverride ?? null,
          betweenRoundRows: ag.betweenRoundRowsOverride ?? null,
          gameGuarantee: ag.gameGuaranteeOverride ?? null,
        };
      }
    }
    this.agOverrides.set(agOvr);

    const divOvr: Record<string, AgOverride> = {};
    for (const ag of snap.agegroups) {
      for (const div of ag.divisions) {
        if (div.gamePlacementOverride != null || div.betweenRoundRowsOverride != null || div.gameGuaranteeOverride != null) {
          divOvr[div.divisionId] = {
            gamePlacement: div.gamePlacementOverride ?? null,
            betweenRoundRows: div.betweenRoundRowsOverride ?? null,
            gameGuarantee: div.gameGuaranteeOverride ?? null,
          };
        }
      }
    }
    this.divOverrides.set(divOvr);

    this.baselineEvent.set(JSON.stringify(ev));
    this.baselineAg.set(JSON.stringify(agOvr));
    this.baselineDiv.set(JSON.stringify(divOvr));
  }

  // ── Expand/collapse ──
  toggleAg(agId: string): void {
    const expanded = new Set(this.expandedAgs());
    expanded.has(agId) ? expanded.delete(agId) : expanded.add(agId);
    this.expandedAgs.set(expanded);
  }
  isAgExpanded(agId: string): boolean { return this.expandedAgs().has(agId); }

  // ── Event defaults editing ──
  updateEventField(field: keyof RuleValues, value: string | number): void {
    this.eventDefaults.set({ ...this.eventDefaults(), [field]: value });
  }

  // ── Agegroup override editing ──
  getAgOverride(agId: string): AgOverride | null { return this.agOverrides()[agId] ?? null; }

  resolvedAgValue(agId: string, field: keyof AgOverride): string | number {
    const ovr = this.agOverrides()[agId];
    if (ovr && ovr[field] != null) return ovr[field] as string | number;
    return this.eventDefaults()[field as keyof RuleValues];
  }

  hasAgOverride(agId: string): boolean { return agId in this.agOverrides(); }

  toggleAgField(agId: string, field: keyof AgOverride): void {
    const current = { ...this.agOverrides() };
    const ovr = current[agId] ?? { gamePlacement: null, betweenRoundRows: null, gameGuarantee: null };

    if (ovr[field] != null) {
      ovr[field] = null;
    } else {
      // Set to current effective value (which is the event default if no override)
      ovr[field] = this.eventDefaults()[field as keyof RuleValues] as any;
    }

    // If all nulls, remove the entry
    if (ovr.gamePlacement == null && ovr.betweenRoundRows == null && ovr.gameGuarantee == null) {
      delete current[agId];
    } else {
      current[agId] = { ...ovr };
    }
    this.agOverrides.set(current);
  }

  setAgField(agId: string, field: keyof AgOverride, value: string | number): void {
    const current = { ...this.agOverrides() };
    const ovr = current[agId] ?? { gamePlacement: null, betweenRoundRows: null, gameGuarantee: null };
    (ovr as any)[field] = value;
    current[agId] = { ...ovr };
    this.agOverrides.set(current);
  }

  clearAgOverride(agId: string): void {
    const current = { ...this.agOverrides() };
    delete current[agId];
    this.agOverrides.set(current);
  }

  // ── Division override editing ──
  getDivOverride(divId: string): AgOverride | null { return this.divOverrides()[divId] ?? null; }

  resolvedDivValue(divId: string, agId: string, field: keyof AgOverride): string | number {
    const ovr = this.divOverrides()[divId];
    if (ovr && ovr[field] != null) return ovr[field] as string | number;
    return this.resolvedAgValue(agId, field);
  }

  hasDivOverride(divId: string): boolean { return divId in this.divOverrides(); }

  toggleDivField(divId: string, agId: string, field: keyof AgOverride): void {
    const current = { ...this.divOverrides() };
    const ovr = current[divId] ?? { gamePlacement: null, betweenRoundRows: null, gameGuarantee: null };

    if (ovr[field] != null) {
      ovr[field] = null;
    } else {
      ovr[field] = this.resolvedAgValue(agId, field) as any;
    }

    if (ovr.gamePlacement == null && ovr.betweenRoundRows == null && ovr.gameGuarantee == null) {
      delete current[divId];
    } else {
      current[divId] = { ...ovr };
    }
    this.divOverrides.set(current);
  }

  setDivField(divId: string, field: keyof AgOverride, value: string | number): void {
    const current = { ...this.divOverrides() };
    const ovr = current[divId] ?? { gamePlacement: null, betweenRoundRows: null, gameGuarantee: null };
    (ovr as any)[field] = value;
    current[divId] = { ...ovr };
    this.divOverrides.set(current);
  }

  clearDivOverride(divId: string): void {
    const current = { ...this.divOverrides() };
    delete current[divId];
    this.divOverrides.set(current);
  }

  /** Auto-open a <select> after Angular renders it (one tick delay). */
  autoOpenSelect(event: MouseEvent): void {
    setTimeout(() => {
      const btn = event.target as HTMLElement;
      const cell = btn.closest('.cascade-cell');
      const select = cell?.querySelector('select') as HTMLSelectElement | null;
      if (select) {
        select.focus();
        try { select.showPicker(); } catch { /* older browsers */ }
      }
    });
  }

  // ── Save all (single batch request) ──
  saveAll(): void {
    this.isSaving.set(true);

    const agOverrides: Record<string, any> = {};
    for (const [agId, ovr] of Object.entries(this.agOverrides())) {
      agOverrides[agId] = {
        gamePlacement: ovr.gamePlacement,
        betweenRoundRows: ovr.betweenRoundRows,
        gameGuarantee: ovr.gameGuarantee,
      };
    }

    const divOverrides: Record<string, any> = {};
    for (const [divId, ovr] of Object.entries(this.divOverrides())) {
      divOverrides[divId] = {
        gamePlacement: ovr.gamePlacement,
        betweenRoundRows: ovr.betweenRoundRows,
        gameGuarantee: ovr.gameGuarantee,
      };
    }

    this.cascadeSvc.saveBatchBuildRules({
      eventDefaults: {
        gamePlacement: this.eventDefaults().gamePlacement,
        betweenRoundRows: this.eventDefaults().betweenRoundRows,
        gameGuarantee: this.eventDefaults().gameGuarantee,
      },
      agegroupOverrides: Object.keys(agOverrides).length > 0 ? agOverrides : null,
      divisionOverrides: Object.keys(divOverrides).length > 0 ? divOverrides : null,
    }).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.baselineEvent.set(JSON.stringify(this.eventDefaults()));
        this.baselineAg.set(JSON.stringify(this.agOverrides()));
        this.baselineDiv.set(JSON.stringify(this.divOverrides()));
        this.reload();
        this.toast.show('Build rules saved', 'success');
      },
      error: () => {
        this.isSaving.set(false);
        this.toast.show('Failed to save build rules', 'danger');
      },
    });
  }

  // ── Display helpers ──
  placementLabel(val: string): string { return val === 'V' ? 'Vertical' : 'Horizontal'; }
  restLabel(val: number): string {
    if (val === 0) return 'None (btb)';
    return `${val} slot${val !== 1 ? 's' : ''}`;
  }
}
