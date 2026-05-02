import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';

import { NavigationEnd, Route, Router, RouterLink, RouterLinkActive } from '@angular/router';
import type { NavItemDto } from '@core/api';
import { JobService } from '@infrastructure/services/job.service';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { MenuStateService } from '../../services/menu-state.service';

@Component({
    selector: 'app-client-menu',
    standalone: true,
    imports: [RouterLink, RouterLinkActive],
    templateUrl: './client-menu.component.html',
    styleUrl: './client-menu.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ClientMenuComponent {
    private readonly jobService = inject(JobService);
    private readonly menuState = inject(MenuStateService);
    private readonly router = inject(Router);

    // Known child routes under :jobPath — includes both literal paths and wildcard prefixes
    // (e.g., 'reporting' from 'reporting/:action' so that 'reporting/get_netusers' matches)
    private readonly knownRoutes = this.buildKnownRoutes();
    private readonly wildcardPrefixes = this.buildWildcardPrefixes();

    // Access nav data from JobService
    menus = computed(() => this.jobService.navItems());
    menusLoading = computed(() => this.jobService.navLoading());
    menusError = computed(() => this.jobService.navError());

    // Current URL split into lowercase path + lowercase query params — reacts to every navigation.
    // Query params are kept so isParentActive() can disambiguate Type-2 report children that differ
    // only by ?spName=... (e.g. Reports vs Accounting both contain reporting/export-sp children).
    private readonly currentUrl = toSignal(
        this.router.events.pipe(
            filter((e): e is NavigationEnd => e instanceof NavigationEnd),
            map(e => this.parseUrl(e.urlAfterRedirects))
        ),
        { initialValue: this.parseUrl(this.router.url) }
    );

    private parseUrl(url: string): { path: string; params: URLSearchParams } {
        const [rawPath, rawQuery] = url.split('?');
        const path = rawPath.replace(/^\/+/, '').toLowerCase();
        const params = new URLSearchParams();
        if (rawQuery) {
            for (const [k, v] of new URLSearchParams(rawQuery)) {
                params.append(k.toLowerCase(), v.toLowerCase());
            }
        }
        return { path, params };
    }

    // Offcanvas state from shared service
    offcanvasOpen = this.menuState.offcanvasOpen;

    // Track which item's dropdown panel is open
    expandedItems = signal<Set<string>>(new Set());

    // Fixed-position coordinates for the open desktop dropdown panel
    dropdownPanelTop = signal(0);
    dropdownPanelLeft = signal(0);

    // Hover-to-open timer — short delay prevents flicker when moving between pill and panel
    private hoverCloseTimer: ReturnType<typeof setTimeout> | null = null;
    private readonly HOVER_CLOSE_DELAY = 150;

    /** Close all dropdown panels */
    collapseAll(): void {
        this.expandedItems.set(new Set());
    }

    /** Close offcanvas (mobile sidebar) */
    closeOffcanvas(): void {
        this.menuState.closeOffcanvas();
    }

    /** Desktop hover: open dropdown when mouse enters a parent group */
    onGroupMouseEnter(event: MouseEvent, menuItemId: string | number): void {
        this.clearHoverTimer();
        const normalizedId = String(menuItemId);
        if (this.isExpanded(normalizedId)) return;

        // Position panel below the <button> child inside the group wrapper
        const group = event.currentTarget as HTMLElement;
        const btn = group.querySelector('button') as HTMLElement;
        const rect = btn.getBoundingClientRect();
        const PANEL_MIN_WIDTH = 260;
        const left = Math.min(rect.left, window.innerWidth - PANEL_MIN_WIDTH - 8);
        this.dropdownPanelLeft.set(Math.max(8, left));
        this.dropdownPanelTop.set(rect.bottom + 4);
        this.expandedItems.set(new Set([normalizedId]));
    }

    /** Desktop hover: delayed close when mouse leaves the group (pill + panel) */
    onGroupMouseLeave(): void {
        this.startHoverCloseTimer();
    }

    private startHoverCloseTimer(): void {
        this.clearHoverTimer();
        this.hoverCloseTimer = setTimeout(() => {
            this.collapseAll();
        }, this.HOVER_CLOSE_DELAY);
    }

    private clearHoverTimer(): void {
        if (this.hoverCloseTimer) {
            clearTimeout(this.hoverCloseTimer);
            this.hoverCloseTimer = null;
        }
    }

    /** Toggle expansion for mobile accordion (unchanged behaviour) */
    toggleExpanded(menuItemId: string | number): void {
        const normalizedId = String(menuItemId);
        const expanded = this.expandedItems();
        const isCurrentlyExpanded = expanded.has(normalizedId);
        const newExpanded = new Set<string>();
        if (!isCurrentlyExpanded) {
            newExpanded.add(normalizedId);
        }
        this.expandedItems.set(newExpanded);
    }

    isExpanded(menuItemId: string | number): boolean {
        return this.expandedItems().has(String(menuItemId));
    }

    /**
     * Check if any child route of a parent menu item matches the current URL.
     * Used to highlight the parent pill when the user is on a child page.
     */
    isParentActive(item: NavItemDto): boolean {
        if (!item.children?.length) return false;
        const { path, params: urlParams } = this.currentUrl();
        const segments = path.split('/').filter(Boolean);

        return item.children.some(child => {
            if (!child.routerLink) return false;
            const [rawLinkPath, linkQuery] = child.routerLink.split('?');
            const linkPath = rawLinkPath.replace(/^\/+/, '').toLowerCase();
            if (!linkPath) return false;

            // Match against URL segments to avoid substring collisions
            // e.g. "rosters/public" should not match "rosters/club"
            const linkSegments = linkPath.split('/').filter(Boolean);
            if (linkSegments.length === 0 || linkSegments.length > segments.length) return false;
            const tail = segments.slice(segments.length - linkSegments.length);
            if (!tail.every((seg, i) => seg === linkSegments[i])) return false;

            // If the child link has query params, every param must match the current URL.
            // Disambiguates Type-2 reports (e.g. reporting/export-sp?spName=A vs ?spName=B):
            // without this, every L1 with such children lights up on any export-sp URL.
            if (linkQuery) {
                const linkParams = new URLSearchParams(linkQuery);
                for (const [k, v] of linkParams) {
                    if ((urlParams.get(k.toLowerCase()) ?? '') !== v.toLowerCase()) return false;
                }
            }
            return true;
        });
    }

    /**
     * Check if item has children
     */
    hasChildren(item: NavItemDto): boolean {
        return !!(item.children && item.children.length > 0);
    }

    /**
     * Legacy controller/action → Angular route translations.
     * Preserved for future use when reporting/scheduling nav items
     * may reference legacy controller/action patterns.
     */
    private readonly legacyRouteMap = new Map<string, string>([
        ['scheduling/manageleagueseasonfields', 'scheduling/fields'],
        ['scheduling/manageleagueseasonpairings', 'scheduling/pairings'],
        ['scheduling/manageleagueseasontimeslots', 'scheduling/timeslots'],
        ['scheduling/scheduledivbyagfields', 'scheduling/schedule-hub'],
        ['scheduling/getschedule', 'scheduling/view-schedule'],
    ]);

    /**
     * Get the path portion of a nav item's link (strips query string if present).
     */
    getLink(item: NavItemDto): string | null {
        if (item.navigateUrl) return item.navigateUrl;
        if (item.routerLink) return item.routerLink.split('?')[0];
        return null;
    }

    /**
     * Parse query params from a routerLink string (e.g. "reporting/export-sp?spName=foo&bUseJobId=true").
     * Returns null if no query string present.
     */
    getQueryParams(item: NavItemDto): Record<string, string> | null {
        if (!item.routerLink || !item.routerLink.includes('?')) return null;
        const qs = item.routerLink.split('?')[1];
        if (!qs) return null;
        const params: Record<string, string> = {};
        for (const pair of qs.split('&')) {
            const [key, value] = pair.split('=');
            if (key) params[decodeURIComponent(key)] = decodeURIComponent(value ?? '');
        }
        return params;
    }

    /**
     * Check if a nav item's route is implemented in the Angular router.
     * Items with navigateUrl (external) are always considered implemented.
     * Items with routerLink are checked against the router config.
     * Items with no link (parent headers) are considered implemented.
     */
    isRouteImplemented(item: NavItemDto): boolean {
        if (item.navigateUrl) return true;
        if (item.routerLink) {
            const link = item.routerLink.split('?')[0].replace(/^\/+/, '').toLowerCase();
            if (this.knownRoutes.has(link)) return true;
            return this.wildcardPrefixes.some(prefix => link.startsWith(prefix + '/'));
        }
        // Parent headers with no link are always shown
        return true;
    }

    /**
     * Check if the link is external (navigateUrl present)
     */
    isExternalLink(item: NavItemDto): boolean {
        return !!item.navigateUrl;
    }

    /**
     * Get icon for nav item — uses explicit iconName from seed data,
     * falls back to folder/dot for items without icons.
     */
    getMenuIcon(item: NavItemDto): string {
        if (item.iconName) return item.iconName;
        return this.hasChildren(item) ? 'folder' : 'dot';
    }

    /**
     * Walks the Angular router config and collects all child paths under :jobPath.
     * Returns a Set of lowercase paths (e.g., 'jobadministrator/admin', 'home', 'login').
     */
    private buildKnownRoutes(): Set<string> {
        const paths = new Set<string>();
        const jobPathRoute = this.router.config.find(r => r.path === ':jobPath');
        if (jobPathRoute?.children) {
            this.collectPaths(jobPathRoute.children, '', paths);
        }
        return paths;
    }

    private collectPaths(routes: Route[], prefix: string, paths: Set<string>): void {
        for (const route of routes) {
            if (!route.path && route.path !== '') continue;
            const fullPath = prefix ? `${prefix}/${route.path}` : route.path;
            if (fullPath) {
                paths.add(fullPath.toLowerCase());
            }
            if (route.children) {
                this.collectPaths(route.children, fullPath, paths);
            }
        }
    }

    /**
     * Collects route prefixes that have parameterized segments (e.g., 'reporting' from 'reporting/:action').
     * Used for wildcard matching — any menu item path starting with these prefixes is considered implemented.
     */
    private buildWildcardPrefixes(): string[] {
        const prefixes: string[] = [];
        const jobPathRoute = this.router.config.find(r => r.path === ':jobPath');
        if (jobPathRoute?.children) {
            for (const route of jobPathRoute.children) {
                if (route.path && route.path.includes(':')) {
                    // Extract the static prefix before the first parameterized segment
                    const segments = route.path.split('/');
                    const staticSegments: string[] = [];
                    for (const seg of segments) {
                        if (seg.startsWith(':')) break;
                        staticSegments.push(seg);
                    }
                    if (staticSegments.length > 0) {
                        prefixes.push(staticSegments.join('/').toLowerCase());
                    }
                }
            }
        }
        return prefixes;
    }
}
