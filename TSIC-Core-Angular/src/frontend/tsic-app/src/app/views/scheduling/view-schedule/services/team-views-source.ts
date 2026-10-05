import { InjectionToken, type Signal } from '@angular/core';
import type { Observable } from 'rxjs';
import type { DivisionBracketResponse, PublicRosterPlayerDto, StandingsByDivisionResponse } from '@core/api';

/**
 * Where the team panel's Standings / Bracket / Roster views get their data. Provided by the
 * PAGE that hosts the panel (it owns the services and the jobPath), like CLUB_TEAMS_SOURCE.
 * Not provided → the panel shows the team's schedule alone.
 */
export interface TeamViewsSource {
    /** The team's own pool — the call that names its age group. */
    loadTeamStandings(teamId: string): Observable<StandingsByDivisionResponse>;
    /** Every pool in the age group: all of them decide who advances. */
    loadAgegroupStandings(agegroupId: string): Observable<StandingsByDivisionResponse>;
    /** The age group's brackets; by the team's division when the age group is unknown. */
    loadBrackets(teamId: string, agegroupId: string | null): Observable<DivisionBracketResponse[]>;
    loadTeamRoster(teamId: string): Observable<PublicRosterPlayerDto[]>;
    /** The event keeps rosters private (Jobs.bRestrictPublicRosters) → no Roster view, for anyone. */
    readonly rostersRestricted: Signal<boolean>;
}

export const TEAM_VIEWS_SOURCE = new InjectionToken<TeamViewsSource>('TEAM_VIEWS_SOURCE');
