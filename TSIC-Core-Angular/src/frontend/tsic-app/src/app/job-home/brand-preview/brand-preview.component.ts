import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';

interface ColorPalette {
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

@Component({
    selector: 'app-brand-preview',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './brand-preview.component.html',
    styleUrls: ['./brand-preview.component.scss']
})
export class BrandPreviewComponent {
    selectedPalette = signal<number>(0);
    activeTab = signal<string>('demo');

    palettes: ColorPalette[] = [
        {
            name: 'Friendly Sky (Current)',
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
            description: 'Rich navy blues with coral accents - professional depth',
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
            description: 'Warm oranges and reds - energetic and bold',
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
            description: 'Natural greens with earth tones - calm and trustworthy',
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
            description: 'Rich purples with magenta - creative and luxurious',
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
            description: 'Soft pinks with deep rose - friendly and approachable',
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
            description: 'Dark teal with cyan - modern and sophisticated',
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
            description: 'Strong reds with charcoal - powerful and attention-grabbing',
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

    selectPalette(index: number): void {
        this.selectedPalette.set(index);
        const palette = this.palettes[index];

        // Helper to convert hex to RGB values
        const hexToRgb = (hex: string): string => {
            const result = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex);
            return result
                ? `${parseInt(result[1], 16)}, ${parseInt(result[2], 16)}, ${parseInt(result[3], 16)}`
                : '0, 0, 0';
        };

        // Apply palette to CSS variables (both hex and RGB variants)
        const root = document.documentElement;

        root.style.setProperty('--bs-primary', palette.primary);
        root.style.setProperty('--bs-primary-rgb', hexToRgb(palette.primary));

        root.style.setProperty('--bs-secondary', palette.secondary);
        root.style.setProperty('--bs-secondary-rgb', hexToRgb(palette.secondary));

        root.style.setProperty('--bs-success', palette.success);
        root.style.setProperty('--bs-success-rgb', hexToRgb(palette.success));

        root.style.setProperty('--bs-danger', palette.danger);
        root.style.setProperty('--bs-danger-rgb', hexToRgb(palette.danger));

        root.style.setProperty('--bs-warning', palette.warning);
        root.style.setProperty('--bs-warning-rgb', hexToRgb(palette.warning));

        root.style.setProperty('--bs-info', palette.info);
        root.style.setProperty('--bs-info-rgb', hexToRgb(palette.info));

        root.style.setProperty('--bs-light', palette.light);
        root.style.setProperty('--bs-light-rgb', hexToRgb(palette.light));

        root.style.setProperty('--bs-dark', palette.dark);
        root.style.setProperty('--bs-dark-rgb', hexToRgb(palette.dark));

        // Note: Page background is fixed to --neutral-50, not controlled by palette
        // Only update body text color
        root.style.setProperty('--bs-body-color', palette.bodyColor);
        root.style.setProperty('--bs-body-color-rgb', hexToRgb(palette.bodyColor));

        // Apply card background - update brand tokens so the entire design system responds
        root.style.setProperty('--brand-surface', palette.cardBg);
        root.style.setProperty('--brand-text', palette.bodyColor);
        root.style.setProperty('--brand-bg', palette.bodyBg);

        // Also set the resolved card variables directly to override any theme-specific rules
        root.style.setProperty('--bs-card-bg', palette.cardBg);
        root.style.setProperty('--bs-card-color', palette.bodyColor);

        // Update muted text color to be a lighter version of body color
        root.style.setProperty('--bs-secondary-color', palette.bodyColor);
        root.style.setProperty('--bs-secondary-color-rgb', hexToRgb(palette.bodyColor));
        root.style.setProperty('--bs-secondary-rgb', hexToRgb(palette.bodyColor));

        console.log('Applied palette:', palette.name, 'bg:', palette.bodyBg, 'card:', palette.cardBg);
    }

    selectTab(tab: string): void {
        this.activeTab.set(tab);
    }
}
