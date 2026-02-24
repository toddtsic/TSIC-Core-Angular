/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { StoreSkuDto } from './StoreSkuDto';
export type StoreItemDto = {
    storeItemId: number;
    storeId: number;
    storeItemName: string;
    storeItemComments?: string | null;
    storeItemPrice: number;
    active: boolean;
    sortOrder: number;
    modified: string;
    skus: Array<StoreSkuDto>;
    imageUrls: Array<string>;
};

