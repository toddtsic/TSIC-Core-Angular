import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';

import { Route, Router, RouterLink, RouterLinkActive } from '@angular/router';
import type { NavItemDto } from '@core/api';
import { JobService } from '@infrastructure/services/job.service';
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

    // Offcanvas state from shared service
    offcanvasOpen = this.menuState.offcanvasOpen;

    // Track which item's dropdown panel is open
    expandedItems = signal<Set<string>>(new Set());

    // Fixed-position coordinates for the open desktop dropdown panel
    dropdownPanelTop = signal(0);
    dropdownPanelLeft = signal(0);

    /** Close all dropdown panels */
    collapseAll(): void {
        this.expandedItems.set(new Set());
    }

    /** Close offcanvas (mobile sidebar) */
    closeOffcanvas(): void {
        this.menuState.closeOffcanvas();
    }

    /**
     * Open a dropdown panel positioned below the trigger button.
     * Uses getBoundingClientRect so the panel escapes any stacking context.
     */
    toggleDropdownAtEvent(event: MouseEvent, menuItemId: string | number): void {
        event.preventDefault();
        const normalizedId = String(menuItemId);
        const expanded = this.expandedItems();

        if (expanded.has(normalizedId)) {
            // Same trigger clicked again — close
            this.expandedItems.set(new Set());
            return;
        }

        // Compute panel position from trigger button's screen rect
        const btn = event.currentTarget as HTMLElement;
        const rect = btn.getBoundingClientRect();
        const PANEL_MIN_WIDTH = 260;
        const left = Math.min(rect.left, window.innerWidth - PANEL_MIN_WIDTH - 8);
        this.dropdownPanelLeft.set(Math.max(8, left));
        this.dropdownPanelTop.set(rect.bottom + 4);

        // Open this panel, close all others
        this.expandedItems.set(new Set([normalizedId]));
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
