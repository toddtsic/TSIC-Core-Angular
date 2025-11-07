import { Injectable } from '@angular/core';
import { NavigationEnd, Router, UrlTree } from '@angular/router';
import { filter } from 'rxjs/operators';

type ThemeKey = 'landing' | 'login' | 'role-select' | 'player' | 'family';

@Injectable({ providedIn: 'root' })
export class ThemeOverridesService {
    constructor(private readonly router: Router) {
        // Apply once at startup
        this.applyForUrl(this.router.url);
        // Re-apply on navigation changes to capture jobPath changes
        this.router.events.pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
            .subscribe(e => this.applyForUrl(e.urlAfterRedirects));
    }

    private applyForUrl(url: string) {
        const tree: UrlTree = this.router.parseUrl(url);
        const primary = tree.root.children['primary'];
        const firstSeg = primary?.segments?.[0]?.path ?? '';
        // Only apply for job-scoped routes (exclude TSIC area)
        if (!firstSeg || firstSeg.toLowerCase() === 'tsic') {
            return;
        }
        this.applyForJob(firstSeg);
    }

    private storageKey(jobPath: string, theme: ThemeKey) {
        return `tsic:theme:${jobPath}:${theme}`;
    }

    private ensureStyleElement(jobPath: string): HTMLStyleElement {
        const id = `tsic-theme-${jobPath}`;
        let style = document.getElementById(id) as HTMLStyleElement | null;
        if (!style) {
            style = document.createElement('style');
            style.id = id;
            document.head.appendChild(style);
        }
        return style;
    }

    private buildCssFromTokens(theme: ThemeKey, primaryToken: string, startToken: string, endToken: string): string {
        return `\n.wizard-theme-${theme} {\n  --color-primary: var(${primaryToken});\n  --bs-primary: var(${primaryToken});\n  --gradient-start: var(${startToken});\n  --gradient-end: var(${endToken});\n  --gradient-primary-start: var(${startToken});\n  --gradient-primary-end: var(${endToken});\n}`;
    }

    private buildCssLegacy(theme: ThemeKey, primaryHex: string, startHex: string, endHex: string): string {
        // Best-effort bootstrap mapping; legacy values won't adapt to dark mode
        return `\n.wizard-theme-${theme} {\n  --gradient-primary-start: ${startHex};\n  --gradient-primary-end: ${endHex};\n  --bs-primary: ${primaryHex};\n}`;
    }

    applyForJob(jobPath: string) {
        const themes: ThemeKey[] = ['landing', 'login', 'role-select', 'player', 'family'];
        const cssParts: string[] = [];
        for (const t of themes) {
            const raw = localStorage.getItem(this.storageKey(jobPath, t));
            if (!raw) continue;
            try {
                const obj = JSON.parse(raw);
                if (obj?.primaryToken && obj?.gradientStartToken && obj?.gradientEndToken) {
                    cssParts.push(this.buildCssFromTokens(t, obj.primaryToken, obj.gradientStartToken, obj.gradientEndToken));
                } else if (obj?.primary && obj?.gradientStart && obj?.gradientEnd) {
                    cssParts.push(this.buildCssLegacy(t, obj.primary, obj.gradientStart, obj.gradientEnd));
                }
            } catch { /* ignore */ }
        }
        const style = this.ensureStyleElement(jobPath);
        style.textContent = cssParts.join('\n');
    }
}
