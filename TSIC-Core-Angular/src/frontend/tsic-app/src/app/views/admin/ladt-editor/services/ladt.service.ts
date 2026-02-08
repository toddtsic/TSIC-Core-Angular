import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import type {
  LadtTreeRootDto,
  LeagueDetailDto,
  UpdateLeagueRequest,
  AgegroupDetailDto,
  CreateAgegroupRequest,
  UpdateAgegroupRequest,
  DivisionDetailDto,
  CreateDivisionRequest,
  UpdateDivisionRequest,
  TeamDetailDto,
  CreateTeamRequest,
  UpdateTeamRequest,
  DeleteTeamResultDto
} from '../../../../core/api';

@Injectable({
  providedIn: 'root'
})
export class LadtService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/ladt`;

  // ── Tree ──

  getTree(): Observable<LadtTreeRootDto> {
    return this.http.get<LadtTreeRootDto>(`${this.apiUrl}/tree`);
  }

  // ── League ──

  getLeague(leagueId: string): Observable<LeagueDetailDto> {
    return this.http.get<LeagueDetailDto>(`${this.apiUrl}/leagues/${leagueId}`);
  }

  updateLeague(leagueId: string, request: UpdateLeagueRequest): Observable<LeagueDetailDto> {
    return this.http.put<LeagueDetailDto>(`${this.apiUrl}/leagues/${leagueId}`, request);
  }

  // ── Agegroup ──

  getAgegroup(agegroupId: string): Observable<AgegroupDetailDto> {
    return this.http.get<AgegroupDetailDto>(`${this.apiUrl}/agegroups/${agegroupId}`);
  }

  createAgegroup(request: CreateAgegroupRequest): Observable<AgegroupDetailDto> {
    return this.http.post<AgegroupDetailDto>(`${this.apiUrl}/agegroups`, request);
  }

  updateAgegroup(agegroupId: string, request: UpdateAgegroupRequest): Observable<AgegroupDetailDto> {
    return this.http.put<AgegroupDetailDto>(`${this.apiUrl}/agegroups/${agegroupId}`, request);
  }

  deleteAgegroup(agegroupId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/agegroups/${agegroupId}`);
  }

  addStubAgegroup(leagueId: string): Observable<string> {
    return this.http.post<string>(`${this.apiUrl}/agegroups/stub/${leagueId}`, null);
  }

  // ── Division ──

  getDivision(divId: string): Observable<DivisionDetailDto> {
    return this.http.get<DivisionDetailDto>(`${this.apiUrl}/divisions/${divId}`);
  }

  createDivision(request: CreateDivisionRequest): Observable<DivisionDetailDto> {
    return this.http.post<DivisionDetailDto>(`${this.apiUrl}/divisions`, request);
  }

  updateDivision(divId: string, request: UpdateDivisionRequest): Observable<DivisionDetailDto> {
    return this.http.put<DivisionDetailDto>(`${this.apiUrl}/divisions/${divId}`, request);
  }

  deleteDivision(divId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/divisions/${divId}`);
  }

  addStubDivision(agegroupId: string): Observable<string> {
    return this.http.post<string>(`${this.apiUrl}/divisions/stub/${agegroupId}`, null);
  }

  // ── Team ──

  getTeam(teamId: string): Observable<TeamDetailDto> {
    return this.http.get<TeamDetailDto>(`${this.apiUrl}/teams/${teamId}`);
  }

  createTeam(request: CreateTeamRequest): Observable<TeamDetailDto> {
    return this.http.post<TeamDetailDto>(`${this.apiUrl}/teams`, request);
  }

  updateTeam(teamId: string, request: UpdateTeamRequest): Observable<TeamDetailDto> {
    return this.http.put<TeamDetailDto>(`${this.apiUrl}/teams/${teamId}`, request);
  }

  deleteTeam(teamId: string): Observable<DeleteTeamResultDto> {
    return this.http.delete<DeleteTeamResultDto>(`${this.apiUrl}/teams/${teamId}`);
  }

  cloneTeam(teamId: string): Observable<TeamDetailDto> {
    return this.http.post<TeamDetailDto>(`${this.apiUrl}/teams/${teamId}/clone`, null);
  }

  addStubTeam(divId: string): Observable<string> {
    return this.http.post<string>(`${this.apiUrl}/teams/stub/${divId}`, null);
  }

  // ── Batch ──

  addWaitlistAgegroups(): Observable<number> {
    return this.http.post<number>(`${this.apiUrl}/batch/waitlist-agegroups`, null);
  }

  updatePlayerFeesToAgegroupFees(agegroupId: string): Observable<number> {
    return this.http.post<number>(`${this.apiUrl}/batch/update-fees/${agegroupId}`, null);
  }
}
