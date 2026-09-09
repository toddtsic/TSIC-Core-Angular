/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { RankingEntryDto } from './RankingEntryDto';
export type ReassessTeamRequest = {
    teamId: string;
    registeredTeamAgeGroupId: string;
    candidateRankings: Array<RankingEntryDto>;
    clubWeight?: number;
    teamWeight?: number;
};

