/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { TeamResultDto } from './TeamResultDto';
export type TeamResultsResponse = {
    teamId: string;
    teamName: string;
    agegroupName: string;
    clubName?: string | null;
    teamRecord?: string | null;
    games: Array<TeamResultDto>;
};

