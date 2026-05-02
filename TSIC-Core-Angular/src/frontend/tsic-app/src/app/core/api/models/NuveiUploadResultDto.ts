/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { NuveiUploadRowError } from './NuveiUploadRowError';
export type NuveiUploadResultDto = {
    totalRows: number;
    importedCount: number;
    duplicateCount: number;
    errorCount: number;
    errors: Array<NuveiUploadRowError>;
};

