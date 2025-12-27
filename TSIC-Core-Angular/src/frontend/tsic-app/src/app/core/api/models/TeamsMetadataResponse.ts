/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AgeGroupDto } from './AgeGroupDto';
import type { ClubTeamDto } from './ClubTeamDto';
import type { RegisteredTeamDto } from './RegisteredTeamDto';
export type TeamsMetadataResponse = {
    clubId: number;
    clubName: string;
    availableClubTeams: Array<ClubTeamDto>;
    registeredTeams: Array<RegisteredTeamDto>;
    ageGroups: Array<AgeGroupDto>;
};

