/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { LadtFeeBelowSummaryDto } from './LadtFeeBelowSummaryDto';
import type { LadtFeeModifierResolutionDto } from './LadtFeeModifierResolutionDto';
export type LadtFeeRoleResolutionDto = {
    roleId: string;
    feeConfigured: boolean;
    deposit?: number | null;
    depositSource?: string | null;
    balanceDue?: number | null;
    balanceDueSource?: string | null;
    fullPayment: boolean;
    phaseSource?: string | null;
    twoPhase: boolean;
    earlyBird?: (null | LadtFeeModifierResolutionDto);
    lateFee?: (null | LadtFeeModifierResolutionDto);
    below?: (null | LadtFeeBelowSummaryDto);
};

