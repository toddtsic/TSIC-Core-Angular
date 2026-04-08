import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { PublicRosterTreeDto, PublicRosterPlayerDto } from '@core/api';

@Injectable({ providedIn: 'root' })
export class PublicRosterService {
	private readonly http = inject(HttpClient);
	private readonly base = `${environment.apiUrl}/public-rosters`;

	getTree(jobPath: string): Observable<PublicRosterTreeDto> {
		return this.http.get<PublicRosterTreeDto>(`${this.base}/tree`, {
			params: { jobPath }
		});
	}

	getTeamRoster(teamId: string, jobPath: string): Observable<PublicRosterPlayerDto[]> {
		return this.http.get<PublicRosterPlayerDto[]>(`${this.base}/team/${teamId}`, {
			params: { jobPath }
		});
	}
}
