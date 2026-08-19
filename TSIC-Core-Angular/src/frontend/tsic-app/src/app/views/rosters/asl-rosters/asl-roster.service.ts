import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { AslRostersIndexDto, AslRegionTeamDto } from '@core/api';

@Injectable({ providedIn: 'root' })
export class AslRosterService {
	private readonly http = inject(HttpClient);
	private readonly base = `${environment.apiUrl}/asl-rosters`;

	getIndex(jobPath: string): Observable<AslRostersIndexDto> {
		return this.http.get<AslRostersIndexDto>(`${this.base}/index`, {
			params: { jobPath }
		});
	}

	getRegionRoster(region: string, jobPath: string): Observable<AslRegionTeamDto[]> {
		return this.http.get<AslRegionTeamDto[]>(`${this.base}/region`, {
			params: { jobPath, region }
		});
	}

	getTeamRoster(teamId: string, jobPath: string): Observable<AslRegionTeamDto> {
		return this.http.get<AslRegionTeamDto>(`${this.base}/team/${teamId}`, {
			params: { jobPath }
		});
	}
}
