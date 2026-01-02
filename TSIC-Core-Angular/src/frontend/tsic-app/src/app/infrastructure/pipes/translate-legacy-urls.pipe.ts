import { Pipe, PipeTransform } from '@angular/core';

/**
 * Pipe to transform legacy ASP.NET MVC URLs in HTML strings to new Angular routes.
 * 
 * Usage in template:
 * <div [innerHTML]="htmlContent | translateLegacyUrls:jobPath"></div>
 * 
 * Translations:
 * - StartARegistration + bPlayer=true → /{jobPath}/register-player
 * - StartARegistration + bClubRep=true → /{jobPath}/register-team
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

            // If URL was translated, return the new href
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
}
