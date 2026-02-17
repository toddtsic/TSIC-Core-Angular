import { Injectable } from '@angular/core';

/**
 * Shared VerticalInsure dark-mode integration service.
 *
 * Handles all DOM-level dark-mode styling for the VI widget:
 * - Injecting computed CSS variable colors into VI's theme config
 * - Applying dark-mode overrides to VI's host container and iframe
 * - Walking the VI subtree to recolor white backgrounds and black text
 * - Maintaining a MutationObserver to reapply styling when VI updates its DOM
 *
 * Used by both player and team insurance services.
 */
@Injectable({ providedIn: 'root' })
export class ViDarkModeService {
    private viMutationObserver: MutationObserver | null = null;

    /**
     * Inject computed dark-mode colors into VI's offerData theme object.
     * Replaces CSS variable references with resolved hex values so VI's
     * iframe (which cannot access the host page's CSS variables) renders
     * with the correct palette.
     */
    injectDarkModeColors(offerData: any): void {
        if (!offerData?.theme) return;

        const style = globalThis.window.getComputedStyle(document.documentElement);
        const bgColor = style.getPropertyValue('--bs-body-bg').trim() || '#1c1917';
        const borderColor = style.getPropertyValue('--bs-border-color').trim() || '#57534e';
        const cardBg = style.getPropertyValue('--bs-card-bg').trim() || '#44403c';

        offerData.theme.colors = offerData.theme.colors || {};
        offerData.theme.colors.background = bgColor;
        offerData.theme.colors.border = borderColor;
        offerData.theme.colors.cardBackground = cardBg;
    }

    /**
     * Apply and maintain dark-mode styling to VI widget host container.
     * Sets up a MutationObserver to reapply styling when VI injects/updates nodes.
     */
    applyViDarkMode(hostSelector: string): void {
        const host = document.querySelector(hostSelector) as HTMLElement;
        if (!host) return;

        host.style.setProperty('background-color', 'var(--bs-body-bg)', 'important');
        host.style.setProperty('color', 'var(--bs-body-color)', 'important');

        // If VI rendered inside an iframe, force its surface to dark by applying a filter.
        const viFrame = host.querySelector('iframe');
        if (viFrame) {
            const bg = globalThis.window.getComputedStyle(document.documentElement).getPropertyValue('--bs-body-bg').trim();
            if (this.isDarkColor(bg)) {
                viFrame.style.setProperty('background-color', bg, 'important');
                viFrame.style.setProperty('border-color', 'var(--bs-border-color)', 'important');
                viFrame.style.setProperty('filter', 'invert(1) hue-rotate(180deg) contrast(0.95)', 'important');
            }
        }

        this.recolorViSubtree(host);

        // Attach MutationObserver if not already attached
        if (!this.viMutationObserver) {
            this.viMutationObserver = new MutationObserver(() => {
                this.recolorViSubtree(host);
            });
            this.viMutationObserver.observe(host, {
                childList: true,
                subtree: true,
                attributes: true,
                attributeFilter: ['style', 'class']
            });
        }
    }

    /**
     * Disconnect the MutationObserver. Call during service/component cleanup.
     */
    disconnect(): void {
        this.viMutationObserver?.disconnect();
        this.viMutationObserver = null;
    }

    /**
     * Recursively walk VI widget subtree and apply dark-mode colors to text/backgrounds.
     */
    private recolorViSubtree(root: HTMLElement): void {
        const walker = document.createTreeWalker(
            root,
            NodeFilter.SHOW_ELEMENT,
            null
        );

        let node: HTMLElement | null;
        while ((node = walker.nextNode() as HTMLElement)) {
            const bgColor = globalThis.window.getComputedStyle(node).backgroundColor;
            if (bgColor === 'rgb(255, 255, 255)' || bgColor === '#fff' || bgColor === '#ffffff') {
                node.style.setProperty('background-color', 'var(--bs-card-bg)', 'important');
            }
            const textColor = globalThis.window.getComputedStyle(node).color;
            if (textColor === 'rgb(0, 0, 0)' || textColor === '#000' || textColor === '#000000') {
                node.style.setProperty('color', 'var(--bs-body-color)', 'important');
            }
        }
    }

    /**
     * Determine whether a CSS color value is "dark" using relative luminance.
     * Supports both hex (#rrggbb) and rgb(r, g, b) formats.
     */
    isDarkColor(color: string): boolean {
        const hexMatch = /^#([0-9a-fA-F]{6})$/.exec(color);
        if (hexMatch) {
            const num = Number.parseInt(hexMatch[1], 16);
            const r = (num >> 16) & 0xff;
            const g = (num >> 8) & 0xff;
            const b = num & 0xff;
            return (0.2126 * r + 0.7152 * g + 0.0722 * b) < 140;
        }
        const rgbMatch = /rgb\((\d+),\s*(\d+),\s*(\d+)\)/i.exec(color);
        if (rgbMatch) {
            const r = Number(rgbMatch[1]);
            const g = Number(rgbMatch[2]);
            const b = Number(rgbMatch[3]);
            return (0.2126 * r + 0.7152 * g + 0.0722 * b) < 140;
        }
        return false;
    }
}
