/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type PlayerCheckinRowDto = {
    registrationId: string;
    playerUserId: string;
    firstName: string;
    lastName: string;
    clubName?: string | null;
    schoolName?: string | null;
    gradYear?: string | null;
    position?: string | null;
    owedTotal: number;
    paidTotal: number;
    hasMedForm: boolean;
    checkedInTs?: string | null;
    checkedInByRegId?: string | null;
};

