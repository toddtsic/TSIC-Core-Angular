/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { GameShiftTarget } from './GameShiftTarget';
import type { ShiftConflict } from './ShiftConflict';
export type BatchShiftPreview = {
    moves: Array<GameShiftTarget>;
    conflicts: Array<ShiftConflict>;
    canApply: boolean;
    applied: boolean;
};

