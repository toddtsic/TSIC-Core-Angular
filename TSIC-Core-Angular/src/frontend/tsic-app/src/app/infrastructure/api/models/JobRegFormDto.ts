/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { JobRegFieldDto } from './JobRegFieldDto';
export type JobRegFormDto = {
    version: string;
    coreProfileName?: string | null;
    fields: Array<JobRegFieldDto>;
    waiverFieldNames: Array<string>;
    constraintType?: string | null;
};

