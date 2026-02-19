/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { BillingTypeRefDto } from './BillingTypeRefDto';
import type { ChargeTypeRefDto } from './ChargeTypeRefDto';
import type { CustomerRefDto } from './CustomerRefDto';
import type { JobTypeRefDto } from './JobTypeRefDto';
import type { SportRefDto } from './SportRefDto';
export type JobConfigReferenceDataDto = {
    jobTypes: Array<JobTypeRefDto>;
    sports: Array<SportRefDto>;
    customers: Array<CustomerRefDto>;
    billingTypes: Array<BillingTypeRefDto>;
    chargeTypes: Array<ChargeTypeRefDto>;
};

