import { Component, input, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive } from '@angular/router';
import type { MenuItemDto } from '@infrastructure/services/job.service';

@Component({
    selector: 'app-menus',
    standalone: true,
    imports: [CommonModule, RouterLink, RouterLinkActive],
    templateUrl: './menus.component.html',
    styleUrl: './menus.component.scss'
})
export class MenusComponent {
    menus = input<MenuItemDto[]>([]);
    loading = input<boolean>(false);
    error = input<string | null>(null);

    // Track which parent menu items are expanded
    expandedItems = signal<Set<string>>(new Set());

    /**
     * Toggle expansion state of a parent menu item
     */
    toggleExpanded(menuItemId: string): void {
        const expanded = this.expandedItems();
        const newExpanded = new Set(expanded);

        if (newExpanded.has(menuItemId)) {
            newExpanded.delete(menuItemId);
        } else {
            newExpanded.add(menuItemId);
        }

        this.expandedItems.set(newExpanded);
    }

    /**
     * Check if a menu item is expanded
     */
    isExpanded(menuItemId: string): boolean {
        return this.expandedItems().has(menuItemId);
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
            // Legacy MVC route mapping (1:1 mapping as specified)
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
     * Check if item has children
     */
    hasChildren(item: MenuItemDto): boolean {
        return item.children && item.children.length > 0;
    }
}
