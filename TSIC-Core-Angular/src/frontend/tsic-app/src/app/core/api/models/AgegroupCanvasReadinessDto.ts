/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { GameDayDto } from './GameDayDto';
export type AgegroupCanvasReadinessDto = {
    agegroupId: string;
    dateCount: number;
    fieldCount: number;
    isConfigured: boolean;
    daysOfWeek: Array<string>;
    gamestartInterval?: number;
    startTime?: string | null;
    maxGamesPerField?: number;
    totalGameSlots: number;
    gameDays: Array<GameDayDto>;
    totalRounds: number;
    maxPairingRound: number;
    gameGuarantee?: number;
    fieldIds: Array<string>;
};

