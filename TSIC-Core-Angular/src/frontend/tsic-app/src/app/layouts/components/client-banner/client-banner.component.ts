import { Component, inject, computed } from '@angular/core';
import { JobService } from '@infrastructure/services/job.service';

@Component({
    selector: 'app-client-banner',
    standalone: true,
    templateUrl: './client-banner.component.html',
    styleUrl: './client-banner.component.scss'
})
export class ClientBannerComponent {
    private readonly jobService = inject(JobService);

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
        return this.buildAssetUrl(bannerPath);
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

    private decodeHtmlText(text: string): string {
        if (!text) return '';
        
        let decoded = text;
        
        // Handle HTML entity encoding (like &lt;br&gt;)
        decoded = decoded
            .replace(/&lt;br&gt;/gi, '<br>')
            .replace(/&lt;br\/&gt;/gi, '<br>')
            .replace(/&lt;i&gt;/gi, '<i>')
            .replace(/&lt;\/i&gt;/gi, '</i>')
            .replace(/&lt;b&gt;/gi, '<b>')
            .replace(/&lt;\/b&gt;/gi, '</b>')
            .replace(/&lt;em&gt;/gi, '<em>')
            .replace(/&lt;\/em&gt;/gi, '</em>')
            .replace(/&lt;strong&gt;/gi, '<strong>')
            .replace(/&lt;\/strong&gt;/gi, '</strong>');
        
        // Also handle URL-encoded HTML as backup
        try {
            decoded = decodeURIComponent(decoded);
            decoded = decoded
                .replace(/%3Cbr%3E/gi, '<br>')
                .replace(/%3Cbr%2F%3E/gi, '<br>')
                .replace(/%3Ci%3E/gi, '<i>')
                .replace(/%3C%2Fi%3E/gi, '</i>')
                .replace(/%3Cb%3E/gi, '<b>')
                .replace(/%3C%2Fb%3E/gi, '</b>');
        } catch (e) {
            // If decodeURIComponent fails, just continue with HTML entity decoded version
        }
        
        return decoded;
    }
}
