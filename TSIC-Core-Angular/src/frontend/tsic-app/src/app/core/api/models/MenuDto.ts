/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { MenuItemDto } from './MenuItemDto';
export type MenuDto = {
    menuId: string;
    jobId: string;
    roleId?: string | null;
    menuTypeId: number;
    tag?: string | null;
    items: Array<MenuItemDto>;
};

