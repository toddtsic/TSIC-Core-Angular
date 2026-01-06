import { Component, inject, computed, signal } from '@angular/core';
import { JobService } from '@infrastructure/services/job.service';

@Component({
    selector: 'app-client-banner',
    standalone: true,
    templateUrl: './client-banner.component.html',
    styleUrl: './client-banner.component.scss'
})
export class ClientBannerComponent {
    private readonly jobService = inject(JobService);

    // Track if overlay image is valid (not a tiny placeholder)
    overlayImageValid = signal(true);

    // Computed properties for reactive job metadata
    job = computed(() => this.jobService.currentJob());
    jobName = computed(() => this.job()?.jobName || '');
    jobPath = computed(() => this.job()?.jobPath || '');
    jobBannerPath = computed(() => this.job()?.jobBannerPath || '');
    jobBannerBackgroundPath = computed(() => this.job()?.jobBannerBackgroundPath || '');
    jobBannerText1 = computed(() => this.job()?.jobBannerText1 || '');
    jobBannerText2 = computed(() => this.job()?.jobBannerText2 || '');

    // Decoded HTML text for proper rendering
    jobBannerText1Decoded = computed(() => this.decodeHtmlText(this.jobBannerText1()));
    jobBannerText2Decoded = computed(() => this.decodeHtmlText(this.jobBannerText2()));

    // Build banner image URL if available (foreground slide image)
    bannerImageUrl = computed(() => {
        const bannerPath = this.jobBannerPath();
        if (!bannerPath) return null;
        let url = this.buildAssetUrl(bannerPath);
        // Fallback: if PDF, try JPG version instead
        if (url && url.toLowerCase().endsWith('.pdf')) {
            url = url.slice(0, -4) + '.jpg';
        }
        return url;
    });

    // Build banner background image URL if available
    bannerBackgroundUrl = computed(() => {
        const backgroundPath = this.jobBannerBackgroundPath();
        if (!backgroundPath) return null;
        return this.buildAssetUrl(backgroundPath);
    });

    private buildAssetUrl(path?: string): string {
        if (!path) return '';
        const STATIC_BASE_URL = 'https://statics.teamsportsinfo.com/BannerFiles';
        const p = String(path).trim();
        if (!p || p === 'undefined' || p === 'null') return '';

        if (/^https?:\/\//i.test(p)) {
            return p.replace(/([^:])\/\/+/, '$1/');
        }

        const noLead = p.replace(/^\/+/, '');
        if (/^BannerFiles\//i.test(noLead)) {
            const rest = noLead.replace(/^BannerFiles\//i, '');
            return `${STATIC_BASE_URL}/${rest}`;
        }

        if (!/[.]/.test(noLead) && /^[a-z0-9-]{2,20}$/i.test(noLead)) {
            return '';
        }

        return `${STATIC_BASE_URL}/${noLead}`;
    }

    // Simple validation for overlay image
    hasValidOverlayImage = computed(() => {
        const url = this.bannerImageUrl();
        const hasBackground = this.bannerBackgroundUrl();
        const isValid = this.overlayImageValid();

        if (!url || url === '' || url === 'undefined' || url === 'null') return false;
        if (!isValid) return false; // Hide if image is too small

        // Overlay should only appear WITH background
        return !!hasBackground && !!url;
    });

    // Check image dimensions after load - hide if too small (likely placeholder)
    onOverlayImageLoad(event: Event) {
        const img = event.target as HTMLImageElement;
        // Hide images smaller than 150x150 (likely placeholders)
        if (img.naturalWidth < 150 || img.naturalHeight < 150) {
            this.overlayImageValid.set(false);
        } else {
            this.overlayImageValid.set(true);
        }
    }

    // Hide overlay image if it fails to load (broken link, 404, etc.)
    onOverlayImageError(event: Event) {
        this.overlayImageValid.set(false);
    }

    private decodeHtmlText(text: string): string {
        if (!text) return '';

        // Use browser's built-in HTML entity decoder
        const textarea = document.createElement('textarea');
        textarea.innerHTML = text;
        let decoded = textarea.value;

        // Clean up trailing <br> and &nbsp; 
        while (/<br\s*\/?>\s*$/i.test(decoded) || /&nbsp;\s*$/i.test(decoded)) {
            decoded = decoded.replace(/<br\s*\/?>\s*$/i, '').replace(/&nbsp;\s*$/i, '');
        }

        // Clean up multiple consecutive <br> tags
        decoded = decoded.replace(/(<br\s*\/?>\s*){2,}/gi, '<br>');

        return decoded.trim();
    }
}
