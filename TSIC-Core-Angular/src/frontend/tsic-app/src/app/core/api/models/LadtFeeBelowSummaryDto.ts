/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { LadtFeeBelowAmountsDto } from './LadtFeeBelowAmountsDto';
import type { LadtFeeBelowModifierDto } from './LadtFeeBelowModifierDto';
import type { LadtFeeBelowPhaseDto } from './LadtFeeBelowPhaseDto';
export type LadtFeeBelowSummaryDto = {
    amounts: LadtFeeBelowAmountsDto;
    phase: LadtFeeBelowPhaseDto;
    earlyBird: LadtFeeBelowModifierDto;
    lateFee: LadtFeeBelowModifierDto;
};

