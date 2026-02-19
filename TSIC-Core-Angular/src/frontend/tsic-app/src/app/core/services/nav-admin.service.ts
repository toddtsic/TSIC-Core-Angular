import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import type {
    NavEditorNavDto,
    NavEditorNavItemDto,
    CreateNavItemRequest,
    UpdateNavItemRequest,
    ReorderNavItemsRequest,
    CreateNavRequest,
    ToggleNavActiveRequest
} from '@core/api';

@Injectable({
    providedIn: 'root'
})
export class NavAdminService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/nav/editor`;

    // Signal-based state
    public readonly navs = signal<NavEditorNavDto[]>([]);
    public readonly isLoading = signal(false);

    /**
     * Load all platform default navs with items.
     */
    loadNavs(): Observable<NavEditorNavDto[]> {
        this.isLoading.set(true);
        return this.http.get<NavEditorNavDto[]>(`${this.apiUrl}/defaults`).pipe(
            tap({
                next: (data) => {
                    this.navs.set(data);
                    this.isLoading.set(false);
                },
                error: () => this.isLoading.set(false)
            })
        );
    }

    /**
     * Toggle the Active state of a nav.
     */
    toggleNavActive(navId: number, active: boolean): Observable<void> {
        const request: ToggleNavActiveRequest = { active };
        return this.http.put<void>(`${this.apiUrl}/defaults/${navId}/active`, request).pipe(
            tap(() => {
                const updated = this.navs().map(n =>
                    n.navId === navId ? { ...n, active } : n
                );
                this.navs.set(updated);
            })
        );
    }

    /**
     * Create a new nav item.
     */
    createItem(request: CreateNavItemRequest): Observable<NavEditorNavItemDto> {
        return this.http.post<NavEditorNavItemDto>(`${this.apiUrl}/items`, request);
    }

    /**
     * Update an existing nav item.
     */
    updateItem(navItemId: number, request: UpdateNavItemRequest): Observable<NavEditorNavItemDto> {
        return this.http.put<NavEditorNavItemDto>(`${this.apiUrl}/items/${navItemId}`, request);
    }

    /**
     * Delete a nav item.
     */
    deleteItem(navItemId: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/items/${navItemId}`);
    }

    /**
     * Reorder sibling nav items.
     */
    reorderItems(request: ReorderNavItemsRequest): Observable<void> {
        return this.http.put<void>(`${this.apiUrl}/items/reorder`, request);
    }

    /**
     * Create a platform default nav for a role.
     */
    createNav(request: CreateNavRequest): Observable<NavEditorNavDto> {
        return this.http.post<NavEditorNavDto>(`${this.apiUrl}/defaults`, request);
    }

    /**
     * Ensure all standard roles have platform default navs.
     */
    ensureAllRoleNavs(): Observable<{ created: number }> {
        return this.http.post<{ created: number }>(`${this.apiUrl}/defaults/ensure-all-roles`, {}).pipe(
            tap(({ created }) => {
                if (created > 0) {
                    this.loadNavs().subscribe();
                }
            })
        );
    }

    /**
     * Export all platform default navs as an idempotent SQL script.
     */
    exportSql(): Observable<string> {
        return this.http.get<{ sql: string }>(`${this.apiUrl}/export-sql`).pipe(
            map(res => res.sql)
        );
    }
}
