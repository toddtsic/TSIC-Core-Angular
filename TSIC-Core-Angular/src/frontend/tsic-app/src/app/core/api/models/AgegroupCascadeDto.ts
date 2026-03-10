/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { DivisionCascadeDto } from './DivisionCascadeDto';
export type AgegroupCascadeDto = {
    agegroupId: string;
    agegroupName: string;
    gamePlacementOverride?: string | null;
    betweenRoundRowsOverride?: number;
    effectiveGamePlacement: string;
    effectiveBetweenRoundRows: number;
    wavesByDate: Record<string, number>;
    divisions: Array<DivisionCascadeDto>;
};

