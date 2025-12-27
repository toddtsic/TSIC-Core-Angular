/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PlayerRegPolicyDto } from './PlayerRegPolicyDto';
export type PlayerRegInsuranceStatusDto = {
    offered: boolean;
    selected: boolean;
    declined: boolean;
    purchaseSucceeded: boolean;
    policies: Array<PlayerRegPolicyDto>;
};

