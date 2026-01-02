import { Injectable } from '@angular/core';

/**
 * Service for translating legacy ASP.NET MVC URLs to new Angular routes.
 * Handles conversion of StartARegistration URLs with parameters to modern Angular paths.
 */
@Injectable({ providedIn: 'root' })
export class LegacyUrlTranslationService {
    /**
     * Translates a URL containing legacy StartARegistration patterns to Angular routes.
     * 
     * @param url - The URL to translate (can be full URL or relative path)
     * @param jobPath - The current job path context (e.g., "aim-cac-2026")
     * @returns The translated Angular route or original URL if no pattern matches
     * 
     * Translation patterns:
     * - StartARegistration + bPlayer=true → /{jobPath}/register-player
     * - StartARegistration + bClubRep=true → /{jobPath}/register-team
     */
    public static translateUrl(url: string, jobPath: string): string {
        if (!url || !jobPath) {
            return url;
        }

        // Check for StartARegistration with bPlayer=true parameter
        if (
            url.toLowerCase().includes('startaregistration') &&
            url.toLowerCase().includes('bplayer=true')
        ) {
            return `/${jobPath}/register-player`;
        }

        // Check for StartARegistration with bClubRep=true parameter
        if (
            url.toLowerCase().includes('startaregistration') &&
            url.toLowerCase().includes('bclubrep=true')
        ) {
            return `/${jobPath}/register-team`;
        }

        // No translation pattern matched - return original URL
        return url;
    }

    /**
     * Determines if a URL is a legacy ASP.NET MVC URL that should be translated.
     * @param url - The URL to check
     * @returns true if the URL contains legacy StartARegistration pattern
     */
    public static isLegacyUrl(url: string): boolean {
        if (!url) return false;

        const lowerUrl = url.toLowerCase();
        return lowerUrl.includes('startaregistration') &&
            (lowerUrl.includes('bplayer=true') || lowerUrl.includes('bclubrep=true'));
    }
}
