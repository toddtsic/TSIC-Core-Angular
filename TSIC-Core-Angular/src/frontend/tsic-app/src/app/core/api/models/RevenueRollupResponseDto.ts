/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { JobAdminFeeDto } from './JobAdminFeeDto';
import type { JobMonthlyCountDto } from './JobMonthlyCountDto';
import type { JobRevenueRecordDto } from './JobRevenueRecordDto';
export type RevenueRollupResponseDto = {
    revenueRecords: Array<JobRevenueRecordDto>;
    monthlyCounts: Array<JobMonthlyCountDto>;
    adminFees: Array<JobAdminFeeDto>;
};

