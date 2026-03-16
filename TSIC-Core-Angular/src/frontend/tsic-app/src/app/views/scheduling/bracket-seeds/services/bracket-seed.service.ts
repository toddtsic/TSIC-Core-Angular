import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { BracketSeedGameDto, BracketSeedDivisionOptionDto, UpdateBracketSeedRequest } from '@core/api';

@Injectable({ providedIn: 'root' })
export class BracketSeedService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = `${environment.apiUrl}/bracket-seeds`;

	getBracketGames(): Observable<BracketSeedGameDto[]> {
		return this.http.get<BracketSeedGameDto[]>(this.apiUrl);
	}

	updateSeed(request: UpdateBracketSeedRequest): Observable<BracketSeedGameDto> {
		return this.http.put<BracketSeedGameDto>(this.apiUrl, request);
	}

	getDivisionsForGame(gid: number): Observable<BracketSeedDivisionOptionDto[]> {
		return this.http.get<BracketSeedDivisionOptionDto[]>(
			`${this.apiUrl}/divisions/${gid}`);
	}
}
