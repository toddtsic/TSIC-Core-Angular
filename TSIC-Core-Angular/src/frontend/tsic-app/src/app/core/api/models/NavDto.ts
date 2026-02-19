/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { NavItemDto } from './NavItemDto';
export type NavDto = {
    navId: number;
    roleId: string;
    jobId?: string | null;
    active: boolean;
    items: Array<NavItemDto>;
};

