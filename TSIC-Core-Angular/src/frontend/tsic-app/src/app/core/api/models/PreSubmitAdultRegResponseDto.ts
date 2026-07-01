/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AdultFeeBreakdownDto } from './AdultFeeBreakdownDto';
export type PreSubmitAdultRegResponseDto = {
    valid: boolean;
    validationErrors?: any[] | null;
    registrationId?: string | null;
    fees: AdultFeeBreakdownDto;
    jobUsesAmex: boolean;
};

