/**
 * Shared utility for building static asset URLs from TSIC banner/logo paths.
 *
 * Used by client-header-bar (job logos) and client-banner (banner images).
 * Handles relative paths, absolute URLs, BannerFiles prefix dedup, and junk filtering.
 */

const STATIC_BASE_URL = 'https://statics.teamsportsinfo.com/BannerFiles';

/**
 * Build a fully-qualified asset URL from a job's banner/logo path.
 *
 * @param path  Raw path value from the Job entity (may be relative, absolute, or junk)
 * @returns     Fully-qualified HTTPS URL, or empty string if the path is invalid
 */
export function buildAssetUrl(path?: string | null): string {
    if (!path) return '';

    const p = String(path).trim();
    if (!p || p === 'undefined' || p === 'null') return '';

    // Already absolute — just normalise double-slashes
    if (/^https?:\/\//i.test(p)) {
        return p.replace(/([^:])\/\/+/g, '$1/');
    }

    const noLead = p.replace(/^\/+/, '');

    // Strip redundant "BannerFiles/" prefix if the DB value already includes it
    if (/^BannerFiles\//i.test(noLead)) {
        const rest = noLead.replace(/^BannerFiles\//i, '');
        return `${STATIC_BASE_URL}/${rest}`;
    }

    // Reject bare slugs that look like route segments, not filenames
    if (!/[.]/.test(noLead) && /^[a-z0-9-]{2,20}$/i.test(noLead)) {
        return '';
    }

    return `${STATIC_BASE_URL}/${noLead}`;
}
