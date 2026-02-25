import {
    ChangeDetectionStrategy, Component, computed, inject, signal
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import type { RefereeCalendarEventDto, RefereeSummaryDto } from '@core/api';
import { RefereeAssignmentService } from '../../../../infrastructure/services/referee-assignment.service';

type TabId = 'agenda' | 'grid';

interface DateGroup {
    dateKey: string;
    dateLabel: string;
    dayOfWeek: string;
    events: RefereeCalendarEventDto[];
}

interface GridCell {
    events: RefereeCalendarEventDto[];
}

@Component({
    selector: 'app-referee-calendar',
    standalone: true,
    imports: [CommonModule, FormsModule],
    templateUrl: './referee-calendar.component.html',
    styleUrl: './referee-calendar.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class RefereeCalendarComponent {
    private readonly refService = inject(RefereeAssignmentService);

    // ── State Signals ──
    events = signal<RefereeCalendarEventDto[]>([]);
    referees = signal<RefereeSummaryDto[]>([]);
    selectedRefereeId = signal<string>('');
    activeTab = signal<TabId>('agenda');
    isLoading = signal(false);
    errorMessage = signal('');

    // ── Computed: Filtered events ──
    filteredEvents = computed(() => {
        const all = this.events();
        const refId = this.selectedRefereeId();
        if (!refId) return all;
        return all.filter(e => e.refereeId === refId);
    });

    // ── Computed: Events grouped by date ──
    eventsByDate = computed<DateGroup[]>(() => {
        const filtered = this.filteredEvents();
        const grouped = new Map<string, RefereeCalendarEventDto[]>();

        for (const ev of filtered) {
            const d = new Date(ev.startTime);
            const dateKey = d.toISOString().split('T')[0];
            const existing = grouped.get(dateKey);
            if (existing) {
                existing.push(ev);
            } else {
                grouped.set(dateKey, [ev]);
            }
        }

        const result: DateGroup[] = [];
        for (const [dateKey, events] of grouped) {
            const d = new Date(dateKey + 'T12:00:00');
            events.sort((a, b) =>
                new Date(a.startTime).getTime() - new Date(b.startTime).getTime()
            );
            result.push({
                dateKey,
                dateLabel: d.toLocaleDateString('en-US', {
                    month: 'long', day: 'numeric', year: 'numeric'
                }),
                dayOfWeek: d.toLocaleDateString('en-US', { weekday: 'long' }),
                events
            });
        }

        result.sort((a, b) => a.dateKey.localeCompare(b.dateKey));
        return result;
    });

    // ── Computed: Distinct fields for grid columns ──
    distinctFields = computed(() => {
        const evts = this.filteredEvents();
        const fieldMap = new Map<string, string>();
        for (const e of evts) {
            if (e.fieldId && e.fieldName) {
                fieldMap.set(e.fieldId, e.fieldName);
            }
        }
        return Array.from(fieldMap.entries())
            .map(([id, name]) => ({ id, name }))
            .sort((a, b) => a.name.localeCompare(b.name));
    });

    // ── Computed: Distinct time slots for grid rows ──
    distinctTimes = computed(() => {
        const evts = this.filteredEvents();
        const times = new Set<string>();
        for (const e of evts) {
            const d = new Date(e.startTime);
            const timeKey = d.toLocaleTimeString('en-US', {
                hour: 'numeric', minute: '2-digit', hour12: true
            });
            times.add(timeKey);
        }
        return Array.from(times).sort((a, b) => {
            return this.parseTimeToMinutes(a) - this.parseTimeToMinutes(b);
        });
    });

    // ── Computed: Grid data (time -> field -> events) ──
    gridData = computed(() => {
        const evts = this.filteredEvents();
        const fields = this.distinctFields();
        const times = this.distinctTimes();

        const grid = new Map<string, Map<string, GridCell>>();

        for (const time of times) {
            const row = new Map<string, GridCell>();
            for (const field of fields) {
                row.set(field.id, { events: [] });
            }
            grid.set(time, row);
        }

        for (const e of evts) {
            if (!e.fieldId) continue;
            const d = new Date(e.startTime);
            const timeKey = d.toLocaleTimeString('en-US', {
                hour: 'numeric', minute: '2-digit', hour12: true
            });
            const row = grid.get(timeKey);
            if (row) {
                const cell = row.get(e.fieldId);
                if (cell) {
                    cell.events.push(e);
                }
            }
        }

        return grid;
    });

    // ── Computed: Summary stats ──
    totalAssignments = computed(() => this.filteredEvents().length);

    soloAssignments = computed(() =>
        this.filteredEvents().filter(e => !e.refsWith || e.refsWith.trim() === '').length
    );

    uniqueRefereeCount = computed(() => {
        const ids = new Set(this.filteredEvents().map(e => e.refereeId));
        return ids.size;
    });

    constructor() {
        this.loadData();
    }

    // ── Data Loading ──
    loadData(): void {
        this.isLoading.set(true);
        this.errorMessage.set('');

        forkJoin({
            events: this.refService.getCalendarEvents(),
            referees: this.refService.getReferees()
        }).subscribe({
            next: ({ events, referees }) => {
                this.events.set(events);
                this.referees.set(referees);
                this.isLoading.set(false);
            },
            error: (err) => {
                this.isLoading.set(false);
                this.errorMessage.set(
                    err?.error?.message || 'Failed to load referee calendar data.'
                );
            }
        });
    }

    // ── Tab Switching ──
    setTab(tab: TabId): void {
        this.activeTab.set(tab);
    }

    // ── Referee Filter ──
    onRefereeChange(refereeId: string): void {
        this.selectedRefereeId.set(refereeId);
    }

    // ── Grid Cell Lookup ──
    getGridCell(time: string, fieldId: string): GridCell {
        const row = this.gridData().get(time);
        return row?.get(fieldId) ?? { events: [] };
    }

    // ── Time Formatting ──
    formatTimeRange(startIso: string, endIso: string): string {
        const opts: Intl.DateTimeFormatOptions = {
            hour: 'numeric', minute: '2-digit', hour12: true
        };
        const start = new Date(startIso).toLocaleTimeString('en-US', opts);
        const end = new Date(endIso).toLocaleTimeString('en-US', opts);
        return `${start} - ${end}`;
    }

    formatTime(iso: string): string {
        return new Date(iso).toLocaleTimeString('en-US', {
            hour: 'numeric', minute: '2-digit', hour12: true
        });
    }

    // ── CSV Export ──
    exportCsv(): void {
        const evts = [...this.filteredEvents()].sort((a, b) => {
            const lastCmp = a.refereeLastName.localeCompare(b.refereeLastName);
            if (lastCmp !== 0) return lastCmp;
            const firstCmp = a.refereeFirstName.localeCompare(b.refereeFirstName);
            if (firstCmp !== 0) return firstCmp;
            return new Date(a.startTime).getTime() - new Date(b.startTime).getTime();
        });

        const header = 'Referee Last,Referee First,Date/Time,Field,Agegroup,Division,Team 1,Team 2,Refs With';
        const rows = evts.map(e => {
            const dt = new Date(e.startTime);
            const dateTime = dt.toLocaleDateString('en-US', {
                month: 'short', day: 'numeric', year: 'numeric'
            }) + ' ' + dt.toLocaleTimeString('en-US', {
                hour: 'numeric', minute: '2-digit', hour12: true
            });
            return [
                this.csvEscape(e.refereeLastName),
                this.csvEscape(e.refereeFirstName),
                this.csvEscape(dateTime),
                this.csvEscape(e.fieldName ?? ''),
                this.csvEscape(e.agegroupName ?? ''),
                this.csvEscape(e.divName ?? ''),
                this.csvEscape(e.team1 ?? ''),
                this.csvEscape(e.team2 ?? ''),
                this.csvEscape(e.refsWith ?? '')
            ].join(',');
        });

        const csv = [header, ...rows].join('\n');
        const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `referee-calendar-${new Date().toISOString().split('T')[0]}.csv`;
        link.click();
        URL.revokeObjectURL(url);
    }

    // ── Print / PDF Export ──
    exportPrint(): void {
        const evts = [...this.filteredEvents()].sort((a, b) => {
            const lastCmp = a.refereeLastName.localeCompare(b.refereeLastName);
            if (lastCmp !== 0) return lastCmp;
            const firstCmp = a.refereeFirstName.localeCompare(b.refereeFirstName);
            if (firstCmp !== 0) return firstCmp;
            return new Date(a.startTime).getTime() - new Date(b.startTime).getTime();
        });

        const refFilter = this.selectedRefereeId();
        const refName = refFilter
            ? this.referees().find(r => r.registrationId === refFilter)
            : null;
        const titleSuffix = refName
            ? ` - ${refName.firstName} ${refName.lastName}`
            : ' - All Referees';

        const rows = evts.map(e => {
            const dt = new Date(e.startTime);
            const dateStr = dt.toLocaleDateString('en-US', {
                weekday: 'short', month: 'short', day: 'numeric'
            });
            const timeStr = dt.toLocaleTimeString('en-US', {
                hour: 'numeric', minute: '2-digit', hour12: true
            });
            return `<tr>
                <td>${this.htmlEscape(e.refereeLastName)}, ${this.htmlEscape(e.refereeFirstName)}</td>
                <td>${this.htmlEscape(dateStr)}</td>
                <td>${this.htmlEscape(timeStr)}</td>
                <td>${this.htmlEscape(e.fieldName ?? '')}</td>
                <td>${this.htmlEscape(e.agegroupName ?? '')} ${this.htmlEscape(e.divName ?? '')}</td>
                <td>${this.htmlEscape(e.team1 ?? '')} vs ${this.htmlEscape(e.team2 ?? '')}</td>
                <td>${this.htmlEscape(e.refsWith ?? '')}</td>
            </tr>`;
        }).join('');

        const html = `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <title>Referee Calendar${this.htmlEscape(titleSuffix)}</title>
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; margin: 24px; color: #1c1917; }
        h1 { font-size: 18px; margin-bottom: 4px; }
        .subtitle { font-size: 12px; color: #78716c; margin-bottom: 16px; }
        table { width: 100%; border-collapse: collapse; font-size: 12px; }
        th { background: #f5f5f4; font-weight: 600; text-align: left; padding: 6px 8px; border-bottom: 2px solid #d6d3d1; }
        td { padding: 5px 8px; border-bottom: 1px solid #e7e5e4; }
        tr:nth-child(even) td { background: #fafaf9; }
        @media print {
            body { margin: 0; }
            table { page-break-inside: auto; }
            tr { page-break-inside: avoid; }
        }
    </style>
</head>
<body>
    <h1>Referee Calendar${this.htmlEscape(titleSuffix)}</h1>
    <div class="subtitle">Generated ${new Date().toLocaleDateString('en-US', { month: 'long', day: 'numeric', year: 'numeric' })} &bull; ${evts.length} assignments</div>
    <table>
        <thead>
            <tr>
                <th>Referee</th>
                <th>Date</th>
                <th>Time</th>
                <th>Field</th>
                <th>Agegroup / Division</th>
                <th>Matchup</th>
                <th>Refs With</th>
            </tr>
        </thead>
        <tbody>${rows}</tbody>
    </table>
</body>
</html>`;

        const printWindow = window.open('', '_blank');
        if (printWindow) {
            printWindow.document.write(html);
            printWindow.document.close();
            printWindow.focus();
            // Allow time for content to render before printing
            setTimeout(() => printWindow.print(), 300);
        }
    }

    // ── Helpers ──
    private csvEscape(value: string): string {
        if (value.includes(',') || value.includes('"') || value.includes('\n')) {
            return `"${value.replace(/"/g, '""')}"`;
        }
        return value;
    }

    private htmlEscape(value: string): string {
        return value
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    private parseTimeToMinutes(timeStr: string): number {
        const match = timeStr.match(/(\d+):(\d+)\s*(AM|PM)/i);
        if (!match) return 0;
        let hours = parseInt(match[1], 10);
        const minutes = parseInt(match[2], 10);
        const period = match[3].toUpperCase();
        if (period === 'PM' && hours !== 12) hours += 12;
        if (period === 'AM' && hours === 12) hours = 0;
        return hours * 60 + minutes;
    }
}
