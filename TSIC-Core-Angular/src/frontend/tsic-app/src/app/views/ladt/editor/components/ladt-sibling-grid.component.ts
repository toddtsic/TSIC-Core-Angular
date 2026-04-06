import { Component, Input, Output, EventEmitter, ChangeDetectionStrategy, signal, computed, OnChanges, SimpleChanges, ViewChild, CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { DecimalPipe, NgClass } from '@angular/common';
import { GridAllModule, GridComponent } from '@syncfusion/ej2-angular-grids';
import type { LadtColumnDef } from '../configs/ladt-grid-columns';
import { countFrozenColumns } from '../configs/ladt-grid-columns';

export interface ParentBreadcrumb {
  name: string;
  level: number;
  id: string;
}

@Component({
  selector: 'app-ladt-sibling-grid',
  standalone: true,
  imports: [DecimalPipe, NgClass, GridAllModule],
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sibling-grid-header">
      <i class="bi {{ levelIcon }} me-2"></i>
      <span class="fw-semibold">{{ levelLabel }}s</span>
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
      @if (level > 0) {
        <span class="add-badge ms-auto" title="Add {{ levelLabel }}"
              (click)="addSibling.emit(); $event.stopPropagation()">
          <i class="bi bi-plus-circle me-1"></i>Add New {{ levelLabel }}
        </span>
      }
      <span class="badge bg-primary-subtle text-primary-emphasis" [class.ms-auto]="level === 0" [class.ms-2]="level > 0">{{ dataSignal().length }}</span>
    </div>

    <ejs-grid #grid
      [dataSource]="sortedData()"
      [allowSorting]="true"
      [allowResizing]="true"
      [allowTextWrap]="true"
      [textWrapSettings]="{ wrapMode: 'Header' }"
      [frozenColumns]="frozenCount()"
      [enableStickyHeader]="true"
      [rowHeight]="32"
      [allowSelection]="true"
      (actionBegin)="onActionBegin($event)"
      (rowDataBound)="onRowDataBound($event)"
      (rowSelected)="onRowSelect($event)"
      class="ladt-grid">

      <e-columns>
        <!-- Action column (always first, frozen) -->
        <e-column headerText="" [width]="actionColWidth()" textAlign="Center"
                  [allowSorting]="false" [allowResizing]="false">
          <ng-template #template let-data>
            <button class="btn-action btn-edit" title="Edit"
                    (click)="editRow.emit(data[idField]); $event.stopPropagation()">
              <i class="bi bi-pencil"></i>
            </button>
            @if (canDeleteFn(data)) {
              <button class="btn-action btn-del" title="Delete"
                      (click)="deleteRow.emit(data[idField]); $event.stopPropagation()">
                <i class="bi bi-trash"></i>
              </button>
            }
            @if (!data['_isSpecial']) {
              <span class="nav-badges">
                @if (level === 2) {
                  <span class="drill-badge drill-up" title="Navigate up to Age Group"
                        (click)="navigateTo.emit(data['_parentAgId']); $event.stopPropagation()">
                    <i class="bi bi-arrow-up-short"></i>A
                  </span>
                }
                @if (level === 3) {
                  <span class="drill-badge drill-up" title="Navigate up to Division"
                        (click)="navigateTo.emit(data['_parentDivId']); $event.stopPropagation()">
                    <i class="bi bi-arrow-up-short"></i>D
                  </span>
                }
                @if (level === 1 && (data['divisionCount'] ?? 0) > 0) {
                  <span class="drill-badge" title="Navigate down to {{ data['divisionCount'] }} Divisions"
                        (click)="drillDown.emit(data[idField]); $event.stopPropagation()">
                    D<i class="bi bi-arrow-down-short"></i>{{ data['divisionCount'] }}
                  </span>
                }
                @if (level === 2 && (data['teamCount'] ?? 0) > 0) {
                  <span class="drill-badge" title="Navigate down to {{ data['teamCount'] }} Teams"
                        (click)="drillDown.emit(data[idField]); $event.stopPropagation()">
                    T<i class="bi bi-arrow-down-short"></i>{{ data['teamCount'] }}
                  </span>
                }
              </span>
            }
          </ng-template>
        </e-column>

        <!-- Data columns — dynamic via @for -->
        @for (col of columns; track col.field) {
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
                          <span class="fee-role">{{ fee.roleLabel }}</span>
                          @if (fee.deposit != null && fee.deposit > 0) {
                            <span class="fee-amount">\${{ fee.deposit | number:'1.0-0' }}</span>
                            <span class="fee-sep">→</span>
                            <span class="fee-amount">\${{ fee.balanceDue | number:'1.0-0' }}</span>
                          } @else if (fee.balanceDue != null && fee.balanceDue > 0) {
                            <span class="fee-amount">\${{ fee.balanceDue | number:'1.0-0' }}</span>
                          } @else {
                            <span class="fee-amount text-body-tertiary">—</span>
                          }
                          @if (fee.inherited) {
                            <span class="fee-from-badge">from {{ fee.source === 'job' ? 'job' : fee.source === 'agegroup' ? 'ag' : fee.source }}</span>
                          } @else {
                            <span class="fee-set-badge">{{ fee.source === 'job' ? 'job' : fee.source === 'agegroup' ? 'ag' : 'team' }} set</span>
                          }
                          @if (fee.activeDiscount) {
                            <span class="fee-modifier fee-discount" title="Active discount">
                              -\${{ fee.activeDiscount | number:'1.0-0' }}
                            </span>
                          }
                          @if (fee.activeLateFee) {
                            <span class="fee-modifier fee-latefee" title="Active late fee">
                              +\${{ fee.activeLateFee | number:'1.0-0' }}
                            </span>
                          }
                        </div>
                      }
                    </div>
                  } @else {
                    <span class="text-body-tertiary">—</span>
                  }
                }
                @default {
                  @if (col.colorField) {
                    <span class="frozen-cell-flex">
                      <span class="color-dot"
                            [class.color-dot--empty]="!data[col.colorField]"
                            [style.background]="data[col.colorField] ?? 'var(--bs-secondary-bg)'"></span>
                      <span class="frozen-cell-name">{{ data[col.field] ?? '' }}</span>
                      @if (data['teamCount'] != null) {
                        <span class="badge bg-primary-subtle text-primary-emphasis frozen-badge">{{ data['teamCount'] | number }}</span>
                      }
                      @if (data['playerCount'] != null) {
                        <span class="badge bg-success-subtle text-success-emphasis frozen-badge">{{ data['playerCount'] | number }}</span>
                      }
                    </span>
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

    :host ::ng-deep .e-grid .e-headercell {
      font-size: var(--font-size-xs);
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.02em;
      white-space: normal;
      line-height: var(--line-height-tight);
    }

    :host ::ng-deep .e-grid .e-rowcell {
      font-size: 0.82rem;
      padding: var(--space-1) var(--space-2);
    }

    /* Row states */
    :host ::ng-deep .e-grid .e-row.row-selected .e-rowcell {
      background: var(--bs-primary-bg-subtle) !important;
      font-weight: 500;
    }
    :host ::ng-deep .e-grid .e-row.row-selected .e-freezeleftborder {
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
    .btn-del:hover { color: var(--bs-danger); }

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

    /* ── Color swatch dot ── */

    .color-dot {
      display: inline-block;
      width: 12px;
      height: 12px;
      border-radius: 50%;
      vertical-align: middle;
      border: 1px solid var(--bs-border-color);
      flex-shrink: 0;
    }
    .color-dot--empty {
      border-style: dashed;
    }

    .frozen-cell-flex {
      display: flex;
      align-items: center;
      gap: var(--space-1);
      width: 100%;
    }
    .frozen-cell-name {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      flex: 1 1 auto;
      min-width: 60px;
    }
    .frozen-badge {
      font-size: 0.65rem;
      padding: 1px 5px;
      flex-shrink: 0;
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
      min-width: 52px;
    }
    .fee-amount {
      font-weight: 500;
      color: var(--bs-body-color);
    }
    .fee-sep {
      color: var(--bs-secondary-color);
      font-size: 0.65rem;
    }
    .fee-inherited {
      opacity: 0.55;
      font-style: italic;
    }
    .fee-from-badge,
    .fee-set-badge {
      font-size: 0.55rem;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.03em;
      padding: 1px 4px;
      border-radius: 3px;
      margin-left: 3px;
    }
    .fee-from-badge {
      border: 1px solid var(--bs-border-color);
      background: var(--bs-body-color);
      color: var(--bs-body-bg);
    }
    .fee-set-badge {
      border: 1px solid rgba(var(--bs-success-rgb), 0.4);
      background: rgba(var(--bs-success-rgb), 0.1);
      color: var(--bs-success);
    }
    .fee-modifier {
      font-size: 0.65rem;
      font-weight: 600;
      padding: 0 3px;
      border-radius: 3px;
      margin-left: 2px;
    }
    .fee-discount {
      color: var(--bs-success-text-emphasis);
      background: var(--bs-success-bg-subtle);
    }
    .fee-latefee {
      color: var(--bs-danger-text-emphasis);
      background: var(--bs-danger-bg-subtle);
    }
  `]
})
export class LadtSiblingGridComponent implements OnChanges {
  @Input() columns: LadtColumnDef[] = [];
  @Input() data: any[] = [];
  @Input() selectedId = '';
  @Input() idField = 'id';
  @Input() levelLabel = '';
  @Input() levelIcon = 'bi-list';
  @Input() parentParts: ParentBreadcrumb[] = [];

  @Input() level = 0; // 0=league, 1=agegroup, 2=division, 3=team
  @Input() canDeleteFn: (row: any) => boolean = () => true;

  @Output() rowSelected = new EventEmitter<string>();
  @Output() drillDown = new EventEmitter<string>();
  @Output() editRow = new EventEmitter<string>();
  @Output() deleteRow = new EventEmitter<string>();
  @Output() addSibling = new EventEmitter<void>();
  @Output() navigateTo = new EventEmitter<string>();

  @ViewChild('grid') grid!: GridComponent;

  // Sort state
  sortField = signal<string | null>(null);
  sortDirection = signal<'asc' | 'desc'>('asc');

  // Internal signal for data (bridges @Input to computed)
  dataSignal = signal<any[]>([]);

  // Frozen column count (action col + frozen data cols)
  frozenCount = computed(() => countFrozenColumns(this.columns));

  // Team level (3) has pencil + trash + drill-up badge; others need less
  actionColWidth = computed(() => this.level >= 2 ? 110 : 90);

  sortedData = computed(() => {
    const rows = this.dataSignal();
    const field = this.sortField();

    return [...rows].sort((a, b) => {
      // Always push special/inactive/Unassigned rows to the bottom
      const aUnassigned = (a['divName'] ?? a['agegroupName'] ?? '').toUpperCase() === 'UNASSIGNED';
      const bUnassigned = (b['divName'] ?? b['agegroupName'] ?? '').toUpperCase() === 'UNASSIGNED';
      if (aUnassigned !== bUnassigned) return aUnassigned ? 1 : -1;

      const aBottom = a['_isSpecial'] === true || a['active'] === false;
      const bBottom = b['_isSpecial'] === true || b['active'] === false;
      if (aBottom !== bBottom) return aBottom ? 1 : -1;

      if (!field) return 0;
      const dir = this.sortDirection() === 'asc' ? 1 : -1;
      const va = a[field];
      const vb = b[field];
      if (va == null && vb == null) return 0;
      if (va == null) return 1;
      if (vb == null) return -1;
      if (typeof va === 'boolean') return (va === vb ? 0 : va ? -1 : 1) * dir;
      if (typeof va === 'number') return (va - vb) * dir;
      return String(va).localeCompare(String(vb)) * dir;
    });
  });

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['data']) {
      this.dataSignal.set(this.data);
      // Reset sort when data source changes (different entity selected)
      this.sortField.set(null);
      this.sortDirection.set('asc');
    }
  }

  // ── Syncfusion event handlers ──

  onActionBegin(args: any): void {
    if (args.requestType === 'sorting') {
      args.cancel = true; // prevent SF default sort — we sort via computed signal
      if (args.columnName) {
        const newDir = args.direction === 'Ascending' ? 'asc' : 'desc';
        this.sortField.set(args.columnName);
        this.sortDirection.set(newDir as 'asc' | 'desc');
      }
    }
  }

  onRowDataBound(args: any): void {
    const row = args.data;
    if (!row || !args.row) return;

    if (row[this.idField] === this.selectedId) {
      args.row.classList.add('row-selected');
    }
    if (row['active'] === false) {
      args.row.classList.add('inactive-row');
    }
    if (row['_isSpecial'] === true) {
      args.row.classList.add('special-row');
    }
  }

  onRowSelect(args: any): void {
    const id = args.data?.[this.idField];
    if (id) {
      this.rowSelected.emit(id);
    }
  }

  // ── Helpers ──

  parseWidth(width: string | undefined): number {
    return parseInt(width ?? '120', 10);
  }

  getTextAlign(col: LadtColumnDef): string {
    if (col.type === 'boolean') return 'Center';
    if (col.type === 'number' || col.type === 'currency') return 'Right';
    return 'Left';
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
