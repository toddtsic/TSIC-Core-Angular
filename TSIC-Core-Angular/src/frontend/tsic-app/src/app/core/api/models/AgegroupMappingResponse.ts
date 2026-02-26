/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AgegroupMappingProposal } from './AgegroupMappingProposal';
export type AgegroupMappingResponse = {
    sourceJobId: string;
    sourceJobName: string;
    sourceYear: string;
    sourceTotalGames: number;
    proposals: Array<AgegroupMappingProposal>;
    currentAgegroupNames: Array<string>;
    currentAgegroupColors: Record<string, string>;
};

