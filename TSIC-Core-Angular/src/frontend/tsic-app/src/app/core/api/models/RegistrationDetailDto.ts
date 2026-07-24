/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AccountingRecordDto } from './AccountingRecordDto';
import type { FamilyContactDto } from './FamilyContactDto';
import type { SubscriptionDetailDto } from './SubscriptionDetailDto';
import type { UserDemographicsDto } from './UserDemographicsDto';
export type RegistrationDetailDto = {
    registrationId: string;
    registrationAi: number;
    firstName: string;
    lastName: string;
    email: string;
    phone?: string | null;
    roleName: string;
    active: boolean;
    teamName?: string | null;
    assignment?: string | null;
    feeBase: number;
    feeProcessing: number;
    feeDiscount: number;
    feeTotal: number;
    paidTotal: number;
    owedTotal: number;
    profileValues: Record<string, string>;
    profileMetadataJson?: string | null;
    coachRequestNote?: string | null;
    coachRequestedTeams?: any[] | null;
    accountUsername?: string | null;
    familyUserId?: string | null;
    sportName?: string | null;
    jsonOptions?: string | null;
    momLabel?: string;
    dadLabel?: string;
    familyContact?: (null | FamilyContactDto);
    userDemographics?: (null | UserDemographicsDto);
    familyAccountDemographics?: (null | UserDemographicsDto);
    registrationDate?: string | null;
    modifiedDate?: string | null;
    hasSubscription?: boolean;
    storedSubscription?: (null | SubscriptionDetailDto);
    accountingRecords: Array<AccountingRecordDto>;
    isClubRep?: boolean;
    clubRepTeamCount?: number;
    clubId?: number | null;
    clubName?: string | null;
};

