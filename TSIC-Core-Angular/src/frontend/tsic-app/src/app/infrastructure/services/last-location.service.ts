import { Injectable } from '@angular/core';
import { Router, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs/operators';
import { LocalStorageKey } from '@infrastructure/shared/local-storage.model';

@Injectable({ providedIn: 'root' })
export class LastLocationService {

    constructor(private readonly router: Router) {
        // Track last visited jobPath (first URL segment that isn't 'tsic')
        this.router.events
            .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
            .subscribe((e) => {
                const url = e.urlAfterRedirects || e.url;
                const path = (url.split('?')[0] || '').split('#')[0] || '';
                const first = path.split('/').find(Boolean) || '';

                // Clear stored path if landing on error pages
                if (first === 'not-found' || path === '**') {
                    localStorage.removeItem(LocalStorageKey.LastJobPath);
                    return;
                }

                // Only persist when it's a valid job path (not 'tsic' or 'tsic-v*', not error routes)
                if (first && !first.startsWith('tsic') && this.isSafeJobPath(first)) {
                    localStorage.setItem(LocalStorageKey.LastJobPath, first);
                }
            });
    }

    getLastJobPath(): string | null {
        const v = localStorage.getItem(LocalStorageKey.LastJobPath);
        return v && !v.startsWith('tsic') && this.isSafeJobPath(v) ? v : null;
    }

    private isSafeJobPath(s: string): boolean {
        // Exclude error/system routes
        const excludedRoutes = ['not-found', 'error', 'unauthorized', 'login', 'register'];
        if (excludedRoutes.includes(s.toLowerCase())) {
            return false;
        }
        // Basic allowlist for job path tokens: letters, numbers, dashes
        return /^[A-Za-z0-9-]+$/.test(s);
    }
}
