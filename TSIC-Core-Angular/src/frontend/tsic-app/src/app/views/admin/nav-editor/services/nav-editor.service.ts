import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
	CreateNavItemRequest,
	CreateNavRequest,
	ImportLegacyMenuRequest,
	NavEditorLegacyMenuDto,
	NavEditorNavDto,
	NavEditorNavItemDto,
	ReorderNavItemsRequest,
	UpdateNavItemRequest
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class NavEditorService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = `${environment.apiUrl}/nav`;

	// ── Platform defaults ──

	getDefaults(): Observable<NavEditorNavDto[]> {
		return this.http.get<NavEditorNavDto[]>(`${this.apiUrl}/editor/defaults`);
	}

	createDefault(request: CreateNavRequest): Observable<NavEditorNavDto> {
		return this.http.post<NavEditorNavDto>(`${this.apiUrl}/editor/defaults`, request);
	}

	// ── Legacy menus ──

	getLegacyMenus(): Observable<NavEditorLegacyMenuDto[]> {
		return this.http.get<NavEditorLegacyMenuDto[]>(`${this.apiUrl}/editor/legacy-menus`);
	}

	importLegacy(request: ImportLegacyMenuRequest): Observable<NavEditorNavDto> {
		return this.http.post<NavEditorNavDto>(`${this.apiUrl}/editor/import-legacy`, request);
	}

	// ── Nav items ──

	createItem(request: CreateNavItemRequest): Observable<NavEditorNavItemDto> {
		return this.http.post<NavEditorNavItemDto>(`${this.apiUrl}/editor/items`, request);
	}

	updateItem(navItemId: number, request: UpdateNavItemRequest): Observable<NavEditorNavItemDto> {
		return this.http.put<NavEditorNavItemDto>(`${this.apiUrl}/editor/items/${navItemId}`, request);
	}

	deleteItem(navItemId: number): Observable<void> {
		return this.http.delete<void>(`${this.apiUrl}/editor/items/${navItemId}`);
	}

	reorderItems(request: ReorderNavItemsRequest): Observable<void> {
		return this.http.put<void>(`${this.apiUrl}/editor/items/reorder`, request);
	}
}
