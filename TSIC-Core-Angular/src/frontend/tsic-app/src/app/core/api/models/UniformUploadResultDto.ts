/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { UniformUploadRowError } from './UniformUploadRowError';
export type UniformUploadResultDto = {
    totalRows: number;
    updatedCount: number;
    skippedCount: number;
    errorCount: number;
    errors: Array<UniformUploadRowError>;
};

