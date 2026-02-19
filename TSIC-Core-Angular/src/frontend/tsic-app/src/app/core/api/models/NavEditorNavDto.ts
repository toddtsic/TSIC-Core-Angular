/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { NavEditorNavItemDto } from './NavEditorNavItemDto';
export type NavEditorNavDto = {
    navId: number;
    roleId: string;
    roleName?: string | null;
    jobId?: string | null;
    active: boolean;
    isDefault: boolean;
    items: Array<NavEditorNavItemDto>;
};

