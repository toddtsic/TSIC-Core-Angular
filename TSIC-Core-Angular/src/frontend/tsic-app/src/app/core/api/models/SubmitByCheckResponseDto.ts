/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { SubmitByCheckRejectionDto } from './SubmitByCheckRejectionDto';
export type SubmitByCheckResponseDto = {
    success: boolean;
    message: string;
    updatedRegistrationIds: Array<string>;
    rejections: Array<SubmitByCheckRejectionDto>;
};

