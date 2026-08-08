/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ThirdPartyJobRowDto } from './ThirdPartyJobRowDto';
import type { ThirdPartyVendorDto } from './ThirdPartyVendorDto';
export type ThirdPartyAccessOverviewDto = {
    customerName: string;
    vendors: Array<ThirdPartyVendorDto>;
    jobs: Array<ThirdPartyJobRowDto>;
};

