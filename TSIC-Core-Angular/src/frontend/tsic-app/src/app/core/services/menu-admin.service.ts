import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import type {
    MenuAdminDto,
    MenuItemAdminDto,
    CreateMenuItemRequest,
    UpdateMenuItemRequest,
    UpdateMenuActiveRequest,
    ReorderMenuItemsRequest
} from '@core/api';

@Injectable({
    providedIn: 'root'
})
export class MenuAdminService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/menu-admin`;

    // Signal-based state for current menus
    public readonly menus = signal<MenuAdminDto[]>([]);
    public readonly isLoading = signal(false);

    /**
     * Loads all role menus for the current user's job.
     * Includes inactive items for admin visibility.
     */
    loadMenus(): Observable<MenuAdminDto[]> {
        this.isLoading.set(true);
        return this.http.get<MenuAdminDto[]>(`${this.apiUrl}/menus`).pipe(
            tap({
                next: (data) => {
                    this.menus.set(data);
                    this.isLoading.set(false);
                },
                error: () => this.isLoading.set(false)
            })
        );
    }

    /**
     * Toggles the Active state of a menu (Level 0).
     */
    toggleMenuActive(menuId: string, active: boolean): Observable<void> {
        const request: UpdateMenuActiveRequest = { active };
        return this.http.put<void>(`${this.apiUrl}/menus/${menuId}/active`, request).pipe(
            tap(() => {
                // Update local state
                const updated = this.menus().map(m =>
                    m.menuId === menuId ? { ...m, active } : m
                );
                this.menus.set(updated);
            })
        );
    }

    /**
     * Creates a new menu item.
     * Level 1 (parentMenuItemId=null): Creates parent + auto-creates stub child.
     * Level 2 (parentMenuItemId set): Creates child under parent.
     */
    createMenuItem(request: CreateMenuItemRequest): Observable<MenuItemAdminDto> {
        return this.http.post<MenuItemAdminDto>(`${this.apiUrl}/items`, request);
    }

    /**
     * Updates an existing menu item's properties.
     * MenuId and ParentMenuItemId cannot be changed.
     */
    updateMenuItem(menuItemId: string, request: UpdateMenuItemRequest): Observable<MenuItemAdminDto> {
        return this.http.put<MenuItemAdminDto>(`${this.apiUrl}/items/${menuItemId}`, request);
    }

    /**
     * Deletes a menu item.
     * Hard delete if siblings exist, soft delete (Active=false) if last sibling.
     */
    deleteMenuItem(menuItemId: string): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/items/${menuItemId}`);
    }

    /**
     * Reorders sibling menu items by assigning sequential Index values.
     */
    reorderItems(request: ReorderMenuItemsRequest): Observable<void> {
        return this.http.put<void>(`${this.apiUrl}/items/reorder`, request);
    }

    /**
     * Ensures all 6 standard roles have menus for the current user's job.
     * Creates missing role menus with stub parent/child items.
     */
    ensureAllRoleMenus(): Observable<{ created: number }> {
        return this.http.post<{ created: number }>(`${this.apiUrl}/menus/ensure-all-roles`, {}).pipe(
            tap(({ created }) => {
                if (created > 0) {
                    // Reload menus after creating new ones
                    this.loadMenus().subscribe();
                }
            })
        );
    }

    /**
     * Finds a menu by ID in the current state.
     */
    getMenuById(menuId: string): MenuAdminDto | undefined {
        return this.menus().find(m => m.menuId === menuId);
    }

    /**
     * Finds a menu item by ID within a specific menu.
     */
    getMenuItemById(menuId: string, menuItemId: string): MenuItemAdminDto | undefined {
        const menu = this.getMenuById(menuId);
        if (!menu) return undefined;

        // Recursively search through hierarchy
        const search = (items: MenuItemAdminDto[]): MenuItemAdminDto | undefined => {
            for (const item of items) {
                if (item.menuItemId === menuItemId) return item;
                if (item.children && item.children.length > 0) {
                    const found = search(item.children);
                    if (found) return found;
                }
            }
            return undefined;
        };

        return search(menu.items);
    }
}
