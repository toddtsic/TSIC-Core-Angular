/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ClubTeamEventSummaryDto } from './ClubTeamEventSummaryDto';
export type ClubTeamLibraryEntryDto = {
    clubTeamId: number;
    clubTeamName: string;
    clubTeamGradYear: string;
    clubTeamLevelOfPlay: string | null;
    active: boolean;
    eventHistory: Array<ClubTeamEventSummaryDto>;
};

