/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ClubRenameJobResult } from './ClubRenameJobResult';
export type AdminClubRenameResponse = {
    success: boolean;
    message?: string | null;
    newClubName?: string | null;
    perJob?: Array<ClubRenameJobResult>;
};

