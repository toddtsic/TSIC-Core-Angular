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
    gamestartInterval?: number | null;
    startTime?: string | null;
    maxGamesPerField?: number | null;
    totalGameSlots: number;
    gameDays: Array<GameDayDto>;
    totalRounds: number;
    maxPairingRound: number;
    gameGuarantee?: number | null;
    fieldIds: Array<string>;
};

