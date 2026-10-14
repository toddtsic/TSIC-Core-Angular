/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ThirdPartyAssignmentDto } from './ThirdPartyAssignmentDto';
import type { ThirdPartyVendorDto } from './ThirdPartyVendorDto';
export type ThirdPartyJobRowDto = {
    jobId: string;
    customerId: string;
    jobName: string;
    expiryUsers: string;
    vendors: Array<ThirdPartyVendorDto>;
    assignment?: (null | ThirdPartyAssignmentDto);
};

