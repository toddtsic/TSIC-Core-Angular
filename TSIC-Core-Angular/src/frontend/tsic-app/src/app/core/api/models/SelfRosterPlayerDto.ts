/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { SelfRosterTeamOptionDto } from './SelfRosterTeamOptionDto';
export type SelfRosterPlayerDto = {
    registrationId: string;
    firstName: string;
    lastName: string;
    uniformNo?: string | null;
    position?: string | null;
    teamId: string;
    teamName: string;
    availableTeams: Array<SelfRosterTeamOptionDto>;
    availablePositions: Array<string>;
};

