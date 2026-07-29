import { DestroyRef, Directive, Input, OnChanges, OnInit, SimpleChanges, inject } from '@angular/core';
import { GridComponent } from '@syncfusion/ej2-angular-grids';

/**
 * Row numbers for a Syncfusion grid — THE one implementation.
 *
 * Eight grids used to hand-copy this pattern, and the copies drifted into three variants; one of
 * them (change-password) drifted into a form that crashed the whole grid render and sat broken for
 * two weeks until a real user hit it (`12cbf0a1`). This directive is the pattern, centralized. Do
 * not re-grow a private `refreshRowNumbers` in a component.
 *
 * WHY STAMPED, NOT BOUND. The number is an ON-SCREEN ordinal — an admin reading a list down a phone
 * call needs "the fourth one" to be the fourth one they can see. A bound field travels with its row,
 * which after a sort leaves the column reading 7, 3, 1. So the column is unbound, and this directive
 * stamps 1..N over the rendered rows on every render the grid announces: `dataBound`, plus
 * `actionComplete` for sorting/paging/refresh, which re-render rows WITHOUT firing `dataBound`.
 *
 * Usage — the attribute on the grid, and the one-line column recipe:
 *
 *     <ejs-grid tsicRowNumbers ...>
 *       <e-columns>
 *         <e-column headerText="#" width="50" [allowSorting]="false"
 *                   [customAttributes]="{ class: 'row-number-cell' }"></e-column>
 *
 * Width, alignment and freeze on the `#` column are the grid's own business. Everything else is not:
 *
 *   ONE CLASS TOKEN in `customAttributes.class`. Syncfusion hands the string to `DOMTokenList.add()`,
 *   and a space throws `InvalidCharacterError` — inside the grid's own render frame, so the WHOLE
 *   table dies unrendered over a perfectly good result set, with no toast and nothing in the UI.
 *   That is the `12cbf0a1` bug. Style `td.row-number-cell` in the grid's SCSS instead.
 *
 * The cells are selected by that class, in document order — which is visual order whatever the
 * freeze layout, legacy two-table panes included, because the `#` column exists exactly once per
 * row. A grid that hides the column (search-registrations on mobile) simply stamps nothing.
 *
 * PAGING. By default a paged grid numbers CONTINUOUSLY — page 2 of 20 starts at 21; the offset is
 * read off the grid's own pager. Two reasons to bind `tsicRowNumbersOffset` instead:
 *
 *   - Server-side paging: the grid's pager doesn't know the real page, so the component binds its
 *     own `(page - 1) * pageSize` (search-registrations).
 *   - Restart-per-page numbering: bind 0 (registered-teams-grid does, preserving its behavior).
 */
@Directive({
    selector: 'ejs-grid[tsicRowNumbers]',
    standalone: true
})
export class GridRowNumbersDirective implements OnInit, OnChanges {
    /** The host grid — element-injector DI: the directive sits on the `ejs-grid` element itself. */
    private readonly grid = inject(GridComponent);
    private readonly destroyRef = inject(DestroyRef);

    /**
     * Explicit numbering offset. Unbound = automatic (the grid's own pager when it pages, else 0).
     * Deliberately a decorator input, not `input()`: reacting to a change uses `ngOnChanges`, and
     * signal inputs never reach it.
     */
    @Input('tsicRowNumbersOffset') offset?: number;

    ngOnInit(): void {
        // The wrapper creates every event's EventEmitter whether or not a template binds it, and a
        // subscription here does not displace a component's own (dataBound)="..." handler.
        const dataBound = this.grid.dataBound.subscribe(() => this.stamp());
        const action = this.grid.actionComplete.subscribe((args: { requestType?: string }) => {
            if (args.requestType === 'sorting' || args.requestType === 'paging' || args.requestType === 'refresh') {
                this.stamp();
            }
        });

        this.destroyRef.onDestroy(() => {
            dataBound.unsubscribe();
            action.unsubscribe();
        });
    }

    /** A server-paged grid re-binds the offset when its page changes — restamp without a grid event. */
    ngOnChanges(changes: SimpleChanges): void {
        if (changes['offset'] && !changes['offset'].firstChange) {
            this.stamp();
        }
    }

    private stamp(): void {
        const offset = this.offset ?? this.autoOffset();
        this.grid.element
            ?.querySelectorAll('td.row-number-cell')
            .forEach((cell, i) => { cell.textContent = String(offset + i + 1); });
    }

    private autoOffset(): number {
        if (!this.grid.allowPaging) return 0;
        const page = this.grid.pageSettings?.currentPage ?? 1;
        const size = this.grid.pageSettings?.pageSize ?? 0;
        return (page - 1) * size;
    }
}
