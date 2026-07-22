import { Component, Input, ChangeDetectionStrategy, signal, computed, OnChanges, SimpleChanges, CUSTOM_ELEMENTS_SCHEMA, input, output, viewChild } from '@angular/core';
import { DecimalPipe, NgClass } from '@angular/common';
import { GridAllModule, GridComponent } from '@syncfusion/ej2-angular-grids';
import type { LadtColumnDef } from '../configs/ladt-grid-columns';
import { countFrozenColumns } from '../configs/ladt-grid-columns';
import { InfoTooltipComponent } from '../../../../shared-ui/components/info-tooltip.component';

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
      [rowHeight]="32"
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
          <e-column [field]="col.field" [headerText]="col.header"
                    [width]="parseWidth(col.width)"
                    [textAlign]="getTextAlign(col)"
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
                        <div class="fee-pill" [class.fee-inherited]="fee.inherited">
                          <span class="fee-role">{{ fee.roleLabel }}:</span>
                          @if (fee.deposit != null && fee.deposit > 0) {
                            <span class="fee-amount">\${{ fee.deposit | number:'1.0-0' }}–\${{ fee.balanceDue | number:'1.0-0' }}</span>
                          } @else if (fee.balanceDue != null && fee.balanceDue > 0) {
                            <span class="fee-amount">\${{ fee.balanceDue | number:'1.0-0' }}</span>
                          } @else {
                            <span class="fee-amount text-body-tertiary">—</span>
                          }
                          <app-info-tooltip trigger="hover" [message]="sourceTooltip(fee.source, fee.inherited)" />
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
                        <div class="fee-pill" [class.fee-inherited]="mod.inherited">
                          <span class="fee-role">{{ mod.roleLabel }}:</span>
                          <span class="fee-amount"
                                [class.fee-discount-text]="col.field === '_earlyBird'"
                                [class.fee-latefee-text]="col.field === '_lateFee'">
                            {{ col.field === '_lateFee' ? '+' : '-' }}\${{ mod.amount | number:'1.0-0' }}
                          </span>
                          <app-info-tooltip trigger="hover" [message]="sourceTooltip(mod.source, mod.inherited)" />
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
                        <div class="fee-pill" [class.fee-inherited]="ph.inherited">
                          <span class="fee-role">{{ ph.roleLabel }}:</span>
                          @if (ph.twoPhase) {
                            <span class="phase-value" [class.phase-value--full]="ph.fullPayment">{{ ph.fullPayment ? 'PIF' : 'Deposit' }}</span>
                          } @else {
                            <span class="phase-value">Single</span>
                          }
                          @if (ph.twoPhase) {
                            <app-info-tooltip trigger="hover" [message]="sourceTooltip(ph.source, ph.inherited)" />
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
      </e-columns>
    </ejs-grid>

    @if (menuRow(); as mr) {
      <div class="menu-backdrop" (click)="closeMenu()"></div>
      <div class="row-menu" [style.top.px]="menuTop()" [style.left.px]="menuLeft()">
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
      white-space: normal;
      line-height: var(--line-height-tight);
    }

    :host ::ng-deep .e-grid .e-rowcell {
      font-size: var(--font-size-xs) !important;
      padding: var(--space-1) var(--space-2);
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

  // Frozen column count (action col + frozen data cols)
  frozenCount = computed(() => countFrozenColumns(this.columns()));

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

  /** Tooltip text for the ⓘ icon: where in the L→AG→T cascade a fee/phase was set or inherited from. */
  sourceTooltip(source: string, inherited: boolean): string {
    const labels: Record<string, string> = {
      job: 'job default', league: 'league', agegroup: 'age-group', team: 'team'
    };
    const label = labels[source] ?? source;
    return inherited ? `Inherited from ${label}` : `Set at ${label} level`;
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
