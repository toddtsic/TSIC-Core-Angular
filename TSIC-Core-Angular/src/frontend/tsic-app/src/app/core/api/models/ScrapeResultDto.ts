/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { RankingEntryDto } from './RankingEntryDto';
export type ScrapeResultDto = {
    success: boolean;
    ageGroup: string;
    lastUpdated: string;
    errorMessage?: string | null;
    rankings: Array<RankingEntryDto>;
};

