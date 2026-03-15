import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { MasterScheduleResponse } from '@core/api';

@Injectable({ providedIn: 'root' })
export class MasterScheduleService {
	private readonly http = inject(HttpClient);
	private readonly apiUrl = `${environment.apiUrl}/master-schedule`;

	getMasterSchedule(): Observable<MasterScheduleResponse> {
		return this.http.get<MasterScheduleResponse>(this.apiUrl);
	}

	exportExcel(includeReferees: boolean, dayIndex?: number): Observable<Blob> {
		return this.http.post(this.apiUrl + '/export',
			{ includeReferees, dayIndex: dayIndex ?? null },
			{ responseType: 'blob' });
	}
}
