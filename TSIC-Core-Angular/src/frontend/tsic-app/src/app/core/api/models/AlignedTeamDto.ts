/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { RankingEntryDto } from './RankingEntryDto';
import type { RankingsTeamDto } from './RankingsTeamDto';
export type AlignedTeamDto = {
    ranking: RankingEntryDto;
    registeredTeam: RankingsTeamDto;
    matchScore: number;
    matchReason: string;
};

