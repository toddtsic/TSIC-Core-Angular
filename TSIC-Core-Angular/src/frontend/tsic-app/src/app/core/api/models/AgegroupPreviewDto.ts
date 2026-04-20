/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { DateShiftDto } from './DateShiftDto';
export type AgegroupPreviewDto = {
    sourceAgegroupId: string;
    sourceName?: string | null;
    newName?: string | null;
    sourceGradYearMin?: number | null;
    newGradYearMin?: number | null;
    sourceGradYearMax?: number | null;
    newGradYearMax?: number | null;
    dobMin?: (null | DateShiftDto);
    dobMax?: (null | DateShiftDto);
    discountFeeStart?: (null | DateShiftDto);
    discountFeeEnd?: (null | DateShiftDto);
    lateFeeStart?: (null | DateShiftDto);
    lateFeeEnd?: (null | DateShiftDto);
};

