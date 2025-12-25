/**
 * Route segment utilities for parsing TSIC URL structure
 * 
 * URL Structure: /:jSeg/:controller/:action
 * - jSeg (segment 1): Job path identifier (e.g., 'aim-cac-2026')
 * - controller (segment 2): Controller name (default: 'job-landing')
 * - action (segment 3): Action name (default: 'Index')
 * 
 * Examples:
 * - /aim-cac-2026 → jSeg: 'aim-cac-2026', controller: 'job-landing', action: 'Index'
 * - /aim-cac-2026/register-player → jSeg: 'aim-cac-2026', controller: 'register-player', action: 'Index'
 * - /aim-cac-2026/registration/player → jSeg: 'aim-cac-2026', controller: 'registration', action: 'player'
 */

// Segments that are structural and not job identifiers
const NON_JOB_SEGMENTS = new Set([
    'tsic',
    'register-player',
    'register-team',
    'family-account',
    'login',
    'admin'
]);

/**
 * Parsed route segments with defaults
 */
export interface RouteSegments {
    /** Job path segment (first segment) */
    jSeg: string | null;
    /** Controller segment (second segment, defaults to 'job-landing') */
    controller: string;
    /** Action segment (third segment, defaults to 'Index') */
    action: string;
    /** Raw URL path */
    fullPath: string;
    /** All segments after jSeg */
    remainingSegments: string[];
}

/**
 * Parse a URL path into structured route segments
 * @param url Full URL or path to parse (query params and fragments are stripped)
 * @returns Parsed route segments with defaults applied
 */
export function parseRouteSegments(url: string): RouteSegments {
    // Strip query params and fragments
    const path = url.split('?')[0].split('#')[0];
    const segments = path.split('/').filter(s => !!s);

    let jSeg: string | null = null;
    let controller = 'job-landing';
    let action = 'Index';
    let remainingSegments: string[] = [];

    if (segments.length === 0) {
        return { jSeg, controller, action, fullPath: path, remainingSegments };
    }

    // First segment is jSeg (unless it's a structural segment like 'tsic')
    const firstSegment = segments[0].toLowerCase();
    if (!NON_JOB_SEGMENTS.has(firstSegment)) {
        jSeg = segments[0];
        remainingSegments = segments.slice(1);

        // Second segment is controller (if present)
        if (segments.length >= 2) {
            controller = segments[1];
        }

        // Third segment is action (if present)
        if (segments.length >= 3) {
            action = segments[2];
        }
    } else {
        // If first segment is structural (like 'tsic'), treat differently
        jSeg = null;
        controller = segments[0];
        action = segments.length >= 2 ? segments[1] : 'Index';
        remainingSegments = segments.slice(1);
    }

    return { jSeg, controller, action, fullPath: path, remainingSegments };
}

/**
 * Check if current route matches specific controller
 * @param url Current URL or path
 * @param controllerName Controller name to match (case-insensitive)
 * @returns True if controller matches
 */
export function isController(url: string, controllerName: string): boolean {
    const segments = parseRouteSegments(url);
    return segments.controller.toLowerCase() === controllerName.toLowerCase();
}

/**
 * Check if current route matches specific controller and action
 * @param url Current URL or path
 * @param controllerName Controller name to match (case-insensitive)
 * @param actionName Action name to match (case-insensitive)
 * @returns True if both controller and action match
 */
export function isControllerAction(url: string, controllerName: string, actionName: string): boolean {
    const segments = parseRouteSegments(url);
    return segments.controller.toLowerCase() === controllerName.toLowerCase() &&
        segments.action.toLowerCase() === actionName.toLowerCase();
}

/**
 * Check if current route is the job landing page (jSeg only, default controller/action)
 * @param url Current URL or path
 * @returns True if this is a job landing page
 */
export function isJobLanding(url: string): boolean {
    const segments = parseRouteSegments(url);
    return segments.jSeg !== null &&
        segments.controller.toLowerCase() === 'job-landing' &&
        segments.action.toLowerCase() === 'index';
}

/**
 * Build a URL path from segments
 * @param jSeg Job segment
 * @param controller Controller name (optional, defaults to 'job-landing')
 * @param action Action name (optional, defaults to 'Index')
 * @returns URL path string
 */
export function buildRoutePath(jSeg: string, controller?: string, action?: string): string {
    let path = `/${jSeg}`;

    if (controller && controller.toLowerCase() !== 'job-landing') {
        path += `/${controller}`;

        if (action && action.toLowerCase() !== 'index') {
            path += `/${action}`;
        }
    }

    return path;
}
