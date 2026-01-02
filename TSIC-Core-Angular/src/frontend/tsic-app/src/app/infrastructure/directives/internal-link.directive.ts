import { Directive, HostListener, inject, input } from '@angular/core';
import { Router } from '@angular/router';

/**
 * Directive to intercept clicks on app-internal links within dynamically rendered HTML.
 * Routes absolute app paths (starting with /) via Angular Router to prevent full-page reloads.
 * Validates that links are for the current job only.
 * 
 * Usage:
 * <div [innerHTML]="htmlContent | pipe" appInternalLink [jobPath]="currentJobPath"></div>
 */
@Directive({
    selector: '[appInternalLink]',
    standalone: true
})
export class InternalLinkDirective {
    private readonly router = inject(Router);
    jobPath = input<string>('');

    @HostListener('click', ['$event'])
    onClick(event: Event): void {
        const target = event.target as HTMLElement;

        // Find the closest <a> tag (handles clicks on child elements inside the link)
        const link = target.closest('a') as HTMLAnchorElement;
        if (!link) {
            return;
        }

        const href = link.getAttribute('href');

        // Skip if no href
        if (!href) {
            return;
        }

        // Only intercept absolute app paths (start with /)
        if (!href.startsWith('/')) {
            return;
        }

        // Validate that the link is for the current job (security: prevent cross-job navigation from API)
        const currentJobPath = this.jobPath();
        if (currentJobPath && !href.startsWith(`/${currentJobPath}/`)) {
            // Link is for a different job or unsafe path - allow default behavior
            return;
        }

        // Prevent default and navigate via Angular Router
        event.preventDefault();
        event.stopPropagation();

        this.router.navigateByUrl(href).then(() => {
        }).catch(err => {
            // Fallback to direct navigation if router fails
            window.location.href = href;
        });
    }
}
