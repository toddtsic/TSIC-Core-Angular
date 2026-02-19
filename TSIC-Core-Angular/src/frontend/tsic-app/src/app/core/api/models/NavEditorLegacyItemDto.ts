/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type NavEditorLegacyItemDto = {
    menuItemId: string;
    parentMenuItemId?: string | null;
    index?: number;
    text?: string | null;
    iconName?: string | null;
    routerLink?: string | null;
    navigateUrl?: string | null;
    controller?: string | null;
    action?: string | null;
    target?: string | null;
    active: boolean;
    children: Array<NavEditorLegacyItemDto>;
};

