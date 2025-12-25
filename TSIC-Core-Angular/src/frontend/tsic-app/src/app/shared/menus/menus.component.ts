import { Component, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive } from '@angular/router';
import type { MenuItemDto } from '../../core/services/job.service';

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
