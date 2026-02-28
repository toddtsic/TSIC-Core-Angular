/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { DivisionSizeProfile } from './DivisionSizeProfile';
export type ProfileExtractionResponse = {
    sourceJobId: string;
    sourceJobName: string;
    sourceYear: string;
    profiles: Array<DivisionSizeProfile>;
    disconnects?: any[] | null;
};

