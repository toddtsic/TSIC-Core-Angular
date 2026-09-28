/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PaidRegistrationsDayRowDto } from './PaidRegistrationsDayRowDto';
export type PaidRegistrationsByDayDto = {
    windowDays: number;
    since: string;
    days: Array<string>;
    jobCount: number;
    rows: Array<PaidRegistrationsDayRowDto>;
};

