/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AgegroupPreviewDto } from './AgegroupPreviewDto';
import type { BulletinShiftDto } from './BulletinShiftDto';
import type { DateShiftDto } from './DateShiftDto';
import type { FeeModifierShiftDto } from './FeeModifierShiftDto';
export type JobClonePreviewResponse = {
    yearDelta: number;
    inferredLeagueName: string;
    currentProcessingFeePercent: number;
    sourceProcessingFeePercent?: number | null;
    currentEcheckProcessingFeePercent: number;
    sourceEcheckProcessingFeePercent?: number | null;
    sourceBEnableEcheck: boolean;
    sourceBEnableStore: boolean;
    eventStartShift?: (null | DateShiftDto);
    eventEndShift?: (null | DateShiftDto);
    adnArbStartShift?: (null | DateShiftDto);
    adminsToDeactivate: number;
    adminsPreserved: number;
    teamsToClone: number;
    teamsExcludedPaid: number;
    teamsExcludedWaitlistDropped: number;
    bulletins?: Array<BulletinShiftDto>;
    agegroups?: Array<AgegroupPreviewDto>;
    feeModifiers?: Array<FeeModifierShiftDto>;
};

