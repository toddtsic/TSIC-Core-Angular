import { InjectionToken } from '@angular/core';
import type { Observable } from 'rxjs';
import type { ClubTeamsResponse } from '@core/api';

/**
 * Where a club menu gets its teams. Provided by the PAGE that hosts the schedule (it owns
 * the service and the jobPath) and injected by every <app-club-menu> beneath it — games
 * grid, team panel heading, team panel opponents — so none of them threads an input.
 * Not provided → the club renders as plain text, no caret.
 */
export interface ClubTeamsSource {
    loadClubTeams(teamId: string): Observable<ClubTeamsResponse>;
}

export const CLUB_TEAMS_SOURCE = new InjectionToken<ClubTeamsSource>('CLUB_TEAMS_SOURCE');
