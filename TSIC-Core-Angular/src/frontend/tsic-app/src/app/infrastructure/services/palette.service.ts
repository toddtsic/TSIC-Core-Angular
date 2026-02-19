import { Injectable, signal, inject } from '@angular/core';
import { LocalStorageService } from './local-storage.service';
import { LocalStorageKey } from '../shared/local-storage.model';

export interface ColorPalette {
    name: string;
    description: string;
    primary: string;
    secondary: string;
    success: string;
    danger: string;
    warning: string;
    info: string;
    light: string;
    dark: string;
    bodyBg: string;
    bodyColor: string;
    cardBg: string;
}

@Injectable({ providedIn: 'root' })
export class PaletteService {
    private readonly localStorage = inject(LocalStorageService);

    readonly palettes: ColorPalette[] = [
        {
            name: 'Friendly Sky',
            description: 'Sky blue primary with warm orange accent',
            primary: '#0ea5e9',
            secondary: '#64748b',
            success: '#22c55e',
            danger: '#ef4444',
            warning: '#f59e0b',
            info: '#0ea5e9',
            light: '#f8fafc',
            dark: '#1e293b',
            bodyBg: '#ffffff',
            bodyColor: '#1e293b',
            cardBg: '#ffffff'
        },
        {
            name: 'Deep Ocean',
            description: 'Rich navy blues with coral accents',
            primary: '#1e40af',
            secondary: '#475569',
            success: '#059669',
            danger: '#f43f5e',
            warning: '#f59e0b',
            info: '#0284c7',
            light: '#e0f2fe',
            dark: '#0c4a6e',
            bodyBg: '#f0f9ff',
            bodyColor: '#0c4a6e',
            cardBg: '#ffffff'
        },
        {
            name: 'Sunset Warmth',
            description: 'Warm oranges and reds — energetic and bold',
            primary: '#ea580c',
            secondary: '#78716c',
            success: '#16a34a',
            danger: '#dc2626',
            warning: '#f59e0b',
            info: '#06b6d4',
            light: '#fff7ed',
            dark: '#7c2d12',
            bodyBg: '#fffbeb',
            bodyColor: '#78350f',
            cardBg: '#fff7ed'
        },
        {
            name: 'Forest Green',
            description: 'Natural greens with earth tones',
            primary: '#15803d',
            secondary: '#57534e',
            success: '#22c55e',
            danger: '#dc2626',
            warning: '#ca8a04',
            info: '#0891b2',
            light: '#f0fdf4',
            dark: '#14532d',
            bodyBg: '#f7fee7',
            bodyColor: '#365314',
            cardBg: '#f0fdf4'
        },
        {
            name: 'Royal Purple',
            description: 'Rich purples with magenta — creative and luxurious',
            primary: '#7c3aed',
            secondary: '#64748b',
            success: '#10b981',
            danger: '#f43f5e',
            warning: '#f59e0b',
            info: '#8b5cf6',
            light: '#faf5ff',
            dark: '#581c87',
            bodyBg: '#fdf4ff',
            bodyColor: '#581c87',
            cardBg: '#faf5ff'
        },
        {
            name: 'Cherry Blossom',
            description: 'Soft pinks with deep rose — friendly and approachable',
            primary: '#ec4899',
            secondary: '#64748b',
            success: '#10b981',
            danger: '#dc2626',
            warning: '#f59e0b',
            info: '#d946ef',
            light: '#fdf2f8',
            dark: '#831843',
            bodyBg: '#fef1f7',
            bodyColor: '#831843',
            cardBg: '#fdf2f8'
        },
        {
            name: 'Midnight Teal',
            description: 'Dark teal with cyan — modern and sophisticated',
            primary: '#0f766e',
            secondary: '#475569',
            success: '#14b8a6',
            danger: '#f43f5e',
            warning: '#f59e0b',
            info: '#06b6d4',
            light: '#f0fdfa',
            dark: '#134e4a',
            bodyBg: '#ecfeff',
            bodyColor: '#134e4a',
            cardBg: '#f0fdfa'
        },
        {
            name: 'Crimson Bold',
            description: 'Strong reds with charcoal — powerful and attention-grabbing',
            primary: '#dc2626',
            secondary: '#52525b',
            success: '#16a34a',
            danger: '#991b1b',
            warning: '#ea580c',
            info: '#3b82f6',
            light: '#fef2f2',
            dark: '#450a0a',
            bodyBg: '#fff5f5',
            bodyColor: '#450a0a',
            cardBg: '#fef2f2'
        }
    ];

    readonly selectedIndex = signal(0);

    constructor() {
        const saved = this.loadFromStorage();
        this.applyPalette(saved);
    }

    selectPalette(index: number): void {
        this.saveToStorage(index);
        this.applyPalette(index);
    }

    togglePalette(index: number): void {
        this.selectPalette(this.selectedIndex() === index ? 0 : index);
    }

    private applyPalette(index: number): void {
        this.selectedIndex.set(index);

        // Default palette (index 0) matches SCSS defaults — no injection needed.
        // This lets _theme-dark.scss dark-mode overrides work unopposed.
        if (index === 0) {
            document.getElementById('tsic-palette')?.remove();
            return;
        }

        const palette = this.palettes[index];

        const hexToRgb = (hex: string): string => {
            const result = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex);
            return result
                ? `${parseInt(result[1], 16)}, ${parseInt(result[2], 16)}, ${parseInt(result[3], 16)}`
                : '0, 0, 0';
        };

        /** Lighten a colour for dark-background readability (L → 70 %, S ≤ 50 %). */
        const forDark = (hex: string): string => {
            const rr = parseInt(hex.slice(1, 3), 16) / 255;
            const gg = parseInt(hex.slice(3, 5), 16) / 255;
            const bb = parseInt(hex.slice(5, 7), 16) / 255;
            const mx = Math.max(rr, gg, bb), mn = Math.min(rr, gg, bb);
            let h = 0, s = 0;
            if (mx !== mn) {
                const d = mx - mn;
                const l = (mx + mn) / 2;
                s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
                if (mx === rr) h = ((gg - bb) / d + (gg < bb ? 6 : 0)) / 6;
                else if (mx === gg) h = ((bb - rr) / d + 2) / 6;
                else h = ((rr - gg) / d + 4) / 6;
            }
            const H = h * 360, S = Math.min(s * 100, 50) / 100, L = 0.70;
            const a = S * Math.min(L, 1 - L);
            const f = (n: number) => {
                const k = (n + H / 30) % 12;
                const c = L - a * Math.max(Math.min(k - 3, 9 - k, 1), -1);
                return Math.round(255 * c).toString(16).padStart(2, '0');
            };
            return `#${f(0)}${f(8)}${f(4)}`;
        };

        // Pre-compute dark-mode accent colours (lightened for readability)
        const dp = forDark(palette.primary);
        const ds = forDark(palette.success);
        const dd = forDark(palette.danger);
        const dw = forDark(palette.warning);
        const di = forDark(palette.info);

        // Light mode: full palette (accents + tinted surfaces)
        // Dark mode: accent colours only (surfaces stay neutral for readability)
        //   - Text/links use lightened accents (L 70 %) for contrast on dark bg
        //   - .btn-primary uses original palette colour as bg (white text, ~5:1)
        const css = `:root:not([data-bs-theme='dark']) {
          --bs-primary: ${palette.primary};
          --bs-primary-rgb: ${hexToRgb(palette.primary)};
          --bs-secondary: ${palette.secondary};
          --bs-secondary-rgb: ${hexToRgb(palette.secondary)};
          --bs-success: ${palette.success};
          --bs-success-rgb: ${hexToRgb(palette.success)};
          --bs-danger: ${palette.danger};
          --bs-danger-rgb: ${hexToRgb(palette.danger)};
          --bs-warning: ${palette.warning};
          --bs-warning-rgb: ${hexToRgb(palette.warning)};
          --bs-info: ${palette.info};
          --bs-info-rgb: ${hexToRgb(palette.info)};
          --bs-light: ${palette.light};
          --bs-light-rgb: ${hexToRgb(palette.light)};
          --bs-dark: ${palette.dark};
          --bs-dark-rgb: ${hexToRgb(palette.dark)};
          --bs-body-color: ${palette.bodyColor};
          --bs-body-color-rgb: ${hexToRgb(palette.bodyColor)};
          --brand-surface: ${palette.cardBg};
          --brand-text: ${palette.bodyColor};
          --brand-bg: ${palette.bodyBg};
          --bs-card-bg: ${palette.cardBg};
          --bs-card-color: ${palette.bodyColor};
          --bs-secondary-color: ${palette.bodyColor};
          --bs-secondary-color-rgb: ${hexToRgb(palette.bodyColor)};
          --bs-secondary-rgb: ${hexToRgb(palette.bodyColor)};
        }
        [data-bs-theme='dark'] {
          --bs-primary: ${dp};
          --bs-primary-rgb: ${hexToRgb(dp)};
          --brand-primary: ${dp};
          --brand-primary-dark: ${palette.primary};
          --bs-success: ${ds};
          --bs-success-rgb: ${hexToRgb(ds)};
          --bs-danger: ${dd};
          --bs-danger-rgb: ${hexToRgb(dd)};
          --bs-warning: ${dw};
          --bs-warning-rgb: ${hexToRgb(dw)};
          --bs-info: ${di};
          --bs-info-rgb: ${hexToRgb(di)};
        }
        [data-bs-theme='dark'] .btn-primary {
          --bs-btn-bg: ${palette.primary};
          --bs-btn-border-color: ${palette.primary};
          --bs-btn-color: #ffffff;
          --bs-btn-hover-bg: ${palette.dark};
          --bs-btn-hover-border-color: ${palette.dark};
          --bs-btn-hover-color: #ffffff;
          --bs-btn-active-bg: ${palette.dark};
          --bs-btn-active-border-color: ${palette.dark};
          --bs-btn-active-color: #ffffff;
        }
        [data-bs-theme='dark'] .btn-outline-primary {
          --bs-btn-color: ${dp};
          --bs-btn-border-color: ${dp};
          --bs-btn-hover-bg: ${palette.primary};
          --bs-btn-hover-border-color: ${palette.primary};
          --bs-btn-hover-color: #ffffff;
          --bs-btn-active-bg: ${palette.dark};
          --bs-btn-active-border-color: ${palette.dark};
          --bs-btn-active-color: #ffffff;
        }`;

        let styleEl = document.getElementById('tsic-palette') as HTMLStyleElement | null;
        if (!styleEl) {
            styleEl = document.createElement('style');
            styleEl.id = 'tsic-palette';
            document.head.appendChild(styleEl);
        }
        styleEl.textContent = css;
    }

    private saveToStorage(index: number): void {
        this.localStorage.set(LocalStorageKey.SelectedPalette, index);
    }

    private loadFromStorage(): number {
        const saved = this.localStorage.getNumber(LocalStorageKey.SelectedPalette, 0);
        return saved >= 0 && saved < this.palettes.length ? saved : 0;
    }
}
