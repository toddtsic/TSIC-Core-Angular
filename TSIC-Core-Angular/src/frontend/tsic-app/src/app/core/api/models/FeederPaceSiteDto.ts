/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { FeederPaceSeasonDto } from './FeederPaceSeasonDto';
export type FeederPaceSiteDto = {
    site: string;
    seasons: Array<FeederPaceSeasonDto>;
    hasPriorSeason: boolean;
    isRetired: boolean;
    jobNames: Array<string>;
};

