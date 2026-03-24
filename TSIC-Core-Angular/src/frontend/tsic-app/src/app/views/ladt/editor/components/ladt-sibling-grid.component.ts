import { Component, Input, Output, EventEmitter, ChangeDetectionStrategy, signal, computed, OnChanges, SimpleChanges } from '@angular/core';
import { DecimalPipe, NgClass } from '@angular/common';
import type { LadtColumnDef } from '../configs/ladt-grid-columns';

export interface ParentBreadcrumb {
  name: string;
  level: number;
  id: string;
}

@Component({
  selector: 'app-ladt-sibling-grid',
  standalone: true,
  imports: [DecimalPipe, NgClass],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sibling-grid-header">
      <i class="bi {{ levelIcon }} me-2"></i>
      <span class="fw-semibold">{{ levelLabel }}s</span>
      @if (parentParts.length) {
        <span class="text-body-secondary ms-2">in</span>
        @for (part of parentParts; track part.level) {
          <span class="badge ms-1 breadcrumb-link" [ngClass]="getBadgeClass(part.level)"
                (click)="navigateTo.emit(part.id); $event.stopPropagation()"
                title="Navigate to {{ part.name }}">
            <i class="bi {{ getPartIcon(part.level) }} me-1"></i>{{ part.name }}
          </span>
        }
      }
      <span class="badge bg-primary-subtle text-primary-emphasis ms-auto">{{ dataSignal().length }}</span>
    </div>

    <div class="sibling-grid-scroll">
      <table class="sibling-table">
        <thead>
          <tr>
            <th class="action-col-header frozen-col" [style.left.px]="0">
              @if (level > 0) {
                <span class="add-badge" title="Add {{ levelLabel }}"
                      (click)="addSibling.emit(); $event.stopPropagation()">
                  Add New {{ levelLabel }}
                </span>
              }
            </th>
            @for (col of columns; track col.field) {
              <th [class.frozen-col]="col.frozen"
                  [style.min-width]="col.width ?? '120px'"
                  [style.left.px]="col.frozen ? frozenOffsets().get(col.field) : null"
                  class="sortable"
                  (click)="toggleSort(col.field)">
                {{ col.header }}
                @if (sortField() === col.field) {
                  <i class="bi sort-icon"
                     [class.bi-caret-up-fill]="sortDirection() === 'asc'"
                     [class.bi-caret-down-fill]="sortDirection() === 'desc'"></i>
                }
              </th>
            }
          </tr>
        </thead>
        <tbody>
          @for (row of sortedData(); track row[idField]; let i = $index) {
            <tr [class.selected]="row[idField] === selectedId"
                [class.inactive-row]="row['active'] === false"
                [class.special-row]="row['_isSpecial'] === true"
                (click)="rowSelected.emit(row[idField])">
              <td class="action-col frozen-col" [style.left.px]="0">
                <button class="btn-action btn-edit" title="Edit"
                        (click)="editRow.emit(row[idField]); $event.stopPropagation()">
                  <i class="bi bi-pencil"></i>
                </button>
                @if (canDeleteFn(row)) {
                  <button class="btn-action btn-del" title="Delete"
                          (click)="deleteRow.emit(row[idField]); $event.stopPropagation()">
                    <i class="bi bi-trash"></i>
                  </button>
                }
                @if (!row['_isSpecial']) {
                  <span class="nav-badges">
                    @if (level === 2) {
                      <span class="drill-badge drill-up" title="Navigate to parent agegroup"
                            (click)="navigateTo.emit(row['_parentAgId']); $event.stopPropagation()">
                        <i class="bi bi-arrow-up-short"></i>A
                      </span>
                    }
                    @if (level === 3) {
                      <span class="drill-badge drill-up" title="Navigate to parent division"
                            (click)="navigateTo.emit(row['_parentDivId']); $event.stopPropagation()">
                        <i class="bi bi-arrow-up-short"></i>D
                      </span>
                    }
                    @if (level === 1 && (row['divisionCount'] ?? 0) > 0) {
                      <span class="drill-badge" title="Show {{ row['divisionCount'] }} divisions"
                            (click)="drillDown.emit(row[idField]); $event.stopPropagation()">
                        D<i class="bi bi-arrow-down-short"></i>{{ row['divisionCount'] }}
                      </span>
                    }
                    @if (level === 2 && (row['teamCount'] ?? 0) > 0) {
                      <span class="drill-badge" title="Show {{ row['teamCount'] }} teams"
                            (click)="drillDown.emit(row[idField]); $event.stopPropagation()">
                        T<i class="bi bi-arrow-down-short"></i>{{ row['teamCount'] }}
                      </span>
                    }
                  </span>
                }
              </td>
              @for (col of columns; track col.field) {
                <td [class.frozen-col]="col.frozen"
                    [style.left.px]="col.frozen ? frozenOffsets().get(col.field) : null"
                    [class.cell-bool]="col.type === 'boolean'"
                    [class.cell-num]="col.type === 'number' || col.type === 'currency'"
                    [title]="getCellTitle(row[col.field], col)">
                  @switch (col.type) {
                    @case ('boolean') {
                      @if (row[col.field] === true) {
                        <i class="bi bi-check-lg text-success"></i>
                      } @else if (row[col.field] === false) {
                        <i class="bi bi-x-lg text-danger opacity-50"></i>
                      }
                    }
                    @case ('currency') {
                      @if (row[col.field] != null) {
                        {{ row[col.field] | number:'1.2-2' }}
                      }
                    }
                    @case ('date') {
                      {{ formatDate(row[col.field]) }}
                    }
                    @case ('dateOnly') {
                      {{ formatDateOnly(row[col.field]) }}
                    }
                    @case ('fees') {
                      @if (row['_fees']?.length) {
                        <div class="fee-pills">
                          @for (fee of row['_fees']; track fee.roleId) {
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
                                [class.color-dot--empty]="!row[col.colorField]"
                                [style.background]="row[col.colorField] ?? 'var(--bs-secondary-bg)'"></span>
                          <span class="frozen-cell-name">{{ row[col.field] ?? '' }}</span>
                          @if (row['teamCount'] != null) {
                            <span class="badge bg-primary-subtle text-primary-emphasis frozen-badge">{{ row['teamCount'] | number }}</span>
                          }
                          @if (row['playerCount'] != null) {
                            <span class="badge bg-success-subtle text-success-emphasis frozen-badge">{{ row['playerCount'] | number }}</span>
                          }
                        </span>
                      } @else {
                        {{ row[col.field] ?? '' }}
                      }
                    }
                  }
                </td>
              }
            </tr>
          } @empty {
            <tr>
              <td [attr.colspan]="columns.length + 2" class="text-center text-body-secondary py-4">
                No items found.
              </td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
  styles: [`
    :host {
      display: flex;
      flex-direction: column;
      height: 100%;
    }

    .sibling-grid-header {
      display: flex;
      align-items: center;
      padding: var(--space-2) var(--space-3);
      border-bottom: 1px solid var(--bs-border-color);
      background: var(--bs-body-bg);
      flex-shrink: 0;
    }

    .sibling-grid-scroll {
      flex: 1;
      overflow: auto;
      -webkit-overflow-scrolling: touch;
    }

    .sibling-table {
      width: max-content;
      min-width: 100%;
      border-collapse: separate;
      border-spacing: 0;
      font-size: 0.82rem;
    }

    thead {
      position: sticky;
      top: 0;
      z-index: 2;
    }

    th {
      background: var(--bs-tertiary-bg);
      color: var(--bs-body-color);
      font-weight: 600;
      font-size: 0.78rem;
      text-transform: uppercase;
      letter-spacing: 0.02em;
      padding: var(--space-1) var(--space-2);
      white-space: nowrap;
      border-bottom: 2px solid var(--bs-border-color);
      border-right: 1px solid var(--bs-border-color);
      user-select: none;
    }

    th.sortable {
      cursor: pointer;

      &:hover {
        background: var(--bs-secondary-bg);
      }
    }

    .sort-icon {
      font-size: 0.65rem;
      margin-left: 0.25rem;
      color: var(--bs-primary);
    }

    td {
      padding: var(--space-1) var(--space-2);
      white-space: nowrap;
      border-bottom: 1px solid var(--bs-border-color);
      border-right: 1px solid var(--bs-border-color-translucent);
      color: var(--bs-body-color);
      max-width: 200px;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    /* Row number column */
    .row-num-col {
      width: 40px;
      min-width: 40px;
      max-width: 40px;
      text-align: center;
      color: var(--bs-secondary-color);
      font-variant-numeric: tabular-nums;
    }

    /* Frozen columns - left offsets set dynamically via [style.left.px] */
    .frozen-col {
      position: sticky;
      z-index: 1;
      background: var(--bs-body-bg);
      border-right: 2px solid var(--bs-border-color);
    }

    th.frozen-col {
      z-index: 3; /* above both sticky header and sticky column */
      background: var(--bs-tertiary-bg);
    }

    /* Row states */
    tbody tr {
      cursor: pointer;
      transition: background-color 0.1s;
    }

    tbody tr:hover td {
      background: var(--bs-tertiary-bg);
    }

    tbody tr:hover td.frozen-col {
      background: var(--bs-tertiary-bg);
    }

    tbody tr.selected td {
      background: var(--bs-primary-bg-subtle);
      font-weight: 500;
    }

    tbody tr.selected td.frozen-col {
      background: var(--bs-primary-bg-subtle);
      font-weight: 600;
      color: var(--bs-primary);
    }

    tbody tr.inactive-row {
      opacity: 0.55;

      td.frozen-col {
        text-decoration: line-through;
        font-style: italic;
      }
    }

    tbody tr.special-row {
      opacity: 0.6;

      td {
        font-style: italic;
        color: var(--bs-secondary-color);
      }
    }

    /* Color swatch dot for columns with colorField */
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

    /* Frozen cell with color dot + name + badges */
    .frozen-cell-flex {
      display: flex;
      align-items: center;
      gap: 4px;
      min-width: 0;
    }

    .frozen-cell-name {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      flex: 1;
      min-width: 0;
    }

    .frozen-badge {
      font-size: 0.65rem;
      padding: 1px 5px;
      flex-shrink: 0;
    }

    /* Cell type alignment */
    .cell-bool {
      text-align: center;
    }

    .cell-num {
      text-align: right;
      font-variant-numeric: tabular-nums;
    }

    /* Fee pills in grid cells */
    .fee-pills {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .fee-pill {
      display: flex;
      align-items: center;
      gap: 4px;
      font-size: 0.75rem;
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

    /* Action column (first column, frozen left) */
    .action-col-header {
      width: 90px;
      min-width: 90px;
      text-align: center;
    }

    .action-col {
      width: 90px;
      min-width: 90px;
      text-align: center;
      white-space: nowrap;
      border-right: 2px solid var(--bs-border-color);
    }

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
      font-size: 0.75rem;
      transition: all 0.15s;
    }

    .btn-action:hover {
      background: var(--bs-secondary-bg);
      color: var(--bs-body-color);
    }

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
      gap: 4px;
    }


    .btn-drill:hover { color: var(--bs-primary); }
    .btn-edit:hover { color: var(--bs-info); }
    .btn-del:hover { color: var(--bs-danger); }

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

    .breadcrumb-link {
      cursor: pointer;
      text-decoration: underline;
      transition: filter 0.15s;
    }
    .breadcrumb-link:hover {
      filter: brightness(0.85);
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
  @Output() navigateTo = new EventEmitter<string>(); // navigate to a breadcrumb node

  get drillLabel(): string {
    switch (this.level) {
      case 0: return 'agegroups';
      case 1: return 'divisions';
      case 2: return 'teams';
      default: return '';
    }
  }

  // Sort state
  sortField = signal<string | null>(null);
  sortDirection = signal<'asc' | 'desc'>('asc');

  // Internal signal for data (bridges @Input to computed)
  dataSignal = signal<any[]>([]);

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

  // Compute frozen column left offsets dynamically
  frozenOffsets = computed(() => {
    const offsets = new Map<string, number>();
    let left = 90; // starts after the 90px action column
    for (const col of this.columns) {
      if (col.frozen) {
        offsets.set(col.field, left);
        left += parseInt(col.width ?? '120', 10);
      }
    }
    return offsets;
  });

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['data']) {
      this.dataSignal.set(this.data);
      // Reset sort when data source changes (different entity selected)
      this.sortField.set(null);
      this.sortDirection.set('asc');
    }
  }

  toggleSort(field: string): void {
    if (this.sortField() === field) {
      this.sortDirection.update(d => d === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortField.set(field);
      this.sortDirection.set('asc');
    }
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

  getCellTitle(value: any, col: LadtColumnDef): string {
    if (value == null) return '';
    if (col.type === 'boolean') return value ? 'Yes' : 'No';
    if (col.type === 'currency') return `$${Number(value).toFixed(2)}`;
    return String(value);
  }
}
