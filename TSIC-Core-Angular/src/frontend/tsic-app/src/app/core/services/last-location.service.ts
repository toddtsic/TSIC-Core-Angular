import { Injectable } from '@angular/core';
import { Router, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs/operators';

@Injectable({ providedIn: 'root' })
export class LastLocationService {
    private readonly STORAGE_KEY = 'last_job_path';

    constructor(private router: Router) {
        // Track last visited jobPath (first URL segment that isn't 'tsic')
        this.router.events
            .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
            .subscribe((e) => {
                const url = e.urlAfterRedirects || e.url;
                const path = (url.split('?')[0] || '').split('#')[0] || '';
                const first = path.split('/').find(Boolean) || '';
                // Only persist when it's a job path (not 'tsic') and looks safe
                if (first && first !== 'tsic' && this.isSafeJobPath(first)) {
                    localStorage.setItem(this.STORAGE_KEY, first);
                }
            });
    }

    getLastJobPath(): string | null {
        const v = localStorage.getItem(this.STORAGE_KEY);
        return v && this.isSafeJobPath(v) ? v : null;
    }

    private isSafeJobPath(s: string): boolean {
        // Basic allowlist for job path tokens: letters, numbers, dashes
        return /^[A-Za-z0-9-]+$/.test(s);
    }
}
