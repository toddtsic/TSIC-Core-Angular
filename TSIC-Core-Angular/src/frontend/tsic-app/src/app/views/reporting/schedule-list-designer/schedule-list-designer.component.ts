import {
    ChangeDetectionStrategy, Component, computed, inject, OnInit, signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { DragDropModule, CdkDragDrop, moveItemInArray } from '@angular/cdk/drag-drop';
import { ToastService } from '@shared-ui/toast.service';
import { ScheduleListService } from '@infrastructure/services/schedule-list.service';
import { ReportingService } from '@infrastructure/services/reporting.service';
import type { ScheduleListFieldDto, ScheduleListRequestDto } from '@core/api';

type GroupBy = 'None' | 'Day' | 'Field' | 'AgeGroup' | 'Division';
type SortBy = 'Time' | 'Field' | 'Team';
type ScoreMode = 'Printed' | 'Blank' | 'Hidden';
type Preset = 'master' | 'byDay' | 'fieldUtil' | 'scoreSheet' | 'flat';

/** Working column model — a chosen field plus its placement/render options. */
interface DesignerColumn {
    key: string;
    label: string;
    widthWeight: number;
    align: string;
    supportsLongText: boolean;
    longText: 'Truncate' | 'Wrap';
    truncateAt: number | null;
    isScore: boolean;
}

/** One representative game row used only to fill the live preview. */
interface SampleGame {
    date: string;
    time: string;
    field: string;
    agegroup: string;
    division: string;
    league: string;
    home: string;
    homeScore: string;
    away: string;
    awayScore: string;
    homeRep: string;
    awayRep: string;
}

/** A resolved preview cell — text already projected through the same rules the PDF uses. */
interface PreviewCell {
    text: string;
    widthPct: number;
    align: string;
    wrap: boolean;
    box: boolean;   // blank write-in score box
}

const SAMPLE_COLOR = '#2E86C1';

const SAMPLE_GAMES: readonly SampleGame[] = [
    { date: '9/13/2025', time: '8:00 AM',  field: 'Reach 11 — Field 3A', agegroup: 'U16 Boys', division: '1', league: 'Premier', home: 'Arizona Thunder FC',  homeScore: '2', away: 'Desert Storm SC',     awayScore: '1', homeRep: 'Jamie Smith', awayRep: 'Pat Rivera' },
    { date: '9/13/2025', time: '9:30 AM',  field: 'Reach 11 — Field 3A', agegroup: 'U16 Boys', division: '1', league: 'Premier', home: 'Phoenix Rising Youth', homeScore: '0', away: 'Tucson United',        awayScore: '0', homeRep: 'Chris Vega',  awayRep: 'Sam Cole' },
    { date: '9/13/2025', time: '11:00 AM', field: 'Reach 11 — Field 3A', agegroup: 'U16 Boys', division: '1', league: 'Premier', home: 'Scottsdale Blackhawks', homeScore: '3', away: 'Mesa FC',             awayScore: '2', homeRep: 'Alex Reed',   awayRep: 'Jordan Fox' },
    { date: '9/13/2025', time: '12:30 PM', field: 'Reach 11 — Field 3A', agegroup: 'U16 Boys', division: '1', league: 'Premier', home: 'Gilbert Galaxy',       homeScore: '1', away: 'Chandler SC',         awayScore: '4', homeRep: 'Morgan Lee',  awayRep: 'Casey Day' },
];

/**
 * Schedule List Designer — director-built replacement for the canned Schedule_ExportExcel
 * report family (Master / By Day / Field Utilization / flat) plus the blank Score Entry
 * sheet. Pick + order + size columns, choose grouping / sort / score mode, then generate
 * the PDF in-process. The retired reports survive as the starter presets.
 */
@Component({
    selector: 'app-schedule-list-designer',
    standalone: true,
    imports: [CommonModule, DragDropModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './schedule-list-designer.component.html',
    styleUrl: './schedule-list-designer.component.scss',
})
export class ScheduleListDesignerComponent implements OnInit {
    private readonly scheduleSvc = inject(ScheduleListService);
    private readonly reportingSvc = inject(ReportingService);
    private readonly toast = inject(ToastService);

    readonly availableFields = signal<ScheduleListFieldDto[]>([]);
    readonly selectedColumns = signal<DesignerColumn[]>([]);

    readonly groupBy = signal<GroupBy>('Division');
    readonly sortBy = signal<SortBy>('Time');
    readonly scoreMode = signal<ScoreMode>('Printed');
    readonly pageBreakPerGroup = signal(false);
    readonly colorAccent = signal(true);

    /** Which starter preset is active. Radio-style: exactly one is always selected. */
    readonly activePreset = signal<Preset>('master');

    readonly isLoading = signal(true);
    readonly isGenerating = signal(false);

    readonly groupByOptions: readonly { value: GroupBy; label: string }[] = [
        { value: 'None', label: 'None (flat)' },
        { value: 'Day', label: 'Day' },
        { value: 'Field', label: 'Field' },
        { value: 'AgeGroup', label: 'Age Group' },
        { value: 'Division', label: 'Division' },
    ];
    readonly sortByOptions: readonly { value: SortBy; label: string }[] = [
        { value: 'Time', label: 'Time' },
        { value: 'Field', label: 'Field' },
        { value: 'Team', label: 'Team' },
    ];
    readonly scoreModeOptions: readonly { value: ScoreMode; label: string }[] = [
        { value: 'Printed', label: 'Printed scores' },
        { value: 'Blank', label: 'Blank write-in boxes' },
        { value: 'Hidden', label: 'Hidden' },
    ];

    readonly canGenerate = computed(() => this.selectedColumns().length > 0 && !this.isGenerating());
    readonly selectedKeys = computed(() => new Set(this.selectedColumns().map(c => c.key)));

    // ── Live preview ──
    readonly previewGroupLabel = computed(() => {
        const g = SAMPLE_GAMES[0];
        switch (this.groupBy()) {
            case 'Day': return 'Saturday, September 13, 2025';
            case 'Field': return g.field;
            case 'AgeGroup': return g.agegroup;
            case 'Division': return `${g.agegroup} — Div ${g.division}`;
            default: return '';
        }
    });

    /** Columns visible in the preview/output — score columns drop when scores are hidden. */
    readonly previewColumns = computed(() => {
        const hidden = this.scoreMode() === 'Hidden';
        return this.selectedColumns().filter(c => !(hidden && c.isScore));
    });

    readonly previewHeaders = computed(() => {
        const cols = this.previewColumns();
        const sumW = cols.reduce((s, c) => s + Math.max(1, c.widthWeight), 0) || 1;
        return cols.map(c => ({
            label: c.label,
            widthPct: (Math.max(1, c.widthWeight) / sumW) * 100,
            align: c.align.toLowerCase(),
        }));
    });

    readonly previewRows = computed<{ cells: PreviewCell[] }[]>(() => {
        const cols = this.previewColumns();
        const sumW = cols.reduce((s, c) => s + Math.max(1, c.widthWeight), 0) || 1;
        const mode = this.scoreMode();

        return SAMPLE_GAMES.map(g => ({
            cells: cols.map((c): PreviewCell => {
                const box = c.isScore;   // score cells always render as a box (hidden cols already filtered)
                let text = this.resolveCell(g, c.key, mode);   // value in Printed, '' in Blank → empty box
                if (!box && c.longText === 'Truncate') {
                    const at = c.truncateAt ?? 28;
                    if (at > 0 && text.length > at) text = text.slice(0, at);
                }
                return {
                    text,
                    widthPct: (Math.max(1, c.widthWeight) / sumW) * 100,
                    align: c.align.toLowerCase(),
                    wrap: c.longText === 'Wrap',
                    box,
                };
            }),
        }));
    });

    private resolveCell(g: SampleGame, key: string, mode: ScoreMode): string {
        switch (key) {
            case 'date': return g.date;
            case 'time': {
                // Mirror the PDF's "ddd M/d  h:mm tt" so a row shows its day, not just the time.
                const d = new Date(g.date);
                const dow = isNaN(d.getTime()) ? '' : `${d.toLocaleDateString('en-US', { weekday: 'short' })} `;
                return `${dow}${g.date.replace(/\/\d{4}$/, '')}  ${g.time}`;
            }
            case 'field': return g.field;
            case 'agegroup': return g.agegroup;
            case 'division': return g.division;
            case 'league': return g.league;
            case 'home': return g.home;
            case 'away': return g.away;
            case 'homeScore': return mode === 'Printed' ? g.homeScore : '';
            case 'awayScore': return mode === 'Printed' ? g.awayScore : '';
            case 'homeRep': return g.homeRep;
            case 'awayRep': return g.awayRep;
            default: return '';
        }
    }

    readonly accentColor = computed(() => this.colorAccent() ? SAMPLE_COLOR : null);

    ngOnInit(): void {
        this.scheduleSvc.getFields().subscribe({
            next: (fields) => {
                this.availableFields.set(fields);
                this.applyMaster(); // sensible default + proves the picker
                this.isLoading.set(false);
            },
            error: () => {
                this.isLoading.set(false);
                this.toast.show('Failed to load schedule fields', 'danger');
            },
        });
    }

    // ── Field selection ──

    isSelected(key: string): boolean {
        return this.selectedKeys().has(key);
    }

    toggleField(field: ScheduleListFieldDto): void {
        const cols = this.selectedColumns();
        if (cols.some(c => c.key === field.key)) {
            this.selectedColumns.set(cols.filter(c => c.key !== field.key));
        } else {
            // Adding a score column while Score Mode is Hidden would silently do
            // nothing — Hidden strips score columns from both the preview and the
            // PDF. Treat checking the column as intent to show scores: flip Hidden
            // -> Printed so the column the user just added actually renders.
            if (field.isScore && this.scoreMode() === 'Hidden') {
                this.scoreMode.set('Printed');
            }
            this.selectedColumns.set([...cols, this.buildColumn(field.key)]);
        }
    }

    updateColumn(key: string, patch: Partial<DesignerColumn>): void {
        this.selectedColumns.set(
            this.selectedColumns().map(c => (c.key === key ? { ...c, ...patch } : c)),
        );
    }

    onDropColumn(event: CdkDragDrop<DesignerColumn[]>): void {
        if (event.previousIndex === event.currentIndex) return;
        const cols = this.selectedColumns().slice();
        moveItemInArray(cols, event.previousIndex, event.currentIndex);
        this.selectedColumns.set(cols);
    }

    // ── Starter presets (the retired CR reports) ──

    applyMaster(): void {
        this.activePreset.set('master');
        this.setColumns(['time', 'field', 'home', 'homeScore', 'away', 'awayScore']);
        this.groupBy.set('Division');
        this.sortBy.set('Time');
        this.scoreMode.set('Printed');
        this.colorAccent.set(true);
        this.pageBreakPerGroup.set(false);
    }

    applyByDay(): void {
        this.activePreset.set('byDay');
        this.setColumns(['time', 'field', 'agegroup', 'home', 'away']);
        this.groupBy.set('Day');
        this.sortBy.set('Time');
        this.scoreMode.set('Hidden');
        this.colorAccent.set(false);
        this.pageBreakPerGroup.set(true);
    }

    applyFieldUtil(): void {
        this.activePreset.set('fieldUtil');
        this.setColumns(['time', 'agegroup', 'division', 'home', 'away']);
        this.groupBy.set('Field');
        this.sortBy.set('Time');
        this.scoreMode.set('Hidden');
        this.colorAccent.set(false);
        this.pageBreakPerGroup.set(true);
    }

    applyScoreSheet(): void {
        this.activePreset.set('scoreSheet');
        this.setColumns(['time', 'field', 'home', 'homeScore', 'away', 'awayScore']);
        this.groupBy.set('Division');
        this.sortBy.set('Time');
        this.scoreMode.set('Blank');
        this.colorAccent.set(true);
        this.pageBreakPerGroup.set(false);
    }

    applyFlat(): void {
        this.activePreset.set('flat');
        // 'time' now carries the date (ddd M/d h:mm tt), so a standalone date column is redundant.
        this.setColumns(['time', 'field', 'agegroup', 'home', 'homeScore', 'away', 'awayScore']);
        this.groupBy.set('None');
        this.sortBy.set('Time');
        this.scoreMode.set('Printed');
        this.colorAccent.set(false);
        this.pageBreakPerGroup.set(false);
    }

    // ── Generate ──

    generate(): void {
        if (!this.canGenerate()) return;

        const request: ScheduleListRequestDto = {
            groupBy: this.groupBy(),
            sortBy: this.sortBy(),
            columns: this.selectedColumns().map(c => ({
                key: c.key,
                widthWeight: c.widthWeight,
                align: c.align,
                longText: c.supportsLongText ? c.longText : 'Truncate',
                truncateAt: c.supportsLongText && c.longText === 'Truncate' ? c.truncateAt : null,
            })),
            scoreMode: this.scoreMode(),
            pageBreakPerGroup: this.pageBreakPerGroup(),
            colorAccent: this.colorAccent(),
        };

        this.isGenerating.set(true);
        this.scheduleSvc.generate(request).subscribe({
            next: (response) => {
                this.reportingSvc.triggerDownload(response, 'Schedule');
                this.isGenerating.set(false);
            },
            error: () => {
                this.isGenerating.set(false);
                this.toast.show('Failed to generate schedule', 'danger');
            },
        });
    }

    // ── Helpers ──

    setGroupBy(v: string): void { this.groupBy.set(v as GroupBy); }
    setSortBy(v: string): void { this.sortBy.set(v as SortBy); }
    setScoreMode(v: string): void { this.scoreMode.set(v as ScoreMode); }

    setChecked(setter: (v: boolean) => void, event: Event): void {
        setter((event.target as HTMLInputElement).checked);
    }

    private setColumns(keys: string[]): void {
        this.selectedColumns.set(keys.map(k => this.buildColumn(k)));
    }

    private buildColumn(key: string, overrides: Partial<DesignerColumn> = {}): DesignerColumn {
        const f = this.availableFields().find(x => x.key === key);
        return {
            key,
            label: f?.label ?? key,
            widthWeight: f?.defaultWidthWeight ?? 50,
            align: f?.defaultAlign ?? 'Left',
            supportsLongText: f?.supportsLongText ?? false,
            longText: 'Truncate',
            truncateAt: 28,
            isScore: f?.isScore ?? false,
            ...overrides,
        };
    }
}
