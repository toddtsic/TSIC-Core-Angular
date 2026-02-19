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

    // Track expanded items for desktop dropdown and mobile accordion
    expandedItems = signal<Set<string>>(new Set());

    /**
     * Collapse all expanded dropdown menus
     */
    collapseAll(): void {
        this.expandedItems.set(new Set());
    }

    /**
     * Close offcanvas (when clicking backdrop or close button)
     */
    closeOffcanvas(): void {
        this.menuState.closeOffcanvas();
    }

    /**
     * Toggle expansion state of a parent menu item
     * Closes all other expanded items (single expansion at a time)
     */
    toggleExpanded(menuItemId: string | number): void {
        const normalizedId = String(menuItemId);
        const expanded = this.expandedItems();
        const isCurrentlyExpanded = expanded.has(normalizedId);

        // Close all items
        const newExpanded = new Set<string>();

        // If the item wasn't expanded, open it (otherwise leave all closed)
        if (!isCurrentlyExpanded) {
            newExpanded.add(normalizedId);
        }

        this.expandedItems.set(newExpanded);
    }

    /**
     * Check if a menu item is expanded
     */
    isExpanded(menuItemId: string | number): boolean {
        const normalizedId = String(menuItemId);
        return this.expandedItems().has(normalizedId);
    }

    /**
     * Expand a menu item (desktop hover)
     */
    expandItem(menuItemId: string | number): void {
        const normalizedId = String(menuItemId);
        const expanded = this.expandedItems();
        if (!expanded.has(normalizedId)) {
            // Close all others and open this one
            const newExpanded = new Set<string>();
            newExpanded.add(normalizedId);
            this.expandedItems.set(newExpanded);
        }
    }

    /**
     * Collapse a menu item (desktop hover out)
     */
    collapseItem(menuItemId: string | number): void {
        const normalizedId = String(menuItemId);
        const expanded = this.expandedItems();
        if (expanded.has(normalizedId)) {
            const newExpanded = new Set(expanded);
            newExpanded.delete(normalizedId);
            this.expandedItems.set(newExpanded);
        }
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
        ['scheduling/manageleagueseasonfields', 'fields/index'],
        ['scheduling/manageleagueseasonpairings', 'pairings/index'],
        ['scheduling/manageleagueseasontimeslots', 'timeslots/index'],
        ['scheduling/scheduledivbyagfields', 'scheduling/scheduledivision'],
        ['scheduling/getschedule', 'scheduling/schedules'],
    ]);

    /**
     * Get the link for a nav item.
     * Nav items have explicit routerLink or navigateUrl — no controller/action translation needed.
     */
    getLink(item: NavItemDto): string | null {
        if (item.navigateUrl) return item.navigateUrl;
        if (item.routerLink) return item.routerLink;
        return null;
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
            if (this.knownRoutes.has(item.routerLink)) return true;
            return this.wildcardPrefixes.some(prefix => item.routerLink!.startsWith(prefix + '/'));
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
