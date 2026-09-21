/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ThirdPartyExportMonthDto } from './ThirdPartyExportMonthDto';
export type ThirdPartyExportRowDto = {
    jobId: string;
    jobName: string;
    exports: number;
    lastExport: string;
    monthCounts: Array<ThirdPartyExportMonthDto>;
};

