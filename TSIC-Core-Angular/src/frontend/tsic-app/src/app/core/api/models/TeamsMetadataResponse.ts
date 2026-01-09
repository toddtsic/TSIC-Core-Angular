/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AgeGroupDto } from './AgeGroupDto';
import type { RegisteredTeamDto } from './RegisteredTeamDto';
import type { SuggestedTeamNameDto } from './SuggestedTeamNameDto';
export type TeamsMetadataResponse = {
    clubId: number;
    clubName: string;
    suggestedTeamNames: Array<SuggestedTeamNameDto>;
    registeredTeams: Array<RegisteredTeamDto>;
    ageGroups: Array<AgeGroupDto>;
};

