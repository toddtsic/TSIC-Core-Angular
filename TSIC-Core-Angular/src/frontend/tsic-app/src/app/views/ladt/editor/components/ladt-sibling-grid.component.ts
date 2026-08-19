import { Component, Input, ChangeDetectionStrategy, signal, computed, OnChanges, SimpleChanges, CUSTOM_ELEMENTS_SCHEMA, input, output, viewChild, inject, afterNextRender, Injector, ElementRef } from '@angular/core';
import { DecimalPipe, NgClass } from '@angular/common';
import { GridAllModule, GridComponent } from '@syncfusion/ej2-angular-grids';
import type { LadtColumnDef } from '../configs/ladt-grid-columns';
import { countFrozenColumns } from '../configs/ladt-grid-columns';
import { InfoTooltipComponent } from '../../../../shared-ui/components/info-tooltip.component';

const WRAP_CELL_ATTRS: Record<string, string> = { class: 'wrap-cell' };
const NO_ATTRS: Record<string, string> = {};

export interface ParentBreadcrumb {
  name: string;
  level: number;
  id: string;
}

@Component({
  selector: 'app-ladt-sibling-grid',
  standalone: true,
  imports: [DecimalPipe, NgClass, GridAllModule, InfoTooltipComponent],
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sibling-grid-header">
      <i class="bi {{ levelIcon() }} me-2"></i>
      <span class="fw-semibold">{{ levelLabel() }}s</span>
      @if (parentParts.length) {
        <span class="text-body-secondary ms-2">under</span>
        @for (part of parentParts; track part.level) {
          <span class="badge ms-1 breadcrumb-link" [ngClass]="getBadgeClass(part.level)"
                (click)="navigateTo.emit(part.id); $event.stopPropagation()"
                title="Navigate to {{ part.name }}">
            <i class="bi {{ getPartIcon(part.level) }} me-1"></i>{{ part.name }}
          </span>
        }
      }
      @if (level() > 0) {
        <span class="add-badge ms-auto" title="Add {{ levelLabel() }}"
              (click)="addSibling.emit(); $event.stopPropagation()">
          <i class="bi bi-plus-circle me-1"></i>Add New {{ levelLabel() }}
        </span>
      }
      <span class="badge bg-primary-subtle text-primary-emphasis" [class.ms-auto]="level() === 0" [class.ms-2]="level() > 0">{{ data().length }}</span>
    </div>

    <ejs-grid #grid
      [dataSource]="data()"
      [allowSorting]="true"
      [allowResizing]="true"
      [allowTextWrap]="true"
      [textWrapSettings]="{ wrapMode: 'Header' }"
      [frozenColumns]="frozenCount()"
      [enableStickyHeader]="true"
      [rowHeight]="rowHeight()"
      [allowSelection]="true"
      (rowDataBound)="onRowDataBound($event)"
      (rowSelected)="onRowSelect($event)"
      (dataBound)="onDataBound()"
      cssClass="tsic-grid-tight">

      <e-columns>
        <!-- Action column (always first, frozen) -->
        <e-column headerText=""
                  [width]="actionColWidth()" [minWidth]="actionColWidth()" [maxWidth]="actionColWidth()"
                  textAlign="Left"
                  [allowSorting]="false" [allowResizing]="false">
          <ng-template #template let-data>
            <button class="btn-action btn-edit" title="Edit"
                    (click)="editRow.emit(data[idField()]); $event.stopPropagation()">
              <i class="bi bi-pencil"></i>
            </button>
            @if (hasMenuItems(data)) {
              <button class="btn-action btn-menu" title="More actions"
                      (click)="openMenu($event, data); $event.stopPropagation()">
                <i class="bi bi-three-dots-vertical"></i>
              </button>
            }
          </ng-template>
        </e-column>

        <!-- Data columns — dynamic via @for -->
        @for (col of columns(); track col.field) {
          @if (col.headerTooltip) {
            <!-- AM-038: header-tooltip columns need a headerTemplate, which must be a
                 direct (unconditional) child for ej2's ContentChild query — hence this
                 dedicated branch. Cell rendering here covers boolean + plain text only;
                 extend the switch before putting a tooltip on a fees/modifier/phase col. -->
            <e-column [field]="col.field" [headerText]="col.header"
                      [width]="parseWidth(col.width)"
                      [textAlign]="getTextAlign(col)"
                      [customAttributes]="cellAttrs(col)"
                      [allowSorting]="true">
              <ng-template #headerTemplate>
                <span class="hdr-label">{{ col.header }}</span><span (click)="$event.stopPropagation()"><app-info-tooltip trigger="hover" [message]="col.headerTooltip ?? ''" /></span>
              </ng-template>
              <ng-template #template let-data>
                @if (col.type === 'boolean') {
                  @if (data[col.field] === true) {
                    <i class="bi bi-check-lg text-success"></i>
                  } @else if (data[col.field] === false) {
                    <i class="bi bi-x-lg text-danger opacity-50"></i>
                  }
                } @else {
                  {{ data[col.field] ?? '' }}
                }
              </ng-template>
            </e-column>
          } @else {
          <e-column [field]="col.field" [headerText]="col.header"
                    [width]="parseWidth(col.width)"
                    [textAlign]="getTextAlign(col)"
                    [customAttributes]="cellAttrs(col)"
                    [allowSorting]="true">
            <ng-template #template let-data>
              @switch (col.type) {
                @case ('boolean') {
                  @if (data[col.field] === true) {
                    <i class="bi bi-check-lg text-success"></i>
                  } @else if (data[col.field] === false) {
                    <i class="bi bi-x-lg text-danger opacity-50"></i>
                  }
                }
                @case ('currency') {
                  @if (data[col.field] != null) {
                    {{ data[col.field] | number:'1.2-2' }}
                  }
                }
                @case ('date') {
                  {{ formatDate(data[col.field]) }}
                }
                @case ('dateOnly') {
                  {{ formatDateOnly(data[col.field]) }}
                }
                @case ('fees') {
                  @if (data['_fees']?.length) {
                    <div class="fee-pills">
                      @for (fee of data['_fees']; track fee.roleId) {
                        <div class="fee-pill" [class.fee-inherited]="fee.inherited" [class.fee-from-below]="fee.fromBelow">
                          <span class="fee-role">{{ fee.roleLabel }}:</span>
                          @if (fee.fromBelow) {
                            <!-- The operative value(s) live at more specific scopes — either
                                 nothing resolves at/above this row (AM-090) or the row's own
                                 value reaches no team (inert). Single agreed value shows the
                                 amount; disagreement reads "varies", NEVER a numeric range
                                 (a range reads as deposit–balance). -->
                            @if (singleBelowPair(fee.below); as pair) {
                              @if (pair.deposit != null && pair.deposit > 0) {
                                <span class="fee-amount">\${{ pair.deposit | number:'1.0-0' }}–\${{ pair.balanceDue | number:'1.0-0' }}</span>
                              } @else {
                                <span class="fee-amount">\${{ (pair.balanceDue ?? 0) | number:'1.0-0' }}</span>
                              }
                            } @else {
                              <span class="fee-amount">varies</span>
                            }
                          } @else {
                            @if (fee.deposit != null && fee.deposit > 0) {
                              <span class="fee-amount">\${{ fee.deposit | number:'1.0-0' }}–\${{ fee.balanceDue | number:'1.0-0' }}</span>
                            } @else if (fee.balanceDue != null && fee.balanceDue > 0) {
                              <span class="fee-amount">\${{ fee.balanceDue | number:'1.0-0' }}</span>
                            } @else {
                              <span class="fee-amount text-body-tertiary">—</span>
                            }
                            @if (fee.below && fee.below.overrideCount > 0) {
                              <span class="below-seg" [class.below-seg--differs]="!fee.below.agrees">↓{{ fee.below.overrideCount }} {{ fee.below.agrees ? 'same' : 'varies' }}</span>
                            }
                          }
                          <app-info-tooltip trigger="hover" [message]="feePillTooltip(fee, level())" />
                        </div>
                      }
                    </div>
                  } @else {
                    <span class="text-body-tertiary">—</span>
                  }
                }
                @case ('modifier') {
                  @if (data[col.field]?.length) {
                    <div class="fee-pills">
                      @for (mod of data[col.field]; track mod.roleId) {
                        <div class="fee-pill" [class.fee-inherited]="mod.inherited" [class.fee-from-below]="mod.fromBelow">
                          <span class="fee-role">{{ mod.roleLabel }}:</span>
                          @if (mod.fromBelow) {
                            <!-- More-specific scopes carry the operative value — either the
                                 row has none, or its own reaches no team (inert). -->
                            @if (singleBelowValue(mod.below); as v) {
                              <span class="fee-amount"
                                    [class.fee-discount-text]="col.field === '_earlyBird'"
                                    [class.fee-latefee-text]="col.field === '_lateFee'">
                                {{ col.field === '_lateFee' ? '+' : '-' }}\${{ v | number:'1.0-0' }}
                              </span>
                            } @else {
                              <span class="fee-amount">varies</span>
                            }
                          } @else {
                            <span class="fee-amount"
                                  [class.fee-discount-text]="col.field === '_earlyBird'"
                                  [class.fee-latefee-text]="col.field === '_lateFee'">
                              {{ col.field === '_lateFee' ? '+' : '-' }}\${{ mod.amount | number:'1.0-0' }}
                            </span>
                          }
                          @if (!mod.fromBelow && mod.below && mod.below.overrideCount > 0) {
                            <span class="below-seg" [class.below-seg--differs]="!mod.below.agrees">↓{{ mod.below.overrideCount }} {{ mod.below.agrees ? 'same' : 'varies' }}</span>
                          }
                          <app-info-tooltip trigger="hover" [message]="modifierPillTooltip(mod, level())" />
                        </div>
                      }
                    </div>
                  } @else {
                    <span class="text-body-tertiary">—</span>
                  }
                }
                @case ('phase') {
                  @if (data['_phase']?.length) {
                    <div class="fee-pills">
                      @for (ph of data['_phase']; track ph.roleId) {
                        <div class="fee-pill" [class.fee-inherited]="ph.inherited"
                             [class.fee-from-below]="ph.inert && ph.twoPhase">
                          <span class="fee-role">{{ ph.roleLabel }}:</span>
                          @if (!ph.twoPhase) {
                            <span class="phase-value">Single</span>
                          } @else if (ph.inert) {
                            <!-- Every team below stamps its own phase — the row's value
                                 operates on nobody, so show what the teams actually do. -->
                            @if (singleBelowPhase(ph.below); as pv) {
                              <span class="phase-value" [class.phase-value--full]="pv.value">{{ pv.value ? 'PIF' : 'Deposit' }}</span>
                            } @else {
                              <span class="phase-value">varies</span>
                            }
                          } @else {
                            <span class="phase-value" [class.phase-value--full]="ph.fullPayment">{{ ph.fullPayment ? 'PIF' : 'Deposit' }}</span>
                          }
                          @if (!ph.inert && ph.below && ph.below.overrideCount > 0) {
                            <span class="below-seg" [class.below-seg--differs]="!ph.below.agrees">↓{{ ph.below.overrideCount }} {{ ph.below.agrees ? 'same' : 'varies' }}</span>
                          }
                          @if (ph.twoPhase || (ph.below && ph.below.overrideCount > 0)) {
                            <app-info-tooltip trigger="hover" [message]="phasePillTooltip(ph, level())" />
                          }
                        </div>
                      }
                    </div>
                  } @else {
                    @if (level() === 0) {
                      <span class="phase-hint">See age group level</span>
                    } @else if (level() === 1) {
                      <span class="phase-hint">See team settings</span>
                    } @else if (level() === 3) {
                      <span class="phase-hint">Not set</span>
                    } @else {
                      <span class="text-body-tertiary">—</span>
                    }
                  }
                }
                @case ('identity') {
                  <!-- Two fields stacked in one cell (mobile team level: club over team).
                       No club → the secondary is promoted, so the cell never renders a
                       blank first line above the only value it has. -->
                  @if (data[col.field]) {
                    <span class="id-stack">
                      <span class="id-primary">{{ data[col.field] }}</span>
                      <span class="id-secondary">{{ data[col.secondaryField ?? ''] ?? '' }}</span>
                    </span>
                  } @else {
                    <span class="id-stack">
                      <span class="id-primary">{{ data[col.secondaryField ?? ''] ?? '' }}</span>
                    </span>
                  }
                }
                @default {
                  @if (col.colorField) {
                    <span class="ag-color-dot"
                          [class.ag-color-dot--empty]="!data[col.colorField]"
                          [style.background]="data[col.colorField] || 'var(--bs-secondary-bg)'"></span><span class="ag-name">{{ data[col.field] ?? '' }}</span>
                  } @else {
                    {{ data[col.field] ?? '' }}
                  }
                }
              }
            </ng-template>
          </e-column>
          }
        }
      </e-columns>
    </ejs-grid>

    @if (menuRow(); as mr) {
      <div class="menu-backdrop" (click)="closeMenu()"></div>
      <div #rowMenu class="row-menu" [style.top.px]="menuTop()" [style.left.px]="menuLeft()">
        @if (parentNavTarget(mr)) {
          <button type="button" class="menu-item" (click)="menuNavUp()">
            <i class="bi bi-arrow-up-short me-2"></i>{{ parentNavLabel() }}
          </button>
        }
        @if (drillDownCount(mr) > 0) {
          <button type="button" class="menu-item" (click)="menuDrillDown()">
            <i class="bi bi-arrow-down-short me-2"></i>{{ drillDownLabel(mr) }}
          </button>
        }
        @if (level() === 1) {
          <button type="button" class="menu-item" (click)="menuClone()">
            <i class="bi bi-copy me-2"></i>Clone age group
          </button>
        }
        @if (level() === 3) {
          <button type="button" class="menu-item" (click)="menuClone()">
            <i class="bi bi-copy me-2"></i>Clone team
          </button>
        }
        @if (canDeleteFn()(mr)) {
          <button type="button" class="menu-item menu-item-danger" (click)="menuDelete()">
            <i class="bi bi-trash me-2"></i>Delete
          </button>
        }
      </div>
    }
  `,
  styles: [`
    :host {
      display: flex;
      flex-direction: column;
      height: 100%;
    }

    /* ── Breadcrumb header (above the grid) ── */

    .sibling-grid-header {
      display: flex;
      align-items: center;
      padding: var(--space-2) var(--space-3);
      border-bottom: 1px solid var(--bs-border-color);
      background: var(--bs-body-bg);
      flex-shrink: 0;
    }

    .breadcrumb-link {
      cursor: pointer;
      text-decoration: underline;
      transition: filter 0.15s;
    }
    .breadcrumb-link:hover {
      filter: brightness(0.85);
    }

    /* ── Syncfusion grid overrides ── */

    :host ::ng-deep .e-grid {
      border: none;
      flex: 1;
      overflow: auto;
    }

    :host ::ng-deep .e-grid .e-headercell,
    :host ::ng-deep .e-grid .e-headercelldiv {
      font-size: var(--font-size-xs) !important;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.02em;
      /* AM-038 nits 3/4 (Ann): ej2 wrapMode:'Header' applies word-wrap:break-word,
         which split header words mid-word ("GE NDER", "ACTI VE"). Wrap at spaces
         only; column widths in ladt-grid-columns.ts fit each longest header word.
         !important is load-bearing (AM-038 re-open 07-31): ej2's own textwrap rule
         (.e-grid.e-wrap … .e-headercelldiv) out-specifies :host ::ng-deep .e-grid
         .e-headercelldiv, so without it word-wrap:break-word won the cascade and
         headers kept splitting — keep-all alone doesn't stop English mid-word breaks.
         white-space needs the same treatment (08-01): the theme's
         .e-grid.e-responsive .e-headercelldiv rule sets nowrap+ellipsis, which was
         truncating narrow headers ("M/F" → "M…", "3RD PA…") instead of wrapping.
         08-02: that didn't kill the "…" — the operative ellipsis is one layer
         DEEPER, on the theme's inner .e-headertext span (overflow:hidden +
         text-overflow:ellipsis), and it fires whenever the text box (column
         width minus 16px cell padding minus 22px sort-icon reserve) can't fit a
         word. The ellipsis is left in place as the honest too-narrow signal;
         the fix is widths in ladt-grid-columns.ts budgeting that 38px chrome. */
      white-space: normal !important;
      word-break: keep-all !important;
      overflow-wrap: normal !important;
      word-wrap: normal !important;
      line-height: var(--line-height-tight);
    }

    /* AM-038 (08-02): headerTemplate columns lose the theme's centering — its
       rule keys on :has(span.e-headertext), which templated headers don't
       render — so restore it for center-aligned templated headers. */
    :host ::ng-deep .e-grid .e-headercell.e-templatecell.e-centeralign .e-headercelldiv {
      justify-content: center;
    }

    :host ::ng-deep .e-grid .e-rowcell {
      font-size: var(--font-size-xs) !important;
      padding: var(--space-1) var(--space-2);
    }

    /* Columns flagged wrap:true in ladt-grid-columns.ts (club / team name) wrap to as many
       lines as the value needs; the row grows, since ej2 writes [rowHeight] to the <tr> as
       a height, which a table row treats as a MINIMUM. The theme's .e-grid .e-rowcell sets
       white-space:nowrap and .e-grid.e-responsive .e-rowcell adds the ellipsis, and both
       out-specify a bare class selector — hence !important here, the same reason the
       header block above needs it. break-word only breaks a token that cannot fit a line
       of its own, so ordinary multi-word names still break at spaces. Scoped to .e-rowcell:
       ej2 puts customAttributes on the HEADER cell too, and the header's keep-all tuning
       (see above) must not be disturbed. */
    :host ::ng-deep .e-grid .e-rowcell.wrap-cell {
      white-space: normal !important;
      text-overflow: clip !important;
      overflow-wrap: break-word;
      line-height: var(--line-height-tight);
    }

    /* Compact padding on the first (action) cell — Syncfusion's default 12px
       horizontal padding blows the action col out to ~112px regardless of
       the declared [width] value. */
    :host ::ng-deep .e-grid .e-rowcell:first-child,
    :host ::ng-deep .e-grid .e-headercell:first-child {
      padding-left: 4px !important;
      padding-right: 4px !important;
    }

    /* Row states — selection is Syncfusion-native (.e-active on the row,
       .e-selectionbackground on its cells); brand the native classes rather than
       a hand-rolled one. */
    :host ::ng-deep .e-grid .e-row.e-active .e-rowcell,
    :host ::ng-deep .e-grid .e-row.e-active .e-selectionbackground {
      background: var(--bs-primary-bg-subtle) !important;
      font-weight: 500;
    }
    :host ::ng-deep .e-grid .e-row.e-active .e-freezeleftborder {
      background: var(--bs-primary-bg-subtle) !important;
      font-weight: 600;
      color: var(--bs-primary);
    }

    :host ::ng-deep .e-grid .e-row.inactive-row {
      opacity: 0.55;
    }
    :host ::ng-deep .e-grid .e-row.inactive-row .e-freezeleftborder {
      text-decoration: line-through;
      font-style: italic;
    }

    :host ::ng-deep .e-grid .e-row.special-row {
      opacity: 0.6;
    }
    :host ::ng-deep .e-grid .e-row.special-row .e-rowcell {
      font-style: italic;
      color: var(--bs-secondary-color);
    }

    /* Hover */
    :host ::ng-deep .e-grid .e-row:hover .e-rowcell {
      background: var(--bs-tertiary-bg) !important;
    }

    /* Cursor on rows */
    :host ::ng-deep .e-grid .e-row {
      cursor: pointer;
    }

    /* ── Action column elements ── */

    .btn-action {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 24px;
      height: 24px;
      padding: 0;
      border: none;
      border-radius: var(--radius-sm);
      background: transparent;
      color: var(--bs-secondary-color);
      cursor: pointer;
      font-size: var(--font-size-xs);
      transition: all 0.15s;
    }
    .btn-action:hover {
      background: var(--bs-secondary-bg);
      color: var(--bs-body-color);
    }
    .btn-edit:hover { color: var(--bs-info); }
    .btn-menu:hover { color: var(--bs-primary); }

    /* ⋮ row-action menu (positioned fixed so it escapes e-rowcell clipping) */
    .menu-backdrop {
      position: fixed; inset: 0; z-index: 1055; background: transparent;
    }
    .row-menu {
      position: fixed; z-index: 1056;
      min-width: 160px;
      background: var(--bs-body-bg);
      border: 1px solid var(--bs-border-color);
      border-radius: var(--radius-sm);
      box-shadow: var(--shadow-md);
      padding: var(--space-1) 0;
    }
    .menu-item {
      display: flex; align-items: center;
      width: 100%;
      padding: var(--space-1) var(--space-3);
      border: none; background: transparent;
      font-size: var(--font-size-xs);
      color: var(--bs-body-color);
      text-align: left; cursor: pointer;
    }
    .menu-item:hover {
      background: var(--bs-tertiary-bg);
    }
    .menu-item-danger { color: var(--bs-danger); }
    .menu-item-danger:hover { background: rgba(var(--bs-danger-rgb), 0.08); }

    .drill-badge {
      display: inline-flex;
      align-items: center;
      font-size: 0.6rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.03em;
      padding: 2px 5px;
      border-radius: var(--radius-sm);
      border: 1px solid var(--bs-primary);
      background: transparent;
      color: var(--bs-primary);
      cursor: pointer;
      transition: all 0.15s;
    }
    .drill-badge:hover {
      background: var(--bs-primary-bg-subtle);
    }
    .drill-up {
      border-color: var(--bs-secondary-color);
      color: var(--bs-secondary-color);
    }
    .drill-up:hover {
      background: var(--bs-secondary-bg);
    }
    .drill-badge i {
      font-size: 0.85rem;
    }

    .nav-badges {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
    }

    .add-badge {
      display: inline-block;
      font-size: 0.6rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.03em;
      padding: 2px 5px;
      border-radius: var(--radius-sm);
      border: 1px solid var(--bs-success);
      background: transparent;
      color: var(--bs-success);
      cursor: pointer;
      text-decoration: underline;
      transition: all 0.15s;
    }
    .add-badge:hover {
      background: var(--bs-success-bg-subtle);
    }

    .ag-color-dot {
      display: inline-block;
      width: 10px;
      height: 10px;
      border-radius: 50%;
      border: 1px solid var(--bs-border-color);
      vertical-align: middle;
    }
    .ag-color-dot--empty {
      border-style: dashed;
    }
    .ag-name {
      margin-left: var(--space-2);
      vertical-align: middle;
    }

    @media (max-width: 767.98px) {
      /* ── Stacked identity cell (type: 'identity') ──
         Only the mobile column sets declare an identity column, so this markup cannot
         appear on desktop — but the rules are gated on the same breakpoint anyway so
         they are absent from desktop's stylesheet, not merely inert in it. Mirrors the
         tree's team node (.tree-label-group / -primary / -secondary in
         ladt.component.scss) so the grid reads the same as the tree the director came
         from. Breakpoint must stay in step with isNarrow in ladt.component.ts. */

      .id-stack {
        display: flex;
        flex-direction: column;
        justify-content: center;
        min-width: 0;
        line-height: 1.25;
      }
      /* Wraps rather than ellipsizes — same call as the desktop club/team columns and
         the tree's .tree-label-group. 210px on a 390px phone truncated most club names. */
      .id-primary,
      .id-secondary {
        white-space: normal;
        overflow-wrap: break-word;
      }
      .id-primary {
        font-weight: 600;
      }
      .id-secondary {
        font-size: 0.8125rem;
        color: var(--bs-secondary-color);
      }
    }

    /* ── Fee pills ── */

    .fee-pills {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }
    .fee-pill {
      display: flex;
      align-items: center;
      gap: var(--space-1);
      font-size: var(--font-size-xs);
      line-height: 1.2;
      font-variant-numeric: tabular-nums;
    }
    .fee-role {
      font-weight: 600;
      color: var(--bs-secondary-color);
    }
    .fee-amount {
      font-weight: 500;
      color: var(--bs-body-color);
    }
    /* Dim the pill's text only — NOT via container opacity, which would cascade onto the
       position:fixed <app-info-tooltip> panel (a descendant) and render it translucent. */
    .fee-inherited > :not(app-info-tooltip) {
      opacity: 0.55;
      font-style: italic;
    }
    /* From-BELOW value (nothing set at/above the row; more specific scopes carry it —
       AM-090). Same dim treatment as inherited but a DISTINCT class: "inherited" means
       from-above and its tooltip wording must never leak here. */
    .fee-from-below > :not(app-info-tooltip) {
      opacity: 0.55;
      font-style: italic;
    }
    /* Downward disclosure segment: "↓2 same" / "↓3 varies". Glyph + text carry the
       meaning; color is supplementary only. */
    .below-seg {
      font-size: var(--font-size-xs);
      color: var(--bs-secondary-color);
      white-space: nowrap;
    }
    .below-seg--differs {
      color: var(--bs-warning-emphasis);
      font-weight: 600;
    }
    .fee-discount-text { color: var(--bs-success); font-weight: 600; }
    .fee-latefee-text { color: var(--bs-danger); font-weight: 600; }
    .phase-value {
      font-weight: 500;
      color: var(--bs-secondary-color);
      white-space: nowrap;
    }
    .phase-value--full {
      color: var(--bs-primary);
      font-weight: 600;
    }
    .phase-hint {
      font-size: 0.8125rem;
      font-style: italic;
      color: var(--bs-secondary-color);
      white-space: nowrap;
    }
  `]
})
export class LadtSiblingGridComponent implements OnChanges {
  readonly columns = input<LadtColumnDef[]>([]);
  readonly data = input<any[]>([]);
  readonly selectedId = input('');
  readonly idField = input('id');
  readonly levelLabel = input('');
  readonly levelIcon = input('bi-list');
  @Input() parentParts: ParentBreadcrumb[] = [];

  readonly level = input(0); // 0=league, 1=agegroup, 2=division, 3=team
  readonly canDeleteFn = input<(row: any) => boolean>(() => true);

  readonly rowSelected = output<string>();
  readonly drillDown = output<string>();
  readonly editRow = output<string>();
  readonly deleteRow = output<string>();
  readonly addSibling = output<void>();
  readonly cloneRow = output<any>();
  readonly navigateTo = output<string>();

  readonly grid = viewChild<GridComponent>('grid');
  private readonly rowMenu = viewChild<ElementRef<HTMLElement>>('rowMenu');
  private readonly injector = inject(Injector);

  // Frozen column count (action col + frozen data cols)
  frozenCount = computed(() => countFrozenColumns(this.columns()));

  /**
   * 32px fits one line of text; an `identity` column stacks two, so the row grows to 48.
   * Derived from the COLUMN SET rather than the viewport on purpose: only the mobile sets
   * carry an `identity` column, so no desktop column set can reach 48 — the desktop row
   * height is provably the same 32 it has always been, with no viewport logic in this
   * component at all.
   */
  rowHeight = computed(() => this.columns().some(c => c.type === 'identity') ? 48 : 32);

  /**
   * ej2 stamps these attributes on the column's header AND body cells. Two frozen
   * singleton objects rather than a fresh literal per change-detection pass, so the
   * binding stays referentially stable.
   */
  cellAttrs(col: LadtColumnDef): Record<string, string> {
    return col.wrap ? WRAP_CELL_ATTRS : NO_ATTRS;
  }

  // Uniform action column width — fits pencil + ⋮ menu (nav badges moved into menu)
  actionColWidth(): number {
    return 64;
  }

  // ── Row action menu (⋮) ──
  menuRow = signal<any | null>(null);
  menuTop = signal(0);
  menuLeft = signal(0);

  hasMenuItems(row: any): boolean {
    return !row?._isSpecial;
  }

  // ── Nav helpers (used by menu items) ──
  parentNavTarget(row: any): string | null {
    const level = this.level();
    if (level === 2) return row?.['_parentAgId'] ?? null;
    if (level === 3) return row?.['_parentDivId'] ?? null;
    return null;
  }
  parentNavLabel(): string {
    const level = this.level();
    if (level === 2) return 'Go up to Age Group';
    if (level === 3) return 'Go up to Division';
    return '';
  }
  drillDownCount(row: any): number {
    const level = this.level();
    if (level === 0) return row?.['agegroupCount'] ?? 0;
    if (level === 1) return row?.['divisionCount'] ?? 0;
    if (level === 2) return row?.['teamCount'] ?? 0;
    return 0;
  }
  drillDownLabel(row: any): string {
    const n = this.drillDownCount(row);
    const level = this.level();
    if (level === 0) return `Drill into ${n} Age Group${n === 1 ? '' : 's'}`;
    if (level === 1) return `Drill into ${n} Division${n === 1 ? '' : 's'}`;
    if (level === 2) return `Drill into ${n} Team${n === 1 ? '' : 's'}`;
    return '';
  }

  menuNavUp(): void {
    const row = this.menuRow();
    const target = row ? this.parentNavTarget(row) : null;
    if (target) this.navigateTo.emit(target);
    this.closeMenu();
  }

  menuDrillDown(): void {
    const row = this.menuRow();
    if (row) this.drillDown.emit(row[this.idField()]);
    this.closeMenu();
  }

  openMenu(event: MouseEvent, row: any): void {
    const btn = event.currentTarget as HTMLElement;
    const rect = btn.getBoundingClientRect();
    // Position menu just below-right of the button; fixed positioning so it
    // escapes Syncfusion's .e-rowcell overflow:hidden clipping.
    this.menuTop.set(rect.bottom + 2);
    this.menuLeft.set(rect.left);
    this.menuRow.set(row);
    this.clampMenuIntoViewport();
  }

  /**
   * Keep the menu on screen. `position: fixed` cannot be scrolled to, so a menu that runs
   * past the bottom edge is UNREACHABLE, not merely clipped — on the last rows of any list
   * that silently removed Delete, Clone and drill-down.
   *
   * Applies at EVERY viewport. It shipped mobile-only first, on the reasoning that a clamp is
   * a no-op whenever the menu already fits and so could not disturb desktop — but that is an
   * argument, and the cost of being cautious was leaving a live defect for desktop directors.
   * Math.min/Math.max return the requested position unchanged in every case that renders
   * correctly today; they only engage when the menu would otherwise be off-screen, which is
   * precisely the broken case.
   *
   * afterNextRender is the sanctioned tool here — .claude/rules/frontend-angular.md bans
   * effect() but explicitly exempts afterNextRender for DOM work against the rendered view,
   * and passing { injector } is what makes it callable from a method.
   *
   * Measured, not estimated from the item count: the template has four independent
   * visibility conditions, and duplicating them here would drift the first time a menu item
   * is added.
   */
  private clampMenuIntoViewport(): void {
    afterNextRender(() => {
      const el = this.rowMenu()?.nativeElement;
      if (!el) return;
      const { height, width } = el.getBoundingClientRect();
      const margin = 8;
      this.menuTop.set(Math.max(margin, Math.min(this.menuTop(), window.innerHeight - height - margin)));
      this.menuLeft.set(Math.max(margin, Math.min(this.menuLeft(), window.innerWidth - width - margin)));
    }, { injector: this.injector });
  }

  closeMenu(): void {
    this.menuRow.set(null);
  }

  menuDelete(): void {
    const row = this.menuRow();
    if (row) this.deleteRow.emit(row[this.idField()]);
    this.closeMenu();
  }

  menuClone(): void {
    const row = this.menuRow();
    if (row) this.cloneRow.emit(row);
    this.closeMenu();
  }

  ngOnChanges(changes: SimpleChanges): void {
    // A selection-only change — ▲/▼ sibling navigation from the fly-in — doesn't rebind
    // data, so (dataBound) won't fire. The rows are already rendered from the prior bind,
    // so drive SF's selection directly: it highlights AND scrolls the row into view.
    if (changes['selectedId']) {
      this.selectRowById();
    }
  }

  // ── Syncfusion event handlers ──

  onRowDataBound(args: any): void {
    const row = args.data;
    if (!row || !args.row) return;

    if (row['active'] === false) {
      args.row.classList.add('inactive-row');
    }
    if (row['_isSpecial'] === true) {
      args.row.classList.add('special-row');
    }
  }

  /** Fires after every rebind (add/drill/data swap) — re-assert the selected row once
   *  the new rows are rendered, using SF's own selection (highlight + scroll into view). */
  onDataBound(): void {
    this.selectRowById();
  }

  /**
   * Reflect the externally-driven `selectedId` onto the grid via Syncfusion's native
   * selection engine (`selectRow`), which highlights the row and scrolls it into view.
   * No-ops when that row is already selected, so redundant rebinds don't re-scroll or
   * re-fire selection events.
   */
  private selectRowById(): void {
    const grid = this.grid();
    if (!grid) return;
    const id = this.selectedId();
    if (!id) return;
    let records: any[] | null;
    try {
      // getCurrentViewRecords() throws before the first bind (init/teardown) — skip then.
      records = grid.getCurrentViewRecords() as any[] | null;
    } catch {
      return;
    }
    if (!records) return;
    const index = records.findIndex((r) => r?.[this.idField()] === id);
    if (index < 0 || grid.selectedRowIndex === index) return;
    grid.selectRow(index);
  }

  onRowSelect(args: any): void {
    // Only user clicks bubble up. Programmatic selectRow (driven by selectedId) reports
    // isInteracted=false — ignoring it prevents a select→emit→re-select echo loop.
    if (!args?.isInteracted) return;
    const id = args.data?.[this.idField()];
    if (id) {
      this.rowSelected.emit(id);
    }
  }

  // ── Helpers ──

  parseWidth(width: string | undefined): number {
    return parseInt(width ?? '90', 10);
  }

  getTextAlign(col: LadtColumnDef): string {
    if (col.type === 'boolean') return 'Center';
    if (col.type === 'number' || col.type === 'currency') return 'Right';
    return 'Left';
  }

  /** Tooltip text for the ⓘ icon: where in the L→AG→T cascade a fee/phase was set or inherited from.
   *
   *  AM-066 (Ann 08-02, Todd go): the job tier renders as "League" to Directors. "Job default"
   *  named a level the UI never shows — since the PL-062 phase rework the League card IS the
   *  top-level control, so a Director has no "job" tier in their model. `job` and `league`
   *  deliberately collapse to the same label: they are one level as far as this UI presents
   *  them, and distinguishing them would reintroduce the vocabulary Ann objected to.
   *  Note the consequence at league scope: a job-baseline pill reads "Inherited from League
   *  level" while you are on the league row — correct under the new model (the value does come
   *  from the League card's tier), and Ann's requested wording verbatim.
   *
   *  "level" moved into the shared template so both branches read as a level, and the labels
   *  are title-case to match the tab/card names Directors see. */
  sourceTooltip(source: string, inherited: boolean): string {
    const label = this.tierLabel(source);
    return inherited ? `Inherited from ${label} level` : `Set at ${label} level`;
  }

  private static readonly TIER_LABELS: Record<string, string> = {
    job: 'League', league: 'League', agegroup: 'Age Group', team: 'Team'
  };

  private tierLabel(tier: string): string {
    return LadtSiblingGridComponent.TIER_LABELS[tier] ?? tier;
  }

  /** "Team level" / "Age Group and Team levels" — the deeper tiers actually carrying the
   *  value, from dist.ownTiers; fallback covers a missing dist (defensive only). */
  private tiersLabel(tiers: string[] | undefined, fallback: string): string {
    if (!tiers?.length) return `${fallback} level`;
    return tiers.map(t => this.tierLabel(t)).join(' and ') + (tiers.length > 1 ? ' levels' : ' level');
  }

  // ── Below-summary helpers (server resolution map) ──
  // The `below` shapes come from LadtFeeBelowSummaryDto: { overrideCount, agrees,
  // distinctValues }. Wording is downward-facing ("Set at Team level") and NEVER
  // routes through the `inherited` branch — that phrasing is from-above only.

  /** The single agreed (deposit, balanceDue) pair below, or null when absent/varied. */
  singleBelowPair(below: any): { deposit: number | null; balanceDue: number | null } | null {
    return below?.overrideCount > 0 && below.distinctValues?.length === 1
      ? below.distinctValues[0] : null;
  }

  /** The single agreed modifier amount below, or null when absent/varied. */
  singleBelowValue(below: any): number | null {
    return below?.overrideCount > 0 && below.distinctValues?.length === 1
      ? below.distinctValues[0] : null;
  }

  /** The single agreed phase stamp below, boxed so a `false` stamp survives @if. */
  singleBelowPhase(below: any): { value: boolean } | null {
    return below?.overrideCount > 0 && below.distinctValues?.length === 1
      ? { value: below.distinctValues[0] } : null;
  }

  private belowWho(count: number, level: number): string {
    const noun = level === 0 ? 'more-specific scope' : 'team';
    return count === 1 ? `1 ${noun} sets` : `${count} ${noun}s set`;
  }

  private pairLabel(p: { deposit: number | null; balanceDue: number | null }): string {
    if (p.deposit != null && p.deposit > 0) return `$${p.deposit}–$${p.balanceDue ?? 0}`;
    return `$${p.balanceDue ?? 0}`;
  }

  private pairHasCharge(p: { deposit: number | null; balanceDue: number | null }): boolean {
    return (p.deposit ?? 0) > 0 || (p.balanceDue ?? 0) > 0;
  }

  // ── Pill tooltips ──
  // Ruling (Todd 08-07): a tooltip may only say a level "set" a value if that value
  // actually operates on someone. Container-row wording is a distribution over the teams
  // below (dist = { teams, covered, own, ownTiers }); a $0 on file never gets intent
  // language ("set") because the data cannot distinguish a decision from save-flow residue.

  feePillTooltip(fee: any, level: number): string {
    const d = fee.dist;
    if (fee.fromBelow) {
      // The pill's value is carried entirely by deeper scopes — either nothing resolves
      // at/above the row, or the row's own value reaches no team (inert).
      if (!fee.below || fee.below.overrideCount === 0) return 'Not set';
      const vals = (fee.below.distinctValues ?? []).map((p: any) => this.pairLabel(p));
      const where = this.tiersLabel(d?.ownTiers, level === 0 ? 'Age Group or Team' : 'Team');
      let text = vals.length === 1
        ? `All fees set at the ${where}: ${vals[0]}`
        : `All fees set at the ${where} — varies: ${vals.join(', ')}`;
      if (fee.inert && this.pairHasCharge(fee)) {
        text += ` · The ${this.tierLabel(fee.source ?? 'job')}-level ${this.pairLabel(fee)} reaches no team`;
      }
      return text;
    }
    const head = this.pairHasCharge(fee)
      ? this.sourceTooltip(fee.source ?? 'job', fee.inherited)
      : `$0 on file at ${this.tierLabel(fee.source ?? 'job')} level`;
    if (!d) return head; // team rows: nothing below to distribute over
    if (d.teams === 0) return `${head} — no teams here`;
    if (d.own === 0) return `${head} — applies to all ${d.teams} team${d.teams === 1 ? '' : 's'}`;
    const vals = (fee.below?.distinctValues ?? []).map((p: any) => this.pairLabel(p)).join(', ');
    const same = fee.below?.agrees ? ' (same)' : '';
    return `${head} — applies to ${d.covered} of ${d.teams} teams`
      + ` · ${d.own} set${d.own === 1 ? 's' : ''} own: ${vals}${same}`;
  }

  modifierPillTooltip(mod: any, level: number): string {
    const d = mod.dist;
    if (mod.fromBelow) {
      if (!mod.below || mod.below.overrideCount === 0) return 'Not set';
      const vals = (mod.below.distinctValues ?? []).map((v: number) => `$${v}`);
      const where = this.tiersLabel(d?.ownTiers, level === 0 ? 'Age Group or Team' : 'Team');
      let text = vals.length === 1
        ? `All set at the ${where}: ${vals[0]}`
        : `All set at the ${where} — varies: ${vals.join(', ')}`;
      if (mod.inert && mod.amount != null) {
        text += ` · The ${this.tierLabel(mod.source)}-level $${mod.amount} reaches no team`;
      }
      return text;
    }
    const head = this.sourceTooltip(mod.source, mod.inherited);
    if (!d) return head;
    if (d.teams === 0) return `${head} — no teams here`;
    if (d.own === 0) return `${head} — applies to all ${d.teams} team${d.teams === 1 ? '' : 's'}`;
    const vals = (mod.below?.distinctValues ?? []).map((v: number) => `$${v}`).join(', ');
    const same = mod.below?.agrees ? ' (same)' : '';
    return `${head} — applies to ${d.covered} of ${d.teams} teams`
      + ` · ${d.own} set${d.own === 1 ? 's' : ''} own: ${vals}${same}`;
  }

  phasePillTooltip(ph: any, level: number): string {
    const d = ph.dist;
    const b = ph.below;
    const phLabel = (v: boolean) => (v ? 'Full payment' : 'Deposit first');
    if (ph.inert && ph.twoPhase) {
      const vals = (b?.distinctValues ?? []).map(phLabel);
      const where = this.tiersLabel(d?.ownTiers, level === 0 ? 'Age Group or Team' : 'Team');
      const note = ` · The ${this.tierLabel(ph.source)}-level setting reaches no team`;
      return vals.length === 1
        ? `All phase set at the ${where}: ${vals[0]}${note}`
        : `All phase set at the ${where} — varies: ${vals.join(', ')}${note}`;
    }
    const src = this.sourceTooltip(ph.source, ph.inherited);
    if (!b || b.overrideCount === 0) {
      return d && d.teams > 0 && d.own === 0
        ? `${src} — applies to all ${d.teams} team${d.teams === 1 ? '' : 's'}`
        : src;
    }
    const vals = (b.distinctValues ?? []).map(phLabel).join(', ');
    const same = b.agrees ? ' (same)' : '';
    if (d && d.own > 0 && d.covered > 0) {
      return `${src} — applies to ${d.covered} of ${d.teams} teams`
        + ` · ${d.own} set${d.own === 1 ? 's' : ''} own: ${vals}${same}`;
    }
    return `${src} · ${this.belowWho(b.overrideCount, level)} own phase below: ${vals}`;
  }

  getBadgeClass(level: number): string {
    switch (level) {
      case 0: return 'bg-primary-subtle text-primary-emphasis';
      case 1: return 'bg-success-subtle text-success-emphasis';
      case 2: return 'bg-warning-subtle text-warning-emphasis';
      case 3: return 'bg-info-subtle text-info-emphasis';
      default: return 'bg-secondary-subtle text-secondary-emphasis';
    }
  }

  getPartIcon(level: number): string {
    switch (level) {
      case 0: return 'bi-trophy';
      case 1: return 'bi-people';
      case 2: return 'bi-grid-3x3-gap';
      case 3: return 'bi-person-badge';
      default: return 'bi-circle';
    }
  }

  formatDate(value: string | null | undefined): string {
    if (!value) return '';
    const d = new Date(value);
    if (isNaN(d.getTime())) return value;
    return d.toLocaleDateString('en-US', { month: '2-digit', day: '2-digit', year: 'numeric' });
  }

  formatDateOnly(value: string | null | undefined): string {
    if (!value) return '';
    // DateOnly comes as "YYYY-MM-DD"
    const parts = value.split('-');
    if (parts.length === 3) return `${parts[1]}/${parts[2]}/${parts[0]}`;
    return value;
  }
}
