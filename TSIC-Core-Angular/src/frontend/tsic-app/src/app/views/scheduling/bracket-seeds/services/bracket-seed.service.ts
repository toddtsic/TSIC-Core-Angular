import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { BracketSeedBoardDto, BracketSeedGameDto, BracketSeedDivisionOptionDto, UpdateBracketSeedRequest } from '@core/api';

@Injectable({ providedIn: 'root' })
export class BracketSeedService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = `${environment.apiUrl}/bracket-seeds`;

	getBracketGames(): Observable<BracketSeedBoardDto> {
		return this.http.get<BracketSeedBoardDto>(this.apiUrl);
	}

	updateSeed(request: UpdateBracketSeedRequest): Observable<BracketSeedGameDto> {
		return this.http.put<BracketSeedGameDto>(this.apiUrl, request);
	}

	getDivisionsForGame(gid: number): Observable<BracketSeedDivisionOptionDto[]> {
		return this.http.get<BracketSeedDivisionOptionDto[]>(
			`${this.apiUrl}/divisions/${gid}`);
	}

	/** Reseed mode: valid seed-rank ceiling for a pool = its active team count. */
	getRankCeiling(divId: string): Observable<number> {
		return this.http.get<number>(`${this.apiUrl}/rank-ceiling/${divId}`);
	}
}
