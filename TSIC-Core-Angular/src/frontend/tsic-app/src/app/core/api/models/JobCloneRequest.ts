/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type JobCloneRequest = {
    sourceJobId: string;
    jobPathTarget: string;
    jobNameTarget: string;
    yearTarget: string;
    seasonTarget: string;
    displayName: string;
    leagueNameTarget: string;
    expiryAdmin: string;
    expiryUsers: string;
    regFormFrom?: string | null;
    upAgegroupNamesByOne?: boolean;
    setDirectorsToInactive?: boolean;
    noParallaxSlide1?: boolean;
    ladtScope?: string;
    processingFeeChoice?: string;
    customProcessingFeePercent?: number | null;
    echeckProcessingFeeChoice?: string;
    customEcheckProcessingFeePercent?: number | null;
    enableEcheckChoice?: string;
    storeChoice?: string;
};

