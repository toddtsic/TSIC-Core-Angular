/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { DirectorClubLibraryTeamDto } from './DirectorClubLibraryTeamDto';
export type DirectorClubLibraryDto = {
    registrationId: string;
    clubId: number;
    clubName: string;
    oldestOfferedGradYear?: number | null;
    teams: Array<DirectorClubLibraryTeamDto>;
};

