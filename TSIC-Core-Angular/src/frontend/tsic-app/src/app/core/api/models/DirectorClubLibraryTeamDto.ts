/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ClubTeamEventHistoryDto } from './ClubTeamEventHistoryDto';
export type DirectorClubLibraryTeamDto = {
    clubTeamId: number;
    clubTeamName: string;
    clubTeamGradYear: string;
    clubTeamLevelOfPlay?: string | null;
    archived: boolean;
    eventStatus: string;
    eventTeamId?: string | null;
    eventTeamName?: string | null;
    eventAgeGroupName?: string | null;
    otherEvents: Array<ClubTeamEventHistoryDto>;
};

