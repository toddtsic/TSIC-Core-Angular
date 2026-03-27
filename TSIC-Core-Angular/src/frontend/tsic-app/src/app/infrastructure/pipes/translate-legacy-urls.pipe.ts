import { Pipe, PipeTransform } from '@angular/core';

/**
 * Pipe to transform legacy ASP.NET MVC URLs in HTML strings to new Angular routes.
 * 
 * Usage in template:
 * <div [innerHTML]="htmlContent | translateLegacyUrls:jobPath"></div>
 * 
 * Translations:
 * - StartARegistration + bPlayer=true → /{jobPath}/registration/player
 * - StartARegistration + bClubRep=true → /{jobPath}/registration/team
 * - StartARegistration + bStaff=true → /{jobPath}/registration/adult
 */
@Pipe({
    name: 'translateLegacyUrls',
    standalone: true
})
export class TranslateLegacyUrlsPipe implements PipeTransform {
    transform(html: string | null | undefined, jobPath: string): string {
        if (!html || !jobPath) {
            return html || '';
        }

        // Pattern to match href attributes with legacy URLs
        // Matches: href="..." or href='...'
        const hrefPattern = /href\s*=\s*["']([^"']+)["']/gi;

        return html.replace(hrefPattern, (match: string, url: string) => {
            const translatedUrl = this.translateUrl(url, jobPath);

            if (translatedUrl !== url) {
                return `href="${translatedUrl}"`;
            }

            return match;
        });
    }

    /**
     * Translates a single URL from legacy format to Angular route.
     */
    private translateUrl(url: string, jobPath: string): string {
        if (!url) return url;

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

        // Check for JobAdministrator/Admin
        if (lower.includes('jobadministrator/admin')) {
            return `/${jobPath}/configure/administrators`;
        }

        // No translation pattern matched - return original URL
        return url;
    }
}
