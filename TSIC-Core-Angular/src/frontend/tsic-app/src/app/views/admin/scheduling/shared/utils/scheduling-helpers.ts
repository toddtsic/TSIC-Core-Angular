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

/** Format date for display: "Sat, Feb 15" */
export function formatDate(gDate: string | Date): string {
    const d = new Date(gDate);
    return d.toLocaleDateString('en-US', {
        weekday: 'short',
        month: 'short',
        day: 'numeric'
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

/** Format full date+time: "Sat, Feb 15, 8:00 AM" */
export function formatTime(gDate: string | Date): string {
    const d = new Date(gDate);
    return d.toLocaleString('en-US', {
        weekday: 'short',
        month: 'short',
        day: 'numeric',
        hour: 'numeric',
        minute: '2-digit'
    });
}

/** Format team designator: pool play → "2", bracket → "Y1", "S4", "F1", etc. */
export function teamDes(type: string, num: number | undefined | null): string {
    if (num == null) return type;
    return type === 'T' ? `${num}` : `${type}${num}`;
}

/** Sum team counts across all divisions in an agegroup. */
export function agTeamCount(ag: Pick<AgegroupWithDivisionsDto, 'divisions'>): number {
    return ag.divisions.reduce((sum, d) => sum + d.teamCount, 0);
}

/** Format game day for filter chips: "Sat, Feb 15" (same as formatDate). */
export function formatGameDay(iso: string): string {
    return formatDate(iso);
}
