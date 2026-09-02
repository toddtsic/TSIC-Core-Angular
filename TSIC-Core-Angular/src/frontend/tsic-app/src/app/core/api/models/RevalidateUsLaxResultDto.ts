/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { UsLaxCheckRowDto } from './UsLaxCheckRowDto';
export type RevalidateUsLaxResultDto = {
    found: boolean;
    memStatus?: string | null;
    expDate?: string | null;
    message?: string | null;
    eligible?: boolean | null;
    eligibilityReason?: string | null;
    eligibilityDetail?: string | null;
    checks?: Array<UsLaxCheckRowDto>;
};

