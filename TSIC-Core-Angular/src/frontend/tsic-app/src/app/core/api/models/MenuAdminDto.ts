/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { MenuItemAdminDto } from './MenuItemAdminDto';
export type MenuAdminDto = {
    menuId: string;
    jobId: string;
    roleId?: string | null;
    roleName?: string | null;
    active: boolean;
    menuTypeId: number;
    items: Array<MenuItemAdminDto>;
};

