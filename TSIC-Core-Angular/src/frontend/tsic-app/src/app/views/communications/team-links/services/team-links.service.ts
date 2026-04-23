import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import type {
  AdminTeamLinkDto,
  CreateTeamLinkRequest,
  UpdateTeamLinkRequest,
  TeamLinkTeamOptionDto
} from '../../../../core/api';

@Injectable({ providedIn: 'root' })
export class TeamLinksService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/team-links`;

  list(): Observable<AdminTeamLinkDto[]> {
    return this.http.get<AdminTeamLinkDto[]>(this.apiUrl);
  }

  availableTeams(): Observable<TeamLinkTeamOptionDto[]> {
    return this.http.get<TeamLinkTeamOptionDto[]>(`${this.apiUrl}/available-teams`);
  }

  create(request: CreateTeamLinkRequest): Observable<void> {
    return this.http.post<void>(this.apiUrl, request);
  }

  update(docId: string, request: UpdateTeamLinkRequest): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/${docId}`, request);
  }

  delete(docId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${docId}`);
  }
}
