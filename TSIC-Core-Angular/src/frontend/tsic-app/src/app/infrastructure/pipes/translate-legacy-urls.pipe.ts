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
 * - StartARegistration + bStaff=true → /{jobPath}/registration/adult?role=coach
 *   (Backend resolves to UnassignedAdult in Club/League, Staff in Tournament with flag guard.)
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

        // First pass: replace entire <li> elements containing combined player+staff links.
        // This removes the orphaned surrounding text (e.g. "to BEGIN / EDIT a PLAYER or COACH...").
        const liPattern = /<li[^>]*>([\s\S]*?)<\/li>/gi;
        html = html.replace(liPattern, (liMatch: string, liContent: string) => {
            const anchorInLi = /<a\s[^>]*href\s*=\s*["']([^"']+)["'][^>]*>([\s\S]*?)<\/a>/i.exec(liContent);
            if (!anchorInLi) return liMatch;
            const url = anchorInLi[1].toLowerCase();
            if (url.includes('startaregistration') && url.includes('bplayer=true') && url.includes('bstaff=true')) {
                const playerUrl = `/${jobPath}/registration/player`;
                const adultUrl = `/${jobPath}/registration/adult?role=coach`;
                return `</ul><p style="margin-bottom:0.25em;"><strong>SELF-ROSTERING:</strong></p><ul style="margin-top:0;">` +
                    `<li><a href="${playerUrl}">CLICK HERE</a> to self-roster a <strong>PLAYER</strong></li>` +
                    `<li><a href="${adultUrl}">CLICK HERE</a> to self-roster a <strong>COACH</strong></li>` +
                    `<li style="list-style:none; margin-top:0.25em;">All players and coaches must be Self-Rostered in order to participate.</li>`;
            }
            return liMatch;
        });

        // Second pass: translate remaining individual anchor tags.
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

            if (hasClubRep) {
                return `<a href="/${jobPath}/registration/team">${linkText}</a>`;
            }
            if (hasStaff && !hasPlayer) {
                return `<a href="/${jobPath}/registration/adult?role=coach">${linkText}</a>`;
            }
            if (hasPlayer && !hasStaff) {
                return `<a href="/${jobPath}/registration/player">${linkText}</a>`;
            }
        }

        if (lower.includes('jobadministrator/admin')) {
            return `<a href="/${jobPath}/configure/administrators">${linkText}</a>`;
        }

        if (lower.includes('rosters/rosterspubliclookuptourny') || lower.includes('rosters/rosterpubliclookup')) {
            return `<a href="/${jobPath}/rosters">CLICK HERE</a>`;
        }

        if (lower.includes('playerwaiverupdate')) {
            return `<a href="/${jobPath}/registration/self-roster-update">CLICK HERE</a>`;
        }

        return null;
    }
}
