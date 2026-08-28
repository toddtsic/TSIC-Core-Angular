/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { StoreAbandonedCartDto } from './StoreAbandonedCartDto';
import type { StoreCampaignKind } from './StoreCampaignKind';
import type { StoreCampaignTokenDto } from './StoreCampaignTokenDto';
export type StoreCampaignSetupDto = {
    kind: StoreCampaignKind;
    recipientCount: number;
    defaultSubject: string;
    defaultBody: string;
    tokens: Array<StoreCampaignTokenDto>;
    abandonedCarts: Array<StoreAbandonedCartDto>;
    minAgeHours: number;
    maxAgeHours: number;
    minAgeHourOptions: Array<number>;
    maxAgeHourOptions: Array<number>;
};

