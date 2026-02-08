/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type MenuItemAdminDto = {
    menuItemId: string;
    menuId: string;
    parentMenuItemId?: string | null;
    text?: string | null;
    iconName?: string | null;
    routerLink?: string | null;
    navigateUrl?: string | null;
    controller?: string | null;
    action?: string | null;
    target?: string | null;
    active: boolean;
    index?: number;
    children: Array<MenuItemAdminDto>;
};

