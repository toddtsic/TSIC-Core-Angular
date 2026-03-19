import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, map, of, switchMap, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import type {
    NavEditorNavDto,
    NavEditorNavItemDto,
    NavVisibilityOptionsDto,
    CreateNavItemRequest,
    UpdateNavItemRequest,
    ReorderNavItemsRequest,
    CreateNavRequest,
    ToggleNavActiveRequest,
    ToggleHideRequest,
    CascadeRouteRequest,
    CloneBranchRequest
} from '@core/api';

/** Shape of the 409 body returned when a delete requires confirmation. */
export interface DeleteNavItemConflict {
    requiresConfirmation: boolean;
    message: string;
    affectedCount: number;
}

@Injectable({
    providedIn: 'root'
})
export class NavAdminService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/nav/editor`;

    // Signal-based state
    public readonly navs = signal<NavEditorNavDto[]>([]);
    public readonly jobOverrides = signal<NavEditorNavDto[]>([]);
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
     * Delete a nav item. Pass force=true to cascade-delete job override references.
     * On 409 Conflict, the error.error body is a DeleteNavItemConflict.
     */
    deleteItem(navItemId: number, force = false): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/items/${navItemId}`, {
            params: force ? { force: 'true' } : {}
        });
    }

    /**
     * Show or hide a platform default nav item for the current job.
     */
    toggleHide(request: ToggleHideRequest): Observable<void> {
        return this.http.post<void>(`${this.apiUrl}/items/toggle-hide`, request);
    }

    /**
     * Load all job override navs for the current job.
     */
    loadJobOverrides(): void {
        this.http.get<NavEditorNavDto[]>(`${this.apiUrl}/job-overrides`).pipe(
            tap({
                next: (data) => this.jobOverrides.set(data),
                error: () => this.jobOverrides.set([])
            })
        ).subscribe();
    }

    /**
     * Ensure a job override nav exists for the given role.
     * Returns the navId (creates one if needed).
     */
    ensureJobOverrideNav(roleId: string): Observable<number> {
        return this.http.post<{ navId: number }>(`${this.apiUrl}/job-overrides/ensure`, { roleId }).pipe(
            map(res => res.navId)
        );
    }

    /**
     * Reorder sibling nav items.
     */
    reorderItems(request: ReorderNavItemsRequest): Observable<void> {
        return this.http.put<void>(`${this.apiUrl}/items/reorder`, request);
    }

    /**
     * Cascade a route change to all matching nav items across platform default navs.
     * Returns the count of additional items updated.
     */
    cascadeRoute(request: CascadeRouteRequest): Observable<{ updated: number }> {
        return this.http.post<{ updated: number }>(`${this.apiUrl}/items/cascade-route`, request);
    }

    /**
     * Create a platform default nav for a role.
     */
    createNav(request: CreateNavRequest): Observable<NavEditorNavDto> {
        return this.http.post<NavEditorNavDto>(`${this.apiUrl}/defaults`, request);
    }

    /**
     * Ensure all standard roles have platform default navs, then load them.
     * Sets isLoading immediately so the spinner shows during both steps.
     */
    ensureAndLoad(): Observable<NavEditorNavDto[]> {
        this.isLoading.set(true);
        return this.http.post<{ created: number }>(`${this.apiUrl}/defaults/ensure-all-roles`, {}).pipe(
            catchError(() => of({ created: 0 })),
            switchMap(() => this.http.get<NavEditorNavDto[]>(`${this.apiUrl}/defaults`)),
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
     * Move a child nav item to a different parent group within the same nav.
     */
    moveItem(navItemId: number, targetParentNavItemId: number): Observable<void> {
        return this.http.put<void>(`${this.apiUrl}/items/${navItemId}/move`, { targetParentNavItemId });
    }

    /**
     * Clone a Level 1 nav item and its active children to another role's nav.
     * Returns the count of items cloned (parent + children).
     */
    cloneBranch(request: CloneBranchRequest): Observable<number> {
        return this.http.post<number>(`${this.apiUrl}/items/clone-branch`, request);
    }

    /**
     * Export all platform default navs as an idempotent SQL script.
     */
    exportSql(): Observable<string> {
        return this.http.get<{ sql: string }>(`${this.apiUrl}/export-sql`).pipe(
            map(res => res.sql)
        );
    }

    /**
     * Load distinct sports, job types, and customers for visibility rules editor.
     */
    loadVisibilityOptions(): Observable<NavVisibilityOptionsDto> {
        return this.http.get<NavVisibilityOptionsDto>(`${this.apiUrl}/visibility-options`);
    }
}
