import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
	AgeGroupOptionDto,
	ScrapeResultDto,
	AlignmentResultDto,
	ImportRankingsRequest,
	ImportRankingsResultDto,
	RankingsTeamDto,
	UpdateTeamRankingRequest
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class UsLaxRankingsService {
	private readonly http = inject(HttpClient);
	private readonly base = `${environment.apiUrl}/uslax-rankings`;

	/** Get available age groups from usclublax.com */
	getScrapedAgeGroups(): Observable<AgeGroupOptionDto[]> {
		return this.http.get<AgeGroupOptionDto[]>(`${this.base}/age-groups`);
	}

	/** Get registered age groups from the current job */
	getRegisteredAgeGroups(): Observable<AgeGroupOptionDto[]> {
		return this.http.get<AgeGroupOptionDto[]>(`${this.base}/registered-age-groups`);
	}

	/** Get teams with saved ranking data for an age group */
	getSavedRankings(agegroupId: string): Observable<RankingsTeamDto[]> {
		return this.http.get<RankingsTeamDto[]>(`${this.base}/saved-rankings/${agegroupId}`);
	}

	/** Scrape rankings for specific parameters */
	scrapeRankings(v: string, alpha: string, yr: string): Observable<ScrapeResultDto> {
		const params = new HttpParams().set('v', v).set('alpha', alpha).set('yr', yr);
		return this.http.get<ScrapeResultDto>(`${this.base}/scrape`, { params });
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

	/** Bulk-import ranking data into NationalRankingData */
	importRankings(request: ImportRankingsRequest): Observable<ImportRankingsResultDto> {
		return this.http.post<ImportRankingsResultDto>(`${this.base}/import-rankings`, request);
	}

	/** Update a single team's national ranking data (JSON string) */
	updateTeamRanking(teamId: string, rankingData: string): Observable<unknown> {
		const body: UpdateTeamRankingRequest = { rankingData };
		return this.http.put(`${this.base}/team-ranking/${teamId}`, body);
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
