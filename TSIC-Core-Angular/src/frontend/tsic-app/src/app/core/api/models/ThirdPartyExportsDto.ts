/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ThirdPartyExportLogEntryDto } from './ThirdPartyExportLogEntryDto';
import type { ThirdPartyExportRowDto } from './ThirdPartyExportRowDto';
export type ThirdPartyExportsDto = {
    windowDays: number;
    jobCount: number;
    rows: Array<ThirdPartyExportRowDto>;
    months: Array<string>;
    log: Array<ThirdPartyExportLogEntryDto>;
    totalExports: number;
    eventsExported: number;
    exporters: number;
    logTruncated: boolean;
};

