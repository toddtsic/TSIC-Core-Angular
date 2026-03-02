/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { BulkDateAgegroupEntry } from './BulkDateAgegroupEntry';
export type BulkDateAssignRequest = {
    gDate: string;
    startTime: string;
    gamestartInterval: number;
    maxGamesPerField: number;
    entries: Array<BulkDateAgegroupEntry>;
    agegroupIds?: any[] | null;
};

