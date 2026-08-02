/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { LeagueRenameDto } from './LeagueRenameDto';
export type JobCloneRequest = {
    sourceJobId: string;
    targetCustomerId: string;
    jobPathTarget: string;
    jobNameTarget: string;
    yearTarget: string;
    seasonTarget: string;
    displayName: string;
    leagues?: Array<LeagueRenameDto>;
    expiryAdmin: string;
    expiryUsers: string;
    regFormFrom?: string | null;
    regFormCcs?: string | null;
    regFormBccs?: string | null;
    rescheduleemaillist?: string | null;
    alwayscopyemaillist?: string | null;
    mailTo?: string | null;
    payTo?: string | null;
    storeContactEmail?: string | null;
    upAgegroupNamesByOne?: boolean;
    noParallaxSlide1?: boolean;
    ladtScope?: string;
    copyDivisions?: boolean;
    enableEcheckChoice?: string;
    storeChoice?: string;
    planFingerprint?: string | null;
};

