/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { FeederPaceSeasonDto } from './FeederPaceSeasonDto';
import type { FeederPaceSiteDto } from './FeederPaceSiteDto';
export type FeederPaceDto = {
    currentSeason: number;
    asOfDate: string;
    seasons: Array<FeederPaceSeasonDto>;
    sites: Array<FeederPaceSiteDto>;
    ungroupedJobNames: Array<string>;
};

