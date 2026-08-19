import { UrlSegment, UrlMatchResult } from '@angular/router';

/**
 * Case-insensitive matcher for the legacy ASL roster board path.
 *
 * ASP.NET routing matched controller/action segments case-insensitively, so every casing of
 * `{jSeg}/ASLRosters/Index` resolved in the legacy site. Angular routes are case-SENSITIVE, and
 * American Select links this page in from its own site — we don't control how those hrefs are
 * cased, and a casing miss lands the visitor on a 404 rather than the rosters. Matching the way
 * legacy did keeps every existing inbound link working.
 *
 * Accepts `aslrosters` alone or `aslrosters/index`, in any casing.
 */
export function aslRostersMatcher(segments: UrlSegment[]): UrlMatchResult | null {
	if (segments.length === 0 || segments.length > 2) return null;
	if (segments[0].path.toLowerCase() !== 'aslrosters') return null;
	if (segments.length === 2 && segments[1].path.toLowerCase() !== 'index') return null;

	return { consumed: segments };
}
