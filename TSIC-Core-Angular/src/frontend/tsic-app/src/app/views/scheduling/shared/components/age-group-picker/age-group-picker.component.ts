import {
    ChangeDetectionStrategy,
    Component,
    ElementRef,
    HostListener,
    computed,
    inject,
    input,
    output,
    signal,
} from '@angular/core';

/** One selectable age group. `color` is the resolved hex (or null for none). */
export interface AgePickerItem {
    id: string;
    label: string;
    color: string | null;
    /** Optional right-aligned count, matching the age-group rails elsewhere. Omit for none. */
    count?: number | null;
}

/**
 * Age-group picker — a compact "dot + name" dropdown for navigating age groups,
 * ported from the TSIC-Events-2025 mobile app (app-agegroup-picker, Ionic). An
 * event routinely carries dozens of age groups; a horizontal tab strip hides most
 * off-screen on a phone, so a single-select dropdown that scrolls internally is the
 * better tool. Trigger and each row read the same way: a small dot of the group's
 * color followed by neutral text — color as a quiet identity marker, not a wall of
 * saturated pills.
 *
 * Shared by the Standings and Brackets tabs, which both already compute the color +
 * label per age group. Selection is id-based; callers map their index to a string id.
 *
 * Custom popover (not Syncfusion, not a native select): a native <option> can't
 * render a per-row color dot, and this keeps the surface on our design tokens. No
 * effect() (banned) — open state is a plain signal; outside-click/Escape close via
 * HostListener. No backdrop-filter (banned) — the popover is a solid elevated surface.
 */
@Component({
    selector: 'app-age-group-picker',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <button type="button" class="ag-picker-trigger"
                [disabled]="!items().length"
                aria-haspopup="listbox"
                [attr.aria-expanded]="open()"
                [attr.aria-label]="'Age group: ' + (selectedItem()?.label || emptyLabel())"
                (click)="toggle()">
            <span class="ag-dot" [class.ag-dot--empty]="!selectedItem()?.color"
                  [style.background]="selectedItem()?.color || null"></span>
            <span class="ag-picker-label">{{ selectedItem()?.label || emptyLabel() }}</span>
            <i class="bi bi-chevron-down ag-picker-chevron" [class.is-open]="open()" aria-hidden="true"></i>
        </button>

        @if (open()) {
            <!-- Capped height so a large event scrolls inside the popover rather than
                 running off the screen. -->
            <div class="ag-picker-popover" role="listbox"
                 [attr.aria-label]="'Select age group'">
                @for (item of items(); track item.id) {
                    <button type="button" class="ag-picker-item" role="option"
                            [class.selected]="item.id === selectedId()"
                            [attr.aria-selected]="item.id === selectedId()"
                            (click)="select(item)">
                        <span class="ag-dot" [class.ag-dot--empty]="!item.color"
                              [style.background]="item.color || null"></span>
                        <span class="ag-picker-label">{{ item.label }}</span>
                        @if (item.count !== null && item.count !== undefined) {
                            <span class="badge bg-secondary-subtle text-secondary-emphasis rounded-pill ag-picker-count">
                                {{ item.count }}
                            </span>
                        }
                        @if (item.id === selectedId()) {
                            <i class="bi bi-check2 ag-picker-check" aria-hidden="true"></i>
                        }
                    </button>
                }
            </div>
        }
    `,
    styles: [`
        :host {
            position: relative;
            display: inline-flex;
            align-items: center;
        }

        /* Trigger + rows share one language: a color dot + neutral text. */
        .ag-picker-trigger {
            display: inline-flex;
            align-items: center;
            gap: var(--space-2);
            padding: var(--space-1) var(--space-2);
            background: transparent;
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-sm);
            color: var(--bs-body-color);
            cursor: pointer;
            transition: border-color 0.15s, background-color 0.15s;
        }
        .ag-picker-trigger:hover { background: var(--bs-secondary-bg); }
        .ag-picker-trigger:disabled { opacity: 0.5; cursor: default; }
        .ag-picker-trigger:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
        }

        /* Small filled dot of the age-group's color. The hairline inset ring keeps
           light dots (white / yellow / lavender) visible on any surface. Empty
           (no color) → dashed neutral ring, matching the .ag-dot--empty convention
           used elsewhere in the schedule. */
        .ag-dot {
            flex-shrink: 0;
            display: inline-block;
            width: 10px;
            height: 10px;
            border-radius: 50%;
            background: var(--bs-secondary-bg);
            box-shadow: inset 0 0 0 1px var(--bs-border-color);
        }
        .ag-dot--empty {
            background: transparent;
            box-shadow: none;
            border: 1px dashed var(--bs-border-color);
        }

        .ag-picker-label {
            font-size: var(--font-size-sm);
            font-weight: var(--font-weight-semibold);
            white-space: nowrap;
        }

        .ag-picker-chevron {
            font-size: var(--font-size-xs);
            color: var(--bs-secondary-color);
            transition: transform 0.15s ease;
        }
        .ag-picker-chevron.is-open { transform: rotate(180deg); }

        /* Solid elevated surface (gradient/shadow/border for depth — never
           backdrop-filter). Left-anchored under the trigger, dropping down-right so a
           left-placed trigger keeps the list on-screen; max-width guards the far edge. */
        .ag-picker-popover {
            position: absolute;
            top: calc(100% + var(--space-1));
            left: 0;
            z-index: 1000;
            min-width: 220px;
            max-width: 80vw;
            max-height: 60vh;
            overflow-y: auto;
            padding: var(--space-1);
            background: var(--bs-body-bg);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius);
            box-shadow: var(--shadow-lg);
        }

        .ag-picker-item {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            width: 100%;
            min-height: 40px;
            padding: var(--space-2) var(--space-3);
            background: transparent;
            border: none;
            border-radius: var(--radius-sm);
            color: var(--bs-body-color);
            text-align: left;
            cursor: pointer;
            transition: background-color 0.15s;
        }
        .ag-picker-item:hover { background: var(--bs-secondary-bg); }
        .ag-picker-item.selected { background: var(--bs-secondary-bg); }
        .ag-picker-item:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
        }
        /* The label takes the slack so the check pins to the right edge. */
        .ag-picker-item .ag-picker-label { flex: 1; }

        /* Right-aligned count, same treatment as the age-group rails. The label already
           takes the slack, so this and the check pin to the right edge in that order. */
        .ag-picker-count {
            flex-shrink: 0;
            font-size: var(--font-size-xs);
        }

        .ag-picker-check {
            flex-shrink: 0;
            font-size: var(--font-size-base);
            color: var(--bs-primary);
        }

        @media (prefers-reduced-motion: reduce) {
            .ag-picker-trigger,
            .ag-picker-chevron,
            .ag-picker-item { transition: none !important; }
        }
    `],
})
export class AgeGroupPickerComponent {
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

    items = input.required<AgePickerItem[]>();
    selectedId = input<string | null>(null);
    /** Trigger text when there is no selection / no items. */
    emptyLabel = input<string>('—');
    /** Emits the picked item's id. */
    selectionChange = output<string>();

    readonly open = signal(false);

    readonly selectedItem = computed(
        () => this.items().find(i => i.id === this.selectedId()) ?? null
    );

    toggle(): void {
        if (!this.items().length) return;
        this.open.update(o => !o);
    }

    select(item: AgePickerItem): void {
        this.open.set(false);
        this.selectionChange.emit(item.id);
    }

    // Trigger clicks live inside the host, so contains() is true and this handler
    // leaves them to toggle(); only genuine outside clicks close the popover.
    @HostListener('document:click', ['$event'])
    onDocumentClick(ev: MouseEvent): void {
        if (this.open() && !this.host.nativeElement.contains(ev.target as Node)) {
            this.open.set(false);
        }
    }

    @HostListener('document:keydown.escape')
    onEscape(): void {
        if (this.open()) this.open.set(false);
    }
}
