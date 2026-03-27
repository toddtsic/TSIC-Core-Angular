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
     * - StartARegistration + bClubRep=true → /{jobPath}/registration/team
     * - StartARegistration + bStaff=true → /{jobPath}/registration/adult
     * - StartARegistration + bPlayer=true → /{jobPath}/registration/player
     */
    public static translateUrl(url: string, jobPath: string): string {
        if (!url || !jobPath) {
            return url;
        }

        const lower = url.toLowerCase();

        if (lower.includes('startaregistration')) {
            // More specific patterns first
            if (lower.includes('bclubrep=true')) {
                return `/${jobPath}/registration/team`;
            }
            if (lower.includes('bstaff=true')) {
                return `/${jobPath}/registration/adult`;
            }
            if (lower.includes('bplayer=true')) {
                return `/${jobPath}/registration/player`;
            }
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

        const lower = url.toLowerCase();
        return lower.includes('startaregistration') &&
            (lower.includes('bplayer=true') || lower.includes('bclubrep=true') || lower.includes('bstaff=true'));
    }
}
