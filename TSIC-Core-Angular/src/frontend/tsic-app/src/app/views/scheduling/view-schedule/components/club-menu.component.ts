import {
    booleanAttribute,
    ChangeDetectionStrategy,
    Component,
    computed,
    DestroyRef,
    ElementRef,
    EmbeddedViewRef,
    Injector,
    TemplateRef,
    ViewContainerRef,
    afterNextRender,
    inject,
    input,
    output,
    signal,
    viewChild
} from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, catchError, fromEvent, map, merge, of, switchMap } from 'rxjs';
import type { ClubTeamsResponse } from '@core/api';
import { CLUB_TEAMS_SOURCE } from '../services/club-teams-source';

/** Exactly one of top/bottom and one of left/right is set; null removes the style. */
interface MenuState {
    readonly top: number | null;
    readonly bottom: number | null;
    readonly left: number | null;
    readonly right: number | null;
    readonly maxHeight: number;
    readonly status: 'loading' | 'ready' | 'error';
    readonly data: ClubTeamsResponse | null;
}

/**
 * A dropdown of a club's teams on the event schedule; picking one emits its teamId. Two
 * triggers, one menu: inline club name + caret, or — `pill` — the TEAM name as a picker pill
 * (the team panel's heading and opponents), where the menu opens on that team so a sibling is
 * one pick away. The games grid's club name is plain text by ruling: no club dropdown there.
 *
 * Inline by design: the host is an inline element and the trigger an inline span, so in the
 * grid the club name stays part of the cell's single text run (star, hanging indent, wrap).
 * Font, size, weight and colour INHERIT — each host styles the club for its context (grid:
 * semibold -xs emphasis ink; panel heading: the xl title).
 *
 * The teams are looked up on open, never carried with the schedule. The lookup comes from
 * CLUB_TEAMS_SOURCE, provided by the host page; with none provided the club is plain text.
 *
 * PORTALED to <body>: the team panel slides in with a transform, and a transform makes
 * position: fixed resolve against the panel instead of the viewport — a fixed menu inside
 * it would land in the wrong place. The menu's template is instantiated through this
 * component's ViewContainerRef (so change detection and encapsulated styles still apply)
 * and its root nodes are moved to <body>; destroying the view removes them.
 */
@Component({
    selector: 'app-club-menu',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @if (source) {
            <span #trigger class="club-trigger" role="button" tabindex="0" aria-haspopup="menu"
                  [class.club-trigger--pill]="pill()"
                  [attr.aria-expanded]="menu() !== null"
                  [attr.aria-label]="pill() ? display() + ' — show ' + club() + ' teams' : 'Show ' + club() + ' teams'"
                  (click)="toggle($event)"
                  (keydown.enter)="toggle($event)"
                  (keydown.space)="$event.preventDefault(); toggle($event)">@if (pill()) {<span class="club-trigger__text">{{ display() }}</span><i class="bi bi-caret-down-fill club-caret" aria-hidden="true"></i>} @else {{{ display() }}&nbsp;<i class="bi bi-caret-down-fill club-caret" aria-hidden="true"></i>}</span>
        } @else {
            {{ display() }}
        }

        <ng-template #menuTpl>
            <div class="club-menu-backdrop" (click)="close(false)"></div>
            @let m = menu();
            <div class="club-menu" role="menu" tabindex="-1"
                 [attr.aria-label]="(m?.data?.clubName ?? club()) + ' teams'"
                 [attr.aria-busy]="m?.status === 'loading'"
                 [style.top.px]="m?.top" [style.bottom.px]="m?.bottom"
                 [style.left.px]="m?.left" [style.right.px]="m?.right"
                 [style.max-height.px]="m?.maxHeight"
                 (keydown)="onMenuKeydown($event)">
                @switch (m?.status) {
                    @case ('loading') { <div class="club-menu-note">Loading teams…</div> }
                    @case ('error') { <div class="club-menu-note">Couldn't load teams.</div> }
                    @default {
                        <div class="club-menu-head">{{ m?.data?.clubName || club() }}</div>
                        @for (t of m?.data?.teams ?? []; track t.teamId) {
                            <button type="button" class="club-menu-item" role="menuitem" tabindex="-1"
                                    [class.is-current]="t.teamId === teamId()"
                                    [attr.aria-current]="t.teamId === teamId() ? 'true' : null"
                                    (click)="choose(t.teamId)">
                                <span class="club-menu-ag">{{ t.agegroupName }}</span>
                                <span class="club-menu-team">{{ t.teamName }}</span>
                            </button>
                        } @empty {
                            <div class="club-menu-note">No scheduled teams.</div>
                        }
                    }
                }
            </div>
        </ng-template>
    `,
    styles: [`
        :host { display: inline; }

        /* No dotted underline — that affordance belongs to team links (they open ONE team;
           this opens a list). The always-visible caret is the cue, so touch gets it too. The
           &nbsp; before the caret glues it to the last word: it never wraps onto a line alone. */
        .club-trigger {
            cursor: pointer;
            border-radius: var(--radius-sm);
        }
        .club-trigger:hover,
        .club-trigger[aria-expanded="true"] {
            color: var(--bs-primary);
        }
        .club-trigger:focus-visible {
            outline: none;
            color: var(--bs-primary);
            box-shadow: var(--shadow-focus);
        }

        /* Pill: the TEAM as a picker — "2027 Black ▾" — framed so it reads as a control you
           change, not a name you read. The caret sits at the trailing edge; the text wraps
           inside the pill rather than truncating. */
        .club-trigger--pill {
            display: inline-flex;
            align-items: baseline;
            gap: var(--space-1);
            max-width: 100%;
            padding: 0 var(--space-2);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-full);
            background: var(--bs-body-bg);
        }
        .club-trigger--pill:hover,
        .club-trigger--pill[aria-expanded="true"],
        .club-trigger--pill:focus-visible {
            border-color: var(--bs-primary);
        }
        .club-trigger__text {
            min-width: 0;
            overflow-wrap: anywhere;
        }
        /* text-indent: 0 on the icon AND its ::before (the inline-block box Bootstrap Icons
           draws into) — the grid's away cell has a negative hanging indent, inherited otherwise. */
        .club-caret,
        .club-caret::before {
            text-indent: 0;
        }
        .club-caret {
            font-size: 0.6em;
            font-weight: 400;
            color: var(--bs-secondary-color);
        }
        .club-trigger:hover .club-caret,
        .club-trigger:focus-visible .club-caret,
        .club-trigger[aria-expanded="true"] .club-caret {
            color: currentColor;
        }

        /* Menu — lives under <body> (see the class doc). Transparent full-viewport backdrop
           takes the outside click, above the fly-in (1050) and the header menus. Two-track
           subgrid: every age group in one column, every team name in the next. */
        .club-menu-backdrop {
            position: fixed;
            inset: 0;
            z-index: 10000;
        }
        .club-menu {
            position: fixed;
            z-index: 10001;
            display: grid;
            grid-template-columns: auto minmax(0, 1fr);
            align-content: start;
            min-width: 14rem;
            max-width: min(22rem, calc(100vw - 2 * var(--space-4)));
            overflow-y: auto;
            padding: var(--space-1) 0;
            background: var(--bs-body-bg);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--bs-border-radius-lg);
            box-shadow: var(--shadow-lg);
            font-family: var(--bs-body-font-family);
            font-size: var(--font-size-xs);
            font-weight: 400;
            line-height: 1.5;
            color: var(--bs-body-color);
            text-align: start;
        }
        .club-menu:focus-visible {
            outline: none;
        }
        .club-menu-head,
        .club-menu-note {
            grid-column: 1 / -1;
            padding: var(--space-1) var(--space-3);
        }
        .club-menu-head {
            font-weight: 600;
            color: var(--bs-emphasis-color);
            border-bottom: 1px solid var(--bs-border-color);
            margin-bottom: var(--space-1);
        }
        .club-menu-note {
            color: var(--bs-secondary-color);
        }
        .club-menu-item {
            grid-column: 1 / -1;
            display: grid;
            grid-template-columns: subgrid;
            column-gap: var(--space-3);
            align-items: baseline;
            padding: var(--space-1) var(--space-3);
            border: none;
            background: transparent;
            font: inherit;
            color: var(--bs-body-color);
            text-align: start;
            cursor: pointer;
        }
        .club-menu-item:hover,
        .club-menu-item:focus-visible {
            outline: none;
            background: var(--bs-primary-bg-subtle);
            color: var(--bs-primary);
        }
        .club-menu-ag {
            color: var(--bs-secondary-color);
            white-space: nowrap;
        }
        .club-menu-item:hover .club-menu-ag,
        .club-menu-item:focus-visible .club-menu-ag {
            color: inherit;
        }
        /* The team the menu was opened from: semibold + a primary inset bar on the leading
           edge, so it stays marked after focus moves off it (not colour alone — weight too). */
        .club-menu-item.is-current {
            font-weight: 600;
            box-shadow: inset 3px 0 0 var(--bs-primary);
        }
    `]
})
export class ClubMenuComponent {
    /** Club name — the menu's subject (heading fallback, accessible names). */
    readonly club = input.required<string>();
    /** Trigger text when it is not the club — the team name, in pill mode. */
    readonly label = input<string | null>(null);
    /** Render the trigger as a team-picker pill (bordered, caret trailing) instead of inline
     *  club text. Same menu either way. */
    readonly pill = input(false, { transform: booleanAttribute });
    protected readonly display = computed(() => this.label() ?? this.club());
    /** Any team of the club — the lookup key, and the entry marked current in the list. */
    readonly teamId = input.required<string>();
    /** Which edge of the club name the menu hangs from: 'end' for right-aligned text
     *  (the grid's home column), 'start' everywhere else. */
    readonly align = input<'start' | 'end'>('start');
    /** A team was chosen — the host opens it. */
    readonly pick = output<string>();

    protected readonly source = inject(CLUB_TEAMS_SOURCE, { optional: true });
    protected readonly menu = signal<MenuState | null>(null);

    private readonly trigger = viewChild<ElementRef<HTMLElement>>('trigger');
    private readonly menuTpl = viewChild.required<TemplateRef<unknown>>('menuTpl');
    private readonly vcr = inject(ViewContainerRef);
    private readonly injector = inject(Injector);
    private readonly doc = inject(DOCUMENT);
    private readonly requests = new Subject<string>();
    private view: EmbeddedViewRef<unknown> | null = null;

    constructor() {
        // switchMap drops a stale lookup if the menu is re-opened; the teamId check drops a
        // response that lands after the menu closed.
        this.requests.pipe(
            switchMap(teamId => {
                if (!this.source) return of({ teamId, data: null as ClubTeamsResponse | null });
                return this.source.loadClubTeams(teamId).pipe(
                    map(data => ({ teamId, data: data as ClubTeamsResponse | null })),
                    catchError(() => of({ teamId, data: null as ClubTeamsResponse | null })));
            }),
            takeUntilDestroyed()
        ).subscribe(({ teamId, data }) => {
            const m = this.menu();
            if (!m || teamId !== this.teamId()) return;
            this.menu.set({ ...m, status: data ? 'ready' : 'error', data });
            afterNextRender(() => {
                this.clampIntoViewport();
                // Open on the team the menu was opened from; first entry if it isn't listed.
                const items = this.items();
                const start = Math.max(0, data?.teams.findIndex(t => t.teamId === teamId) ?? 0);
                this.focusItem(items, start);
            }, { injector: this.injector });
        });

        // The menu is fixed to the viewport, so it would float free of its club name on
        // scroll or resize — close instead. Capture phase sees scrolls of ANY container (the
        // fly-in body scrolls, not the window); the menu's own list scroll is exempt.
        merge(
            fromEvent<Event>(this.doc, 'scroll', { capture: true }),
            fromEvent<Event>(this.doc.defaultView ?? window, 'resize')
        ).pipe(takeUntilDestroyed()).subscribe(ev => {
            if (!this.menu()) return;
            const el = this.menuEl();
            if (el && ev.target instanceof Node && el.contains(ev.target)) return;
            this.close(false);
        });

        inject(DestroyRef).onDestroy(() => this.destroyView());
    }

    protected toggle(ev: Event): void {
        ev.stopPropagation();
        if (this.menu()) { this.close(true); return; }

        const rect = (ev.currentTarget as HTMLElement).getBoundingClientRect();
        const win = this.doc.defaultView ?? window;
        const gap = 4;
        const margin = 16;
        const roomBelow = win.innerHeight - rect.bottom - gap - margin;
        const roomAbove = rect.top - gap - margin;
        const openUp = roomBelow < 240 && roomAbove > roomBelow;
        const end = this.align() === 'end';

        this.menu.set({
            top: openUp ? null : rect.bottom + gap,
            bottom: openUp ? win.innerHeight - rect.top + gap : null,
            left: end ? null : rect.left,
            right: end ? win.innerWidth - rect.right : null,
            maxHeight: Math.max(160, openUp ? roomAbove : roomBelow),
            status: 'loading',
            data: null
        });

        this.view = this.vcr.createEmbeddedView(this.menuTpl());
        for (const node of this.view.rootNodes) this.doc.body.appendChild(node);
        this.view.detectChanges();
        this.clampIntoViewport();
        // Focus the menu itself while loading so Escape works before the items exist.
        this.menuEl()?.focus();
        this.requests.next(this.teamId());
    }

    protected close(returnFocus: boolean): void {
        if (!this.menu()) return;
        this.menu.set(null);
        this.destroyView();
        if (returnFocus) this.trigger()?.nativeElement.focus();
    }

    protected choose(teamId: string): void {
        this.close(false);
        this.pick.emit(teamId);
    }

    protected onMenuKeydown(ev: KeyboardEvent): void {
        const items = this.items();
        const idx = items.indexOf(this.doc.activeElement as HTMLElement);
        switch (ev.key) {
            case 'Escape':
            case 'Tab':
                // Tab too: the menu sits at the end of <body>, so letting Tab through would
                // drop focus somewhere unrelated. Close and hand focus back to the club name.
                ev.preventDefault();
                this.close(true);
                return;
            case 'ArrowDown':
                ev.preventDefault();
                this.focusItem(items, idx < 0 ? 0 : (idx + 1) % items.length);
                return;
            case 'ArrowUp':
                ev.preventDefault();
                this.focusItem(items, idx <= 0 ? items.length - 1 : idx - 1);
                return;
            case 'Home':
                ev.preventDefault();
                this.focusItem(items, 0);
                return;
            case 'End':
                ev.preventDefault();
                this.focusItem(items, items.length - 1);
                return;
        }
    }

    private menuEl(): HTMLElement | null {
        const node = this.view?.rootNodes.find(n => n instanceof HTMLElement && n.classList.contains('club-menu'));
        return (node as HTMLElement | undefined) ?? null;
    }

    private items(): HTMLElement[] {
        const el = this.menuEl();
        return el ? Array.from(el.querySelectorAll<HTMLElement>('[role="menuitem"]')) : [];
    }

    private focusItem(items: HTMLElement[], i: number): void {
        const item = items[i];
        item?.focus({ preventScroll: true });
        item?.scrollIntoView({ block: 'nearest' });
    }

    /** Anchored to one edge, a wide list can run off the other side on a narrow screen —
     *  pull it back inside with a margin. Direct style write: a one-off correction after
     *  layout, not state. */
    private clampIntoViewport(): void {
        const el = this.menuEl();
        if (!el) return;
        const vw = (this.doc.defaultView ?? window).innerWidth;
        const margin = 8;
        const r = el.getBoundingClientRect();
        if (r.right > vw - margin) {
            el.style.right = '';
            el.style.left = `${Math.max(margin, vw - r.width - margin)}px`;
        } else if (r.left < margin) {
            el.style.right = '';
            el.style.left = `${margin}px`;
        }
    }

    private destroyView(): void {
        this.view?.destroy();
        this.view = null;
    }
}
