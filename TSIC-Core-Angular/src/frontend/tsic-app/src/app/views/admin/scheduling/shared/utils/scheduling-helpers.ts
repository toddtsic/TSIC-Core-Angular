/**
 * Shared pure utility functions for all scheduling components.
 * Extracted from schedule-division, manage-pairings, and rescheduler.
 */

import type { AgegroupWithDivisionsDto } from '@core/api';

/** Returns '#fff' or '#000' for WCAG-compliant contrast against a hex background. */
export function contrastText(hex: string | null | undefined): string {
    if (!hex || hex.length < 7 || hex[0] !== '#') return 'var(--bs-secondary-color)';
    const r = parseInt(hex.slice(1, 3), 16);
    const g = parseInt(hex.slice(3, 5), 16);
    const b = parseInt(hex.slice(5, 7), 16);
    return (0.299 * r + 0.587 * g + 0.114 * b) / 255 > 0.55 ? '#000' : '#fff';
}

/** Returns an opaque tinted background: 12% agegroup color over solid body-bg. */
export function agBg(hex: string | null | undefined): string {
    if (!hex || hex.length < 7 || hex[0] !== '#')
        return 'var(--bs-body-bg)';
    const r = parseInt(hex.slice(1, 3), 16);
    const g = parseInt(hex.slice(3, 5), 16);
    const b = parseInt(hex.slice(5, 7), 16);
    return `linear-gradient(rgba(${r}, ${g}, ${b}, 0.12), rgba(${r}, ${g}, ${b}, 0.12)), var(--bs-body-bg)`;
}

/** Format date for display: "Sat, Feb 15, 2027" */
export function formatDate(gDate: string | Date): string {
    const d = new Date(gDate);
    return d.toLocaleDateString('en-US', {
        weekday: 'short',
        month: 'short',
        day: 'numeric',
        year: 'numeric'
    });
}

/** Format time only: "8:00 AM" */
export function formatTimeOnly(gDate: string | Date): string {
    const d = new Date(gDate);
    return d.toLocaleTimeString('en-US', {
        hour: 'numeric',
        minute: '2-digit'
    });
}

/** Format full date+time: "Sat, Feb 15, 2027, 8:00 AM" */
export function formatTime(gDate: string | Date): string {
    const d = new Date(gDate);
    return d.toLocaleString('en-US', {
        weekday: 'short',
        month: 'short',
        day: 'numeric',
        year: 'numeric',
        hour: 'numeric',
        minute: '2-digit'
    });
}

/** Format team designator: "T2", "Y1", "S4", "F1", etc. */
export function teamDes(type: string, num: number | undefined | null): string {
    if (num == null) return type;
    return `${type}${num}`;
}

/** Sum team counts across all divisions in an agegroup. */
export function agTeamCount(ag: Pick<AgegroupWithDivisionsDto, 'divisions'>): number {
    return ag.divisions.reduce((sum, d) => sum + d.teamCount, 0);
}

/** Format game day for filter chips: "Sat, Feb 15" (same as formatDate). */
export function formatGameDay(iso: string): string {
    return formatDate(iso);
}

/** Three-tier selection scope for the schedule-division page. */
export type ScheduleScope =
    | { level: 'event' }
    | { level: 'agegroup'; agegroupId: string }
    | { level: 'division'; agegroupId: string; divId: string };

/** Shared palette of named agegroup colors (used by LADT editor + scheduling navigator). */
export const AGEGROUP_COLORS = [
    { name: 'Red', value: '#FF0000' },
    { name: 'Blue', value: '#0000FF' },
    { name: 'Green', value: '#008000' },
    { name: 'Orange', value: '#FFA500' },
    { name: 'Purple', value: '#800080' },
    { name: 'Yellow', value: '#FFFF00' },
    { name: 'Teal', value: '#008080' },
    { name: 'Navy', value: '#000080' },
    { name: 'Maroon', value: '#800000' },
    { name: 'Lime', value: '#00FF00' },
    { name: 'Lawn Green', value: '#7CFC00' },
    { name: 'Aqua', value: '#00FFFF' },
    { name: 'Pale Turquoise', value: '#AFEEEE' },
    { name: 'Fuchsia', value: '#FF00FF' },
    { name: 'Pink', value: '#FFC0CB' },
    { name: 'Khaki', value: '#F0E68C' },
    { name: 'Silver', value: '#C0C0C0' },
    { name: 'Gray', value: '#808080' },
    { name: 'Black', value: '#000000' },
    { name: 'White', value: '#FFFFFF' },
    { name: 'Olive', value: '#808000' },
    { name: 'Coral', value: '#FF7F50' },
    { name: 'Crimson', value: '#DC143C' },
    { name: 'Dodger Blue', value: '#1E90FF' },
] as const;
