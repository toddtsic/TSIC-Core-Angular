/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AgegroupBuildEntry } from './AgegroupBuildEntry';
export type AutoBuildRequest = {
    sourceJobId?: string | null;
    agegroupOrder: Array<AgegroupBuildEntry>;
    divisionOrderStrategy: string;
    excludedDivisionIds: Array<string>;
    divisionStrategies?: any[] | null;
    saveProfiles?: boolean;
    existingGameMode?: string | null;
    gameGuarantee?: number;
};

