import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
	AgeGroupOptionDto,
	AlignmentResultDto,
	ReassessTeamRequest,
	ReassessTeamResultDto,
	SaveRankingsRequest,
	SaveRankingsResultDto,
	RankingsTeamDto,
	RankingSeasonDto
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class UsLaxRankingsService {
	private readonly http = inject(HttpClient);
	private readonly base = `${environment.apiUrl}/uslax-rankings`;

	/** Seasons published by usclublax.com, newest first, current one flagged */
	getSeasons(): Observable<RankingSeasonDto[]> {
		return this.http.get<RankingSeasonDto[]>(`${this.base}/seasons`);
	}

	/** Age groups from usclublax.com. Omit yr for the season the site currently serves. */
	getScrapedAgeGroups(yr?: string): Observable<AgeGroupOptionDto[]> {
		const params = yr ? new HttpParams().set('yr', yr) : undefined;
		return this.http.get<AgeGroupOptionDto[]>(`${this.base}/age-groups`, { params });
	}

	/** Get registered age groups from the current job */
	getRegisteredAgeGroups(): Observable<AgeGroupOptionDto[]> {
		return this.http.get<AgeGroupOptionDto[]>(`${this.base}/registered-age-groups`);
	}

	/**
	 * Every team in an age group with whatever ranking stamp it holds — the screen's starting
	 * point, loaded before any scrape so the roster is visible and renameable on its own.
	 */
	getAgeGroupTeams(agegroupId: string): Observable<RankingsTeamDto[]> {
		return this.http.get<RankingsTeamDto[]>(`${this.base}/age-group-teams/${agegroupId}`);
	}

	/** Scrape + align rankings with registered teams */
	alignRankings(
		v: string, alpha: string, yr: string,
		agegroupId: string,
		clubWeight = 75, teamWeight = 25
	): Observable<AlignmentResultDto> {
		const params = new HttpParams()
			.set('v', v).set('alpha', alpha).set('yr', yr)
			.set('agegroupId', agegroupId)
			.set('clubWeight', clubWeight.toString())
			.set('teamWeight', teamWeight.toString());
		return this.http.get<AlignmentResultDto>(`${this.base}/align`, { params });
	}

	/**
	 * Save the reviewed match set. Sends the decisions, not the parameters to re-derive them —
	 * the server writes exactly what it is handed and never re-scrapes usclublax.com.
	 */
	saveRankings(request: SaveRankingsRequest): Observable<SaveRankingsResultDto> {
		return this.http.post<SaveRankingsResultDto>(`${this.base}/save-rankings`, request);
	}

	/**
	 * Re-check ONE team against the rankings still unpaired on screen — used after a rename,
	 * where the old name was usually what hid the pairing. Scores nothing else and writes nothing.
	 */
	reassessTeam(request: ReassessTeamRequest): Observable<ReassessTeamResultDto> {
		return this.http.post<ReassessTeamResultDto>(`${this.base}/reassess-team`, request);
	}

	/** Clear all national ranking data for an age group */
	clearTeamRankings(agegroupId: string): Observable<unknown> {
		return this.http.delete(`${this.base}/team-rankings/${agegroupId}`);
	}

	/** Export aligned rankings as CSV */
	exportCsv(
		v: string, alpha: string, yr: string,
		agegroupId: string,
		clubWeight = 75, teamWeight = 25
	): Observable<Blob> {
		const params = new HttpParams()
			.set('v', v).set('alpha', alpha).set('yr', yr)
			.set('agegroupId', agegroupId)
			.set('clubWeight', clubWeight.toString())
			.set('teamWeight', teamWeight.toString());
		return this.http.get(`${this.base}/export-csv`, {
			params,
			responseType: 'blob'
		});
	}
}
