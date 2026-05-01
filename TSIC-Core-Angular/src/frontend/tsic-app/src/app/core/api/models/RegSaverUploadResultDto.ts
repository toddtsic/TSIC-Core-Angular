/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { RegSaverUploadRowError } from './RegSaverUploadRowError';
export type RegSaverUploadResultDto = {
    totalRows: number;
    importedCount: number;
    duplicateCount: number;
    errorCount: number;
    errors: Array<RegSaverUploadRowError>;
};

