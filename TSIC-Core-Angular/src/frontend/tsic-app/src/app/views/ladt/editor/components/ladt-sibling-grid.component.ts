import { Component, Input, Output, EventEmitter, ChangeDetectionStrategy, signal, computed, OnChanges, SimpleChanges } from '@angular/core';
import { DecimalPipe, NgClass } from '@angular/common';
import type { LadtColumnDef } from '../configs/ladt-grid-columns';

export interface ParentBreadcrumb {
  name: string;
  level: number;
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
        <span class="text-body-secondary ms-2">under</span>
        @for (part of parentParts; track part.level) {
          <span class="badge ms-1" [ngClass]="getBadgeClass(part.level)">
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
            <th class="frozen-col row-num-col" [style.left.px]="0">#</th>
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
                (click)="rowSelected.emit(row[idField])">
              <td class="frozen-col row-num-col cell-num" [style.left.px]="0">{{ i + 1 }}</td>
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
                    @default {
                      @if (col.colorField && row[col.colorField]) {
                        <span class="color-dot" [style.background]="row[col.colorField]"></span>
                      }
                      {{ row[col.field] ?? '' }}
                    }
                  }
                </td>
              }
            </tr>
          } @empty {
            <tr>
              <td [attr.colspan]="columns.length + 1" class="text-center text-body-secondary py-4">
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

    /* Color swatch dot for columns with colorField */
    .color-dot {
      display: inline-block;
      width: 10px;
      height: 10px;
      border-radius: 50%;
      margin-right: 4px;
      vertical-align: middle;
      border: 1px solid var(--bs-border-color);
    }

    /* Cell type alignment */
    .cell-bool {
      text-align: center;
    }

    .cell-num {
      text-align: right;
      font-variant-numeric: tabular-nums;
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

  @Output() rowSelected = new EventEmitter<string>();

  // Sort state
  sortField = signal<string | null>(null);
  sortDirection = signal<'asc' | 'desc'>('asc');

  // Internal signal for data (bridges @Input to computed)
  dataSignal = signal<any[]>([]);

  sortedData = computed(() => {
    const rows = this.dataSignal();
    const field = this.sortField();
    if (!field) return rows;

    const dir = this.sortDirection() === 'asc' ? 1 : -1;
    return [...rows].sort((a, b) => {
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
    let left = 40; // starts after the 40px row-number column
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
