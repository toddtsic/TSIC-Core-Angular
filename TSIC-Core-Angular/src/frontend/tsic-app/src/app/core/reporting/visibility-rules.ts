/**
 * Client-side port of the backend IVisibilityRulesEvaluator.
 *
 * Used for filtering the hard-coded Type 1 report catalogue against the
 * current job's sport / jobtype / flag context. Type 2 reports are already
 * filtered server-side before reaching the client.
 *
 * Shape matches TSIC.Contracts.Dtos.NavItemVisibilityRules (backend).
 * Evaluation matches TSIC.Infrastructure.Services.VisibilityRulesEvaluator.Passes.
 */

import type { JobMetadataResponse, JobPulseDto } from '@core/api';

export interface VisibilityRules {
    sports?: string[];
    jobTypes?: string[];
    customersDeny?: string[];
    requiresFlags?: string[];
    requiresRoles?: string[];
}

export interface JobVisibilityContext {
    sportName: string | null;
    jobTypeName: string | null;
    customerName: string | null;
    activeFlags: ReadonlySet<string>;
    callerRoles: ReadonlySet<string>;
}

/**
 * Builds the visibility context from the available client-side job state.
 * Frontend doesn't currently have customerName in scope — customersDeny
 * rules effectively fail-open here. If per-customer gating is needed
 * for a Type 1 report, do it server-side instead.
 *
 * `callerRoles` should be the active user's role set (typically [user.role]
 * or user.roles when present). Items with `requiresRoles` are filtered out
 * when none of the caller's roles match.
 */
export function buildJobVisibilityContext(
    jobMetadata: JobMetadataResponse | null,
    pulse: JobPulseDto | null,
    callerRoles: Iterable<string>
): JobVisibilityContext {
    const flags = new Set<string>();

    if (jobMetadata?.bEnableStore) flags.add('storeEnabled');
    if (jobMetadata?.adnArb) flags.add('adnArb');
    if (pulse?.enableStayToPlay) flags.add('mobileEnabled');
    if (jobMetadata?.jobTypeId === 1 || jobMetadata?.jobTypeId === 4 || jobMetadata?.jobTypeId === 6) {
        flags.add('playerSiteOnly');
    }

    return {
        sportName: jobMetadata?.sportName ?? null,
        jobTypeName: jobMetadata?.jobTypeName ?? null,
        customerName: null,
        activeFlags: flags,
        callerRoles: new Set(callerRoles)
    };
}

/**
 * Evaluates rules (either a parsed object or the raw JSON string stored on
 * NavItem / ReportCatalogue rows) against the job context.
 *
 * null / empty / malformed rules => returns true (matches backend's fail-open).
 */
export function passesVisibilityRules(
    rules: VisibilityRules | string | null | undefined,
    ctx: JobVisibilityContext
): boolean {
    const parsed = normalize(rules);
    if (!parsed) return true;

    if (parsed.sports?.length && ctx.sportName
        && !parsed.sports.some(s => equalsInsensitive(s, ctx.sportName!))) {
        return false;
    }

    if (parsed.jobTypes?.length && ctx.jobTypeName
        && !parsed.jobTypes.some(t => equalsInsensitive(t, ctx.jobTypeName!))) {
        return false;
    }

    if (parsed.customersDeny?.length && ctx.customerName
        && parsed.customersDeny.some(c => equalsInsensitive(c, ctx.customerName!))) {
        return false;
    }

    if (parsed.requiresFlags?.length) {
        for (const flag of parsed.requiresFlags) {
            if (!ctx.activeFlags.has(flag)) return false;
        }
    }

    if (parsed.requiresRoles?.length) {
        const hasMatch = parsed.requiresRoles.some(r => ctx.callerRoles.has(r));
        if (!hasMatch) return false;
    }

    return true;
}

function normalize(rules: VisibilityRules | string | null | undefined): VisibilityRules | null {
    if (!rules) return null;
    if (typeof rules !== 'string') return rules;
    try {
        return JSON.parse(rules) as VisibilityRules;
    } catch {
        return null;
    }
}

function equalsInsensitive(a: string, b: string): boolean {
    return a.localeCompare(b, undefined, { sensitivity: 'accent' }) === 0;
}
