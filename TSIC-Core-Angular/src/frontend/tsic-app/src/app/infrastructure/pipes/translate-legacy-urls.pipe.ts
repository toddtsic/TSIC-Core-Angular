import { Pipe, PipeTransform, inject } from '@angular/core';
import { JobService } from '@infrastructure/services/job.service';

/** Mirrors TSIC.Domain.Constants.JobConstants — see backend for canonical IDs. */
const JOB_TYPE_TOURNAMENT = 2;

/**
 * Legacy inline-style → class map. Angular's HTML sanitizer strips `style`
 * attributes on `[innerHTML]`, so bulletins authored with inline CSS lose
 * their typography. These classes are declared in bulletins.component.scss
 * and mapped to design-system variables.
 */
const FONT_SIZE_CLASSES: ReadonlyMap<string, string> = new Map([
    ['14px', 'bl-fs-14'],
    ['18px', 'bl-fs-18'],
    ['22px', 'bl-fs-22'],
]);
const COLOR_CLASSES: ReadonlyMap<string, string> = new Map([
    ['#0000ff', 'bl-c-blue'],
    ['#0000cd', 'bl-c-blue'],
    ['#ff0000', 'bl-c-red'],
    ['#000000', 'bl-c-black'],
]);

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
 * - Rosters/RostersPublicLookupTourny → /{jobPath}/rosters/public
 * - Schedules/Index (any query string) → /{jobPath}/schedule (public, anonymous-accessible)
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

        html = html.replace(anchorPattern, (fullMatch: string, url: string, linkText: string) => {
            const translated = this.translateAnchor(url, linkText, jobPath, adultRoleKey);
            return translated ?? fullMatch;
        });

        // Third pass: convert inline styles to sanitizer-safe classes / HTML attrs.
        return this.legacyStylesToSafeForm(html);
    }

    /**
     * Transforms inline `style="..."` attributes into classes (for known
     * font-size / color values) and HTML width/height attributes (for <img>
     * width/height styles). Unknown properties are left in `style` and fall
     * through to Angular's sanitizer — same behavior as before, no regression.
     *
     * Why: [innerHTML] sanitization strips `style` attributes. Legacy bulletins
     * (~94% of the corpus) rely on inline font-size and color for emphasis.
     * Classes and width/height attrs survive sanitization and preserve intent.
     */
    private legacyStylesToSafeForm(html: string): string {
        return html.replace(/<(\w+)([^>]*)>/gi, (_match, tag: string, attrs: string) => {
            const styleMatch = /\sstyle\s*=\s*(['"])([^'"]*?)\1/i.exec(attrs);
            if (!styleMatch) return `<${tag}${attrs}>`;

            const styleValue = styleMatch[2];
            const isImg = tag.toLowerCase() === 'img';
            const addedClasses: string[] = [];
            const keptDecls: string[] = [];
            let imgWidthAttr = '';
            let imgHeightAttr = '';

            for (const rawDecl of styleValue.split(';')) {
                const decl = rawDecl.trim();
                if (!decl) continue;
                const colonIdx = decl.indexOf(':');
                if (colonIdx < 0) continue;
                const property = decl.substring(0, colonIdx).trim().toLowerCase();
                const value = decl.substring(colonIdx + 1).trim();
                if (!property || !value) continue;

                // img-only: promote width/height to HTML attrs (sanitizer-safe)
                if (isImg && property === 'width') {
                    const m = /^(\d+)(?:px)?$/i.exec(value);
                    if (m) { imgWidthAttr = ` width="${m[1]}"`; continue; }
                }
                if (isImg && property === 'height') {
                    const m = /^(\d+)(?:px)?$/i.exec(value);
                    if (m) { imgHeightAttr = ` height="${m[1]}"`; continue; }
                }

                // font-size / color → allowlisted class
                if (property === 'font-size') {
                    const cls = FONT_SIZE_CLASSES.get(value.toLowerCase());
                    if (cls) { addedClasses.push(cls); continue; }
                }
                if (property === 'color') {
                    const cls = COLOR_CLASSES.get(value.toLowerCase());
                    if (cls) { addedClasses.push(cls); continue; }
                }

                // background-color:transparent → noise, drop (830 occurrences in corpus)
                if (property === 'background-color' && value.toLowerCase() === 'transparent') continue;

                // Unknown: keep in style; sanitizer will strip, same as today
                keptDecls.push(`${property}:${value}`);
            }

            // Strip the original style attr from attrs
            let newAttrs = attrs.replace(/\sstyle\s*=\s*(['"])[^'"]*?\1/i, '');

            // Strip existing img width/height HTML attrs if we're about to set them
            if (isImg && imgWidthAttr) {
                newAttrs = newAttrs.replace(/\swidth\s*=\s*(['"])[^'"]*?\1/i, '');
            }
            if (isImg && imgHeightAttr) {
                newAttrs = newAttrs.replace(/\sheight\s*=\s*(['"])[^'"]*?\1/i, '');
            }

            // Merge injected classes with any existing class attribute
            if (addedClasses.length > 0) {
                const classRegex = /\sclass\s*=\s*(['"])([^'"]*)\1/i;
                const classMatch = classRegex.exec(newAttrs);
                if (classMatch) {
                    const existing = classMatch[2].split(/\s+/).filter(Boolean);
                    const merged = Array.from(new Set([...existing, ...addedClasses])).join(' ');
                    newAttrs = newAttrs.replace(classRegex, ` class="${merged}"`);
                } else {
                    newAttrs = `${newAttrs} class="${addedClasses.join(' ')}"`;
                }
            }

            // Re-emit any un-transformed style declarations (will be stripped by sanitizer)
            if (keptDecls.length > 0) {
                newAttrs = `${newAttrs} style="${keptDecls.join(';')}"`;
            }

            newAttrs += imgWidthAttr + imgHeightAttr;

            // Collapse whitespace introduced by removals
            newAttrs = newAttrs.replace(/\s+/g, ' ').replace(/\s+$/, '');
            if (newAttrs && !newAttrs.startsWith(' ')) newAttrs = ' ' + newAttrs;

            return `<${tag}${newAttrs}>`;
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
            return `<a href="/${jobPath}/rosters/public">CLICK HERE</a>`;
        }

        if (lower.includes('schedules/index')) {
            return `<a href="/${jobPath}/schedule">${linkText}</a>`;
        }

        if (lower.includes('playerwaiverupdate')) {
            return `<a href="/${jobPath}/registration/self-roster-update">CLICK HERE</a>`;
        }

        return null;
    }
}
