/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { EntityTagHeaderValue } from './EntityTagHeaderValue';
export type FileContentResult = {
    fileContents?: string;
    contentType?: string | null;
    fileDownloadName?: string | null;
    lastModified?: string | null;
    entityTag?: (null | EntityTagHeaderValue);
    enableRangeProcessing?: boolean;
};

