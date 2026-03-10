/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type DivisionCascadeDto = {
    divisionId: string;
    divisionName: string;
    gamePlacementOverride?: string | null;
    betweenRoundRowsOverride?: number;
    effectiveGamePlacement: string;
    effectiveBetweenRoundRows: number;
    effectiveWavesByDate: Record<string, number>;
};

