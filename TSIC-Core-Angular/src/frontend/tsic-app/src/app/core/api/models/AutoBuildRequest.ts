/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AgegroupBuildEntry } from './AgegroupBuildEntry';
export type AutoBuildRequest = {
    sourceJobId?: string | null;
    agegroupOrder: Array<AgegroupBuildEntry>;
    divisionOrderStrategy?: string | null;
    excludedDivisionIds: Array<string>;
    divisionStrategies?: any[] | null;
    saveProfiles?: boolean;
    existingGameMode?: string | null;
    gameGuarantee?: number;
    divisionWaves?: any | null;
    divisionOrder?: any[] | null;
    selectedFieldIds?: any[] | null;
    overrideStartTime?: string | null;
    overrideBrr?: number;
    overridePlacement?: string | null;
};

