import { ChangeDetectionStrategy, Component, inject, computed, signal } from '@angular/core';
import { JobService } from '@infrastructure/services/job.service';
import { buildAssetUrl } from '@infrastructure/utils/asset-url.utils';

@Component({
    selector: 'app-client-banner',
    standalone: true,
    templateUrl: './client-banner.component.html',
    styleUrl: './client-banner.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
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
        let url = buildAssetUrl(bannerPath);
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
        return buildAssetUrl(backgroundPath);
    });

    // Simple validation for overlay image
    hasValidOverlayImage = computed(() => {
        const url = this.bannerImageUrl();
        const hasBackground = this.bannerBackgroundUrl();
        const isValid = this.overlayImageValid();

        if (!url || url === '' || url === 'undefined' || url === 'null') return false;
        if (!isValid) return false;

        // Overlay should only appear WITH background
        return !!hasBackground && !!url;
    });

    // Check image dimensions after load - hide if too small (likely placeholder)
    onOverlayImageLoad(event: Event) {
        const img = event.target as HTMLImageElement;
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
