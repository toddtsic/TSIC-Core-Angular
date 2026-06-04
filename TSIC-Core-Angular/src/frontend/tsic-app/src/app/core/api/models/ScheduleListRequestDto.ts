/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ScheduleListColumnDto } from './ScheduleListColumnDto';
export type ScheduleListRequestDto = {
    groupBy: string;
    sortBy: string;
    columns: Array<ScheduleListColumnDto>;
    scoreMode: string;
    pageBreakPerGroup: boolean;
    colorAccent: boolean;
};

