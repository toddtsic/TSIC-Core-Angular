import { Pipe, PipeTransform } from '@angular/core';

/**
 * Pipe to transform legacy ASP.NET MVC URLs in HTML strings to new Angular routes.
 *
 * Usage in template:
 * <div [innerHTML]="htmlContent | translateLegacyUrls:jobPath"></div>
 *
 * Translations:
 * - StartARegistration + bPlayer + bStaff → split into TWO links (player + coach)
 * - StartARegistration + bPlayer=true → /{jobPath}/registration/player
 * - StartARegistration + bClubRep=true → /{jobPath}/registration/team
 * - StartARegistration + bStaff=true → /{jobPath}/registration/adult
 * - Rosters/RostersPublicLookupTourny → /{jobPath}/rosters
 *
 * Combined links (bPlayer=true&bStaff=true) were used in the legacy system to show
 * a dropdown choosing player vs coach. In the new system these are separate wizards,
 * so the pipe splits the single <a> into two links.
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

        // Match full <a> tags so we can replace combined links with multiple elements.
        const anchorPattern = /<a\s[^>]*href\s*=\s*["']([^"']+)["'][^>]*>([\s\S]*?)<\/a>/gi;

        return html.replace(anchorPattern, (fullMatch: string, url: string, linkText: string) => {
            const translated = this.translateAnchor(url, linkText, jobPath);
            return translated ?? fullMatch;
        });
    }

    /**
     * Translates a single anchor tag. Returns replacement HTML or null if no translation needed.
     */
    private translateAnchor(url: string, linkText: string, jobPath: string): string | null {
        if (!url) return null;

        const lower = url.toLowerCase();

        if (lower.includes('startaregistration')) {
            const hasPlayer = lower.includes('bplayer=true');
            const hasStaff = lower.includes('bstaff=true');
            const hasClubRep = lower.includes('bclubrep=true');

            // Combined player + staff/coach link → split into two separate links
            if (hasPlayer && hasStaff) {
                const playerUrl = `/${jobPath}/registration/player`;
                const adultUrl = `/${jobPath}/registration/adult`;
                return `<ul style="list-style:disc; padding-left:1.5em; margin:0.25em 0;">` +
                    `<li><a href="${playerUrl}">${linkText}</a> to register a <strong>PLAYER</strong></li>` +
                    `<li><a href="${adultUrl}">${linkText}</a> to register a <strong>COACH</strong></li>` +
                    `</ul>`;
            }

            if (hasClubRep) {
                return `<a href="/${jobPath}/registration/team">${linkText}</a>`;
            }
            if (hasStaff) {
                return `<a href="/${jobPath}/registration/adult">${linkText}</a>`;
            }
            if (hasPlayer) {
                return `<a href="/${jobPath}/registration/player">${linkText}</a>`;
            }
        }

        if (lower.includes('jobadministrator/admin')) {
            return `<a href="/${jobPath}/configure/administrators">${linkText}</a>`;
        }

        if (lower.includes('rosters/rosterspubliclookuptourny')) {
            return `<a href="/${jobPath}/rosters">${linkText}</a>`;
        }

        return null;
    }
}
