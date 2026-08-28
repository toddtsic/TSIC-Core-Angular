/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { StoreCartBatchDto } from './StoreCartBatchDto';
import type { StoreCartTrimAdjustmentDto } from './StoreCartTrimAdjustmentDto';
export type StoreCheckoutPrepareDto = {
    cart: StoreCartBatchDto;
    wasAutoUpdated: boolean;
    adjustments: Array<StoreCartTrimAdjustmentDto>;
};

