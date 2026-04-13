import { Pipe, PipeTransform, inject } from '@angular/core';
import { JobService } from '@infrastructure/services/job.service';

/** Mirrors TSIC.Domain.Constants.JobConstants — see backend for canonical IDs. */
const JOB_TYPE_TOURNAMENT = 2;

/**
 * Pipe to transform legacy ASP.NET MVC URLs in HTML strings to new Angular routes.
 *
 * Usage in template:
 * <div [innerHTML]="htmlContent | translateLegacyUrls:jobPath"></div>
 *
 * Translations:
 * - StartARegistration + bPlayer + bStaff → split into TWO links (player + adult).
 *   The adult link uses ?role=unassigned on player sites (BAllowRosterViewAdult=false),
 *   ?role=coach on tournaments. URL is self-describing for the actual outcome.
 * - StartARegistration + bPlayer=true → /{jobPath}/registration/player
 * - StartARegistration + bClubRep=true → /{jobPath}/registration/team
 * - StartARegistration + bStaff=true → /{jobPath}/registration/adult?role={unassigned|coach}
 *   Same site-aware key choice based on BAllowRosterViewAdult.
 * - Rosters/RostersPublicLookupTourny → /{jobPath}/rosters
 *
 * Site-awareness: pipe reads JobService.currentJob().bAllowRosterViewAdult to decide
 * the URL key. true (tournament with public adult roster) → coach (resolves to Staff).
 * false (player site) → unassigned (resolves unconditionally to UnassignedAdult).
 */
@Pipe({
    name: 'translateLegacyUrls',
    standalone: true
})
export class TranslateLegacyUrlsPipe implements PipeTransform {
    private readonly jobService = inject(JobService);

    transform(html: string | null | undefined, jobPath: string): string {
        if (!html || !jobPath) {
            return html || '';
        }

        const adultRoleKey = this.adultRoleKey();

        // First pass: replace entire <li> elements containing combined player+staff links.
        // This removes the orphaned surrounding text (e.g. "to BEGIN / EDIT a PLAYER or COACH...").
        const liPattern = /<li[^>]*>([\s\S]*?)<\/li>/gi;
        html = html.replace(liPattern, (liMatch: string, liContent: string) => {
            const anchorInLi = /<a\s[^>]*href\s*=\s*["']([^"']+)["'][^>]*>([\s\S]*?)<\/a>/i.exec(liContent);
            if (!anchorInLi) return liMatch;
            const url = anchorInLi[1].toLowerCase();
            if (url.includes('startaregistration') && url.includes('bplayer=true') && url.includes('bstaff=true')) {
                const playerUrl = `/${jobPath}/registration/player`;
                const adultUrl = `/${jobPath}/registration/adult?role=${adultRoleKey}`;
                const adultLabel = adultRoleKey === 'unassigned'
                    ? `<strong>COACH / VOLUNTEER</strong> (director approval required)`
                    : `<strong>COACH</strong>`;
                return `</ul><p style="margin-bottom:0.25em;"><strong>SELF-ROSTERING:</strong></p><ul style="margin-top:0;">` +
                    `<li><a href="${playerUrl}">CLICK HERE</a> to self-roster a <strong>PLAYER</strong></li>` +
                    `<li><a href="${adultUrl}">CLICK HERE</a> to self-roster a ${adultLabel}</li>` +
                    `<li style="list-style:none; margin-top:0.25em;">All players and coaches must be Self-Rostered in order to participate.</li>`;
            }
            return liMatch;
        });

        // Second pass: translate remaining individual anchor tags.
        const anchorPattern = /<a\s[^>]*href\s*=\s*["']([^"']+)["'][^>]*>([\s\S]*?)<\/a>/gi;

        return html.replace(anchorPattern, (fullMatch: string, url: string, linkText: string) => {
            const translated = this.translateAnchor(url, linkText, jobPath, adultRoleKey);
            return translated ?? fullMatch;
        });
    }

    /**
     * Returns 'unassigned' on Club/League/etc. (player sites), 'coach' on Tournament.
     * Discriminator is JobTypeId — canonical numeric ID matching backend JobConstants.
     * Defaults to 'unassigned' (fail closed for minor-PII safety) when job metadata
     * is not yet loaded; backend rejects 'unassigned' on Tournament jobs so a stale
     * tournament bulletin would surface loudly rather than silently wrong-routing.
     */
    private adultRoleKey(): 'unassigned' | 'coach' {
        return this.jobService.currentJob()?.jobTypeId === JOB_TYPE_TOURNAMENT ? 'coach' : 'unassigned';
    }

    /**
     * Translates a single anchor tag. Returns replacement HTML or null if no translation needed.
     */
    private translateAnchor(url: string, linkText: string, jobPath: string, adultRoleKey: 'unassigned' | 'coach'): string | null {
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
                return `<a href="/${jobPath}/registration/adult?role=${adultRoleKey}">${linkText}</a>`;
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
