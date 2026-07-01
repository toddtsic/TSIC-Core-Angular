/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AdultRoleType } from './AdultRoleType';
import type { AdultWaiverDto } from './AdultWaiverDto';
import type { JobRegFieldDto } from './JobRegFieldDto';
export type AdultRegFormResponse = {
    roleType: AdultRoleType;
    fields: Array<JobRegFieldDto>;
    waivers: Array<AdultWaiverDto>;
};

