import {
  Component, ChangeDetectionStrategy, computed, inject, OnInit, signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ToastService } from '@shared-ui/toast.service';
import { ScheduleCascadeService } from '../../schedule-config/schedule-cascade.service';
import type {
  AgegroupCascadeDto,
  DivisionCascadeDto,
} from '@core/api';

/**
 * Build Rules tab — 3 scalar cascade properties:
 * Game Guarantee, Game Placement (H/V), Between Round Rest (0/1/2).
 *
 * Layout:
 * - Event defaults row (always visible, editable)
 * - Agegroup override rows (expandable, shows divisions when expanded)
 * - Inherited values = muted, overridden = bold
 */
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

  readonly cascade = this.cascadeSvc.cascade;
  readonly isSaving = signal(false);

  /** Track which agegroups are expanded to show division overrides */
  readonly expandedAgs = signal<Set<string>>(new Set());

  // ── Event defaults (editable form state) ──
  readonly eventPlacement = signal<string>('H');
  readonly eventRest = signal<number>(0);
  readonly eventGuarantee = signal<number>(3);

  /** Whether event defaults have been modified from the loaded snapshot */
  readonly eventDirty = computed(() => {
    const snap = this.cascade();
    if (!snap) return false;
    const d = snap.eventDefaults;
    return d.gamePlacement !== this.eventPlacement()
      || d.betweenRoundRows !== this.eventRest()
      || d.gameGuarantee !== this.eventGuarantee();
  });

  ngOnInit(): void {
    this.reload();
  }

  /** Sync form state from cascade snapshot. Called on init and after reset. */
  reload(): void {
    const snap = this.cascade();
    if (snap) {
      this.eventPlacement.set(snap.eventDefaults.gamePlacement);
      this.eventRest.set(snap.eventDefaults.betweenRoundRows);
      this.eventGuarantee.set(snap.eventDefaults.gameGuarantee);
    }
  }

  // ── Event defaults save ──

  saveEventDefaults(): void {
    this.isSaving.set(true);
    this.cascadeSvc.saveEventDefaults({
      gamePlacement: this.eventPlacement(),
      betweenRoundRows: this.eventRest(),
      gameGuarantee: this.eventGuarantee(),
    }).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.toast.show('Event defaults saved', 'success');
      },
      error: () => {
        this.isSaving.set(false);
        this.toast.show('Failed to save event defaults', 'danger');
      },
    });
  }

  // ── Agegroup overrides ──

  toggleAg(agId: string): void {
    const expanded = new Set(this.expandedAgs());
    if (expanded.has(agId)) {
      expanded.delete(agId);
    } else {
      expanded.add(agId);
    }
    this.expandedAgs.set(expanded);
  }

  isAgExpanded(agId: string): boolean {
    return this.expandedAgs().has(agId);
  }

  hasAgOverride(ag: AgegroupCascadeDto): boolean {
    return ag.gamePlacementOverride != null
      || ag.betweenRoundRowsOverride != null
      || ag.gameGuaranteeOverride != null;
  }

  saveAgOverride(ag: AgegroupCascadeDto, field: string, value: string | number | null): void {
    this.isSaving.set(true);

    const request: Record<string, unknown> = {};
    // Preserve existing overrides, update the changed field
    request['gamePlacement'] = field === 'placement' ? value : (ag.gamePlacementOverride ?? null);
    request['betweenRoundRows'] = field === 'rest' ? value : (ag.betweenRoundRowsOverride ?? null);
    request['gameGuarantee'] = field === 'guarantee' ? value : (ag.gameGuaranteeOverride ?? null);

    this.cascadeSvc.saveAgegroupOverride(ag.agegroupId, request as any).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.toast.show(`${ag.agegroupName} override saved`, 'success');
      },
      error: () => {
        this.isSaving.set(false);
        this.toast.show(`Failed to save ${ag.agegroupName} override`, 'danger');
      },
    });
  }

  clearAgOverride(ag: AgegroupCascadeDto): void {
    this.isSaving.set(true);
    this.cascadeSvc.saveAgegroupOverride(ag.agegroupId, {
      gamePlacement: null,
      betweenRoundRows: undefined,
      gameGuarantee: undefined,
    }).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.toast.show(`${ag.agegroupName} reset to event defaults`, 'success');
      },
      error: () => {
        this.isSaving.set(false);
        this.toast.show(`Failed to clear ${ag.agegroupName} overrides`, 'danger');
      },
    });
  }

  // ── Division overrides ──

  hasDivOverride(div: DivisionCascadeDto): boolean {
    return div.gamePlacementOverride != null
      || div.betweenRoundRowsOverride != null
      || div.gameGuaranteeOverride != null;
  }

  saveDivOverride(div: DivisionCascadeDto, field: string, value: string | number | null): void {
    this.isSaving.set(true);

    const request: Record<string, unknown> = {};
    request['gamePlacement'] = field === 'placement' ? value : (div.gamePlacementOverride ?? null);
    request['betweenRoundRows'] = field === 'rest' ? value : (div.betweenRoundRowsOverride ?? null);
    request['gameGuarantee'] = field === 'guarantee' ? value : (div.gameGuaranteeOverride ?? null);

    this.cascadeSvc.saveDivisionOverride(div.divisionId, request as any).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.toast.show(`${div.divisionName} override saved`, 'success');
      },
      error: () => {
        this.isSaving.set(false);
        this.toast.show(`Failed to save ${div.divisionName} override`, 'danger');
      },
    });
  }

  clearDivOverride(div: DivisionCascadeDto): void {
    this.isSaving.set(true);
    this.cascadeSvc.saveDivisionOverride(div.divisionId, {
      gamePlacement: null,
      betweenRoundRows: undefined,
      gameGuarantee: undefined,
    }).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.toast.show(`${div.divisionName} reset to agegroup defaults`, 'success');
      },
      error: () => {
        this.isSaving.set(false);
        this.toast.show(`Failed to clear ${div.divisionName} overrides`, 'danger');
      },
    });
  }

  // ── Display helpers ──

  placementLabel(val: string): string {
    return val === 'V' ? 'Vertical' : 'Horizontal';
  }

  restLabel(val: number): string {
    switch (val) {
      case 0: return 'None';
      case 1: return '1 game';
      case 2: return '2 games';
      default: return `${val}`;
    }
  }
}
