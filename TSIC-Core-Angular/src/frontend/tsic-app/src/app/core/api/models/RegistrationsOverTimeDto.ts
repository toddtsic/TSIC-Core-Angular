/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { RegistrationsBucketRowDto } from './RegistrationsBucketRowDto';
export type RegistrationsOverTimeDto = {
    bucket: string;
    since: string;
    buckets: Array<string>;
    jobCount: number;
    rows: Array<RegistrationsBucketRowDto>;
};

