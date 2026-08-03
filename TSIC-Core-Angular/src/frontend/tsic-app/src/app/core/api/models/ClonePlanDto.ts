/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AgegroupPreviewDto } from './AgegroupPreviewDto';
import type { BulletinShiftDto } from './BulletinShiftDto';
import type { ClonedBrandingPreviewDto } from './ClonedBrandingPreviewDto';
import type { ClonePlanStepDto } from './ClonePlanStepDto';
import type { DateShiftDto } from './DateShiftDto';
import type { FeeModifierShiftDto } from './FeeModifierShiftDto';
import type { LeaguePlanDto } from './LeaguePlanDto';
export type ClonePlanDto = {
    steps: Array<ClonePlanStepDto>;
    planFingerprint: string;
    yearDelta: number;
    advanceFlagDefault: boolean;
    resolvedProcessingFeePercent: number;
    sourceProcessingFeePercent?: number | null;
    resolvedEcheckProcessingFeePercent: number;
    sourceEcheckProcessingFeePercent?: number | null;
    sourceBEnableEcheck: boolean;
    sourceBEnableStore: boolean;
    brandingPreview?: (null | ClonedBrandingPreviewDto);
    regFormFrom?: string | null;
    regFormCcs?: string | null;
    regFormBccs?: string | null;
    rescheduleemaillist?: string | null;
    alwayscopyemaillist?: string | null;
    mailTo?: string | null;
    payTo?: string | null;
    storeContactEmail?: string | null;
    sourceJobTypeId: number;
    sourceCustomerId: string;
    isCrossCustomer: boolean;
    adnArbStartShift?: (null | DateShiftDto);
    adnStartDateAfterTrialShift?: (null | DateShiftDto);
    uslaxNumberValidThroughShift?: (null | DateShiftDto);
    adminsToDeactivate: number;
    adminsPreserved: number;
    teamsToClone: number;
    teamsExcludedCompeting: number;
    teamsExcludedWaitlistDropped: number;
    teamsExcludedInactive: number;
    leagues?: Array<LeaguePlanDto>;
    bulletins?: Array<BulletinShiftDto>;
    agegroups?: Array<AgegroupPreviewDto>;
    feeModifiers?: Array<FeeModifierShiftDto>;
    warnings?: Array<string>;
};

