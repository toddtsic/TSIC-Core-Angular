import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * A single resolved destination. In the live system these come from the unified
 * nav resolver (one row per destination, gated by role / requiresPulse /
 * requiresUser, placed by `placement`). For this visual prototype the items are
 * static and render as buttons (no navigation) — live wiring swaps the buttons
 * for `routerLink` / `href` anchors.
 */
export interface HubItem {
    key: string;
    label: string;
    icon: string;                 // full bootstrap class, e.g. 'bi-person-plus'
    hint?: string;                // optional tiny second line on hero cards (e.g. audience: "For registered players")
    emphasis?: 'primary' | 'secondary';
    routerLink?: string;          // path only — query string must NOT be embedded here
    queryParams?: Record<string, string>; // bound to [queryParams] (routerLink won't parse a '?' out of a string)
    href?: string;                // reserved for live wiring
}

/** A titled group of MainNav destinations, rendered as one column of the mega-panel. */
export interface HubGroup {
    key: string;
    label: string;
    icon?: string;
    items: HubItem[];
}

/**
 * Action Hub — the single adaptive menu surface. One resolved list, three
 * placements: hero action cards (LandingCta), the mega-panel / mobile sheet
 * (MainNav), and the top-right account popover (UserMenu). No floating nested
 * dropdowns (avoids the backdrop-filter containing-block trap); the panel is a
 * full-width block on desktop and a bottom sheet on phone.
 */
@Component({
    selector: 'app-action-hub',
    standalone: true,
    imports: [RouterLink],
    templateUrl: './action-hub.component.html',
    styleUrl: './action-hub.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActionHubComponent {
    readonly jobName = input('');
    readonly showHero = input(true);
    /** Hero-only mode: render just the action strip (no top bar / panel / tab bar). */
    readonly bare = input(false);
    readonly heroActions = input<HubItem[]>([]);
    readonly navGroups = input<HubGroup[]>([]);
    readonly accountItems = input<HubItem[]>([]);

    /** Mega-panel (desktop) / bottom sheet (mobile) open state — one signal, two skins. */
    readonly menuOpen = signal(false);
    /** Top-right account popover. */
    readonly accountOpen = signal(false);

    /** Phone bottom-bar quick tabs: the first four MainNav destinations. */
    readonly quickTabs = computed<HubItem[]>(() =>
        this.navGroups().flatMap(g => g.items).slice(0, 4)
    );

    toggleMenu(): void {
        this.menuOpen.update(v => !v);
        this.accountOpen.set(false);
    }

    closeMenu(): void {
        this.menuOpen.set(false);
    }

    toggleAccount(): void {
        this.accountOpen.update(v => !v);
        this.menuOpen.set(false);
    }

    closeAll(): void {
        this.menuOpen.set(false);
        this.accountOpen.set(false);
    }
}
