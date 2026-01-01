import { Component, computed, inject, signal } from '@angular/core';

import { RouterLink, RouterLinkActive } from '@angular/router';
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

    // Access menu data from JobService
    menus = computed(() => this.jobService.menus());
    menusLoading = computed(() => this.jobService.menusLoading());
    menusError = computed(() => this.jobService.menusError());

    // Offcanvas state from shared service
    offcanvasOpen = this.menuState.offcanvasOpen;

    // Track expanded items for desktop dropdown and mobile accordion
    expandedItems = signal<Set<string>>(new Set());

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
     * Check if item has children
     */
    hasChildren(item: MenuItemDto): boolean {
        return !!(item.children && item.children.length > 0);
    }

    /**
     * Get the link for a menu item based on precedence:
     * 1. navigateUrl (external link)
     * 2. routerLink (Angular route)
     * 3. controller/action (legacy MVC - map to Angular route)
     */
    getLink(item: MenuItemDto): string | null {
        if (item.navigateUrl) {
            return item.navigateUrl;
        }
        if (item.routerLink) {
            return item.routerLink;
        }
        if (item.controller && item.action) {
            // Legacy MVC route mapping (1:1 mapping)
            return `/${item.controller.toLowerCase()}/${item.action.toLowerCase()}`;
        }
        return null;
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
}
