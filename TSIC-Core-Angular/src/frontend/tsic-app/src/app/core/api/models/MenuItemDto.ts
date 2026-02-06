/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type MenuItemDto = {
    menuItemId: string;
    parentMenuItemId?: string | null;
    index?: number;
    text?: string | null;
    iconName?: string | null;
    bCollapsed: boolean;
    bTextWrap: boolean;
    routerLink?: string | null;
    navigateUrl?: string | null;
    controller?: string | null;
    action?: string | null;
    linkTarget?: string | null;
    isImplemented: boolean;
    children: Array<MenuItemDto>;
};

