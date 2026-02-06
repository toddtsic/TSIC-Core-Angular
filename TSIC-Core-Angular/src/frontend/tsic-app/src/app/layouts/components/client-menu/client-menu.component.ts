import { Component, computed, inject, signal } from '@angular/core';

import { Route, Router, RouterLink, RouterLinkActive } from '@angular/router';
import type { MenuItemDto } from '@core/api';
import { JobService } from '@infrastructure/services/job.service';
import { MenuStateService } from '../../services/menu-state.service';

@Component({
    selector: 'app-client-menu',
    standalone: true,
    imports: [RouterLink, RouterLinkActive],
    templateUrl: './client-menu.component.html',
    styleUrl: './client-menu.component.scss'
})
export class ClientMenuComponent {
    private readonly jobService = inject(JobService);
    private readonly menuState = inject(MenuStateService);
    private readonly router = inject(Router);

    // Build a set of known child routes under :jobPath for route existence checks
    private readonly knownRoutes = this.buildKnownRoutes();

    // Access menu data from JobService
    menus = computed(() => this.jobService.menus());
    menusLoading = computed(() => this.jobService.menusLoading());
    menusError = computed(() => this.jobService.menusError());

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
    toggleExpanded(menuItemId: string): void {
        const normalizedId = menuItemId.toLowerCase();
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
    isExpanded(menuItemId: string): boolean {
        const normalizedId = menuItemId.toLowerCase();
        return this.expandedItems().has(normalizedId);
    }

    /**
     * Expand a menu item (desktop hover)
     */
    expandItem(menuItemId: string): void {
        const normalizedId = menuItemId.toLowerCase();
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
    collapseItem(menuItemId: string): void {
        const normalizedId = menuItemId.toLowerCase();
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
    hasChildren(item: MenuItemDto): boolean {
        return !!(item.children && item.children.length > 0);
    }

    /**
     * Get the link for a menu item based on precedence:
     * 1. navigateUrl (external link)
     * 2. routerLink (Angular route)
     * 3. controller/action (legacy MVC - map to Angular route as {jobPath}/{controller}/{action})
     */
    getLink(item: MenuItemDto): string | null {
        if (item.navigateUrl) {
            return item.navigateUrl;
        }

        const jobPath = this.jobService.currentJob()?.jobPath || '';

        if (item.routerLink) {
            return item.routerLink;
        }

        if (item.controller && item.action) {
            return `/${jobPath}/${item.controller.toLowerCase()}/${item.action.toLowerCase()}`;
        }

        return null;
    }

    /**
     * Check if a menu item's route is implemented in the Angular router.
     * Items with navigateUrl (external) or routerLink are always considered implemented.
     * Items with controller/action are checked against the router config.
     */
    isRouteImplemented(item: MenuItemDto): boolean {
        if (item.navigateUrl || item.routerLink) {
            return true;
        }
        if (item.controller && item.action) {
            const path = `${item.controller.toLowerCase()}/${item.action.toLowerCase()}`;
            return this.knownRoutes.has(path);
        }
        // Items with no link at all (parent headers) are considered implemented
        return true;
    }

    /**
     * Check if the link is external (navigateUrl present)
     */
    isExternalLink(item: MenuItemDto): boolean {
        return !!item.navigateUrl;
    }

    /**
     * Get icon for menu item with intelligent fallbacks
     */
    getMenuIcon(item: MenuItemDto): string {
        if (item.iconName) {
            return item.iconName;
        }

        // Smart fallbacks based on menu text content
        const text = item.text?.toLowerCase() || '';

        if (text.includes('profile') || text.includes('account')) return 'person-circle';
        if (text.includes('dashboard') || text.includes('home')) return 'house-door';
        if (text.includes('registration') || text.includes('register')) return 'person-plus';
        if (text.includes('schedule') || text.includes('calendar')) return 'calendar-event';
        if (text.includes('team') || text.includes('roster')) return 'people';
        if (text.includes('payment') || text.includes('billing')) return 'credit-card';
        if (text.includes('document') || text.includes('form')) return 'file-earmark-text';
        if (text.includes('setting') || text.includes('config')) return 'gear';
        if (text.includes('help') || text.includes('support')) return 'question-circle';
        if (text.includes('report') || text.includes('stats')) return 'bar-chart';
        if (text.includes('message') || text.includes('communication')) return 'chat-dots';

        // Default fallback
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
}
