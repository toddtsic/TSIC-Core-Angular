/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type UpdateLeagueRequest = {
    leagueName: string;
    sportId: string;
    bAllowCoachScoreEntry: boolean;
    bHideContacts: boolean;
    bHideStandings: boolean;
    bShowScheduleToTeamMembers: boolean;
    bTakeAttendance: boolean;
    bTrackPenaltyMinutes: boolean;
    bTrackSportsmanshipScores: boolean;
    rescheduleEmailsToAddon?: string | null;
    playerFeeOverride?: number;
    standingsSortProfileId?: number;
    pointsMethod?: number;
    strLop?: string | null;
    strGradYears?: string | null;
};

