/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AutoBuildDivisionResult } from './AutoBuildDivisionResult';
import type { ConstraintSacrificeDto } from './ConstraintSacrificeDto';
import type { UnplacedGameDto } from './UnplacedGameDto';
export type AutoBuildV2Result = {
    totalDivisions: number;
    divisionsScheduled: number;
    divisionsSkipped: number;
    totalGamesPlaced: number;
    gamesFailedToPlace: number;
    divisionResults: Array<AutoBuildDivisionResult>;
    unplacedGames: Array<UnplacedGameDto>;
    sacrificeLog: Array<ConstraintSacrificeDto>;
};

