/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AlignedTeamDto } from './AlignedTeamDto';
import type { RankingEntryDto } from './RankingEntryDto';
import type { RankingsTeamDto } from './RankingsTeamDto';
export type AlignmentResultDto = {
    success: boolean;
    errorMessage?: string | null;
    ageGroup: string;
    lastUpdated: string;
    alignedTeams: Array<AlignedTeamDto>;
    unmatchedRankings: Array<RankingEntryDto>;
    unmatchedTeams: Array<RankingsTeamDto>;
    totalMatches: number;
    totalTeamsInAgeGroup: number;
    matchPercentage: number;
};

