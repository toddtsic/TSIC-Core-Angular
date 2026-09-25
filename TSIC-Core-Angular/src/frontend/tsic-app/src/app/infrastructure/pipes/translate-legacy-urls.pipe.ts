import { Pipe, PipeTransform, inject } from '@angular/core';
import { JobService } from '@infrastructure/services/job.service';

/** Mirrors TSIC.Domain.Constants.JobConstants — see backend for canonical IDs. */
const JOB_TYPE_TOURNAMENT = 2;
const JOB_TYPE_LEAGUE = 3;

/**
 * Legacy inline-style → class map.
 *
 * This began as a survival mechanism: Angular's built-in sanitizer strips `style`
 * attributes, so these hand-listed legacy values were rewritten to classes to keep
 * their typography. `RichTextPipe` now solves that generally — inline styles survive —
 * so the mapping is no longer what keeps legacy bulletins readable.
 *
 * It is kept because it does something the general fix cannot: these classes resolve to
 * design-system variables (see `_component-overrides.scss`), so a legacy `#0000ff` shows
 * as the palette's blue and stays legible in dark mode, where the raw hex would not.
 * Legacy values get the upgrade; everything else passes through untouched.
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
 * Highlight backgrounds — the same upgrade as COLOR_CLASSES, applied to the other half
 * of the pairing.
 *
 * Authors highlight through the RTE's `BackgroundColor` button, which emits an inline
 * `background-color` and NO text colour. The background is a fixed literal; the text
 * colour comes from the theme. In dark mode the theme supplies near-white, so a yellow
 * highlight renders white-on-yellow at ~1.02:1 — legible in light mode, invisible in
 * dark. `_theme-dark.scss` already carries a `mark { color: #1a1a1a }` rule aimed at
 * this, but it keys on the `<mark>` TAG and the editor has never produced one.
 *
 * So: resolve the authored background, and when it is light, tag the element with a
 * class that pins dark text. Only when the element declares no `color` of its own —
 * an author who chose both halves keeps their choice.
 */
const HIGHLIGHT_CLASS = 'bl-hl';

/** CSS named colours reachable from the legacy editors' swatch grids. */
const NAMED_COLORS: ReadonlyMap<string, string> = new Map([
    ['yellow', '#ffff00'], ['gold', '#ffd700'], ['orange', '#ffa500'],
    ['lime', '#00ff00'], ['aqua', '#00ffff'], ['cyan', '#00ffff'],
    ['fuchsia', '#ff00ff'], ['magenta', '#ff00ff'], ['pink', '#ffc0cb'],
    ['white', '#ffffff'], ['silver', '#c0c0c0'],
    ['lightgray', '#d3d3d3'], ['lightgrey', '#d3d3d3'],
    ['lightyellow', '#ffffe0'], ['lightgreen', '#90ee90'], ['lightblue', '#add8e6'],
]);

/**
 * WCAG relative luminance, or null if the value isn't a colour this can resolve
 * (gradients, `var(...)`, `currentColor`, anything unrecognised) — callers treat null
 * as "leave it alone".
 */
function relativeLuminance(rawValue: string): number | null {
    const value = rawValue.trim().toLowerCase();
    let r: number, g: number, b: number;

    const named = NAMED_COLORS.get(value);
    const hex = named ?? value;

    const rgbMatch = /^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/.exec(hex);
    if (rgbMatch) {
        [r, g, b] = [Number(rgbMatch[1]), Number(rgbMatch[2]), Number(rgbMatch[3])];
    } else if (/^#[0-9a-f]{6}$/.test(hex)) {
        [r, g, b] = [
            parseInt(hex.slice(1, 3), 16),
            parseInt(hex.slice(3, 5), 16),
            parseInt(hex.slice(5, 7), 16),
        ];
    } else if (/^#[0-9a-f]{3}$/.test(hex)) {
        [r, g, b] = [
            parseInt(hex[1] + hex[1], 16),
            parseInt(hex[2] + hex[2], 16),
            parseInt(hex[3] + hex[3], 16),
        ];
    } else {
        return null;
    }

    const channel = (v: number): number => {
        const s = v / 255;
        return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
    };
    return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

/**
 * Above this luminance, dark text beats white text on the same fill.
 *
 * Not a taste call — it is where the two contrast ratios cross. White scores
 * 1.05 / (L + 0.05); near-black (#1a1a1a, L ≈ 0.0116) scores (L + 0.05) / 0.0616.
 * Setting them equal gives (L + 0.05)² = 1.05 × 0.0616, so L ≈ 0.204. Yellow sits at
 * 0.93 and orange at 0.48, both well clear; a mid grey at 0.22 is marginal either way,
 * which is exactly what a crossover point means.
 */
const LIGHT_BACKGROUND_LUMINANCE = 0.204;

/**
 * Pipe to transform legacy ASP.NET MVC URLs in HTML strings to new Angular routes.
 *
 * Usage in template:
 * <div [innerHTML]="htmlContent | translateLegacyUrls:jobPath"></div>
 *
 * Translations:
 * - StartARegistration + bPlayer + bStaff → split into TWO links (player + adult).
 *   The adult link uses ?role=unassigned on player sites (Club), ?role=coach on
 *   team-registration sites (Tournament/League). URL is self-describing for the
 *   actual outcome.
 * - StartARegistration + bPlayer=true → /{jobPath}/registration/player
 * - StartARegistration + bClubRep=true → /{jobPath}/registration/team
 * - StartARegistration + bStaff=true → /{jobPath}/registration/adult?role={unassigned|coach}
 *   Same site-aware key choice.
 * - Rosters/RostersPublicLookupTourny → /{jobPath}/rosters/public
 * - Schedules/Index (any query string) → /{jobPath}/schedule (public, anonymous-accessible)
 *
 * Site-awareness: pipe reads JobService.currentJob().jobTypeId (see adultRoleKey).
 * Tournament/League → coach (resolves to Staff, direct placement). Club/other →
 * unassigned (resolves unconditionally to UnassignedAdult, director approves).
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
     * width/height styles). Unknown properties are left in `style`.
     *
     * Why: legacy bulletins (~94% of the corpus) rely on inline font-size and color
     * for emphasis, and the curated values above map to palette variables that adapt
     * to the active theme — better than the literal hex the author typed in 2014.
     *
     * Declarations this does not recognise are re-emitted in `style` and now render as
     * authored, because `RichTextPipe` runs after this pipe and permits an allowlist of
     * CSS properties. That is what carries today's font-size and colour choices; before
     * it existed, anything not converted here was silently deleted.
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
            // Pre-scan: an author who set their own text colour keeps it, whatever the
            // background resolves to. Only a background WITHOUT a partner colour is the
            // half-authored pair this fixes.
            const declaresOwnColor = /(^|;)\s*color\s*:/i.test(styleValue);

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

                // A light authored background with no authored text colour: tag it so the
                // text is pinned dark instead of inheriting the theme's (near-white in dark
                // mode). The declaration itself is KEPT — we are supplying the missing half
                // of the pair, not overriding the author's half. `declaresOwnColor` is
                // pre-scanned because `color` may appear after `background-color` here.
                if ((property === 'background-color' || property === 'background') && !declaresOwnColor) {
                    const luminance = relativeLuminance(value);
                    if (luminance !== null && luminance > LIGHT_BACKGROUND_LUMINANCE) {
                        addedClasses.push(HIGHLIGHT_CLASS);
                    }
                }

                // Not a curated legacy value: keep it in `style`. RichTextPipe decides
                // whether the property is allowed to render.
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

            // Re-emit any un-transformed style declarations
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
     * Returns 'coach' on team-registration sites (Tournament/League — coach key resolves
     * to Staff DIRECT placement, ruling 2026-08-14), 'unassigned' on Club/etc. (player
     * sites — UA funnel, director approves). Discriminator is JobTypeId — canonical
     * numeric ID matching backend JobConstants. Defaults to 'unassigned' (fail closed
     * for minor-PII safety) when job metadata is not yet loaded; backend rejects
     * 'unassigned' on Tournament jobs so a stale tournament bulletin would surface
     * loudly rather than silently wrong-routing.
     */
    private adultRoleKey(): 'unassigned' | 'coach' {
        const jobTypeId = this.jobService.currentJob()?.jobTypeId;
        return jobTypeId === JOB_TYPE_TOURNAMENT || jobTypeId === JOB_TYPE_LEAGUE
            ? 'coach' : 'unassigned';
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
