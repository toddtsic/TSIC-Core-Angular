import {
    ChangeDetectionStrategy, Component, computed, inject, OnInit, signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { DragDropModule, CdkDragDrop, moveItemInArray } from '@angular/cdk/drag-drop';
import { ToastService } from '@shared-ui/toast.service';
import { RosterTableService } from '@infrastructure/services/roster-table.service';
import { ReportingService } from '@infrastructure/services/reporting.service';
import type { RosterTableFieldDto, RosterTableRequestDto } from '@core/api';

type GroupBy = 'None' | 'AgeGroup' | 'Division' | 'Team' | 'Club' | 'School'
    | 'DayGroup' | 'NightGroup' | 'Roommate';
type SortBy = 'Name' | 'Uniform' | 'School' | 'GradYear';
type Orientation = 'Portrait' | 'Landscape';
type Preset = 'club' | 'noMedical' | 'coaches' | 'withClubRep' | 'steps' | 'recruiting' | 'camp';

/** Working column model — a chosen field plus its placement/render options. */
interface DesignerColumn {
    key: string;
    label: string;
    widthWeight: number;
    align: string;
    supportsLongText: boolean;
    longText: 'Truncate' | 'Wrap';
    truncateAt: number | null;
}

/** A resolved preview cell — text already projected through the truncation the PDF uses. */
interface PreviewCell {
    text: string;
    widthPct: number;
    align: string;
    wrap: boolean;
}

const SAMPLE_COLOR = '#7D3C98';

/** Fields shared across the preview roster (same team/club) — varied per player below. */
const SAMPLE_BASE = {
    team: 'Arizona Storm 2026',
    agegroup: 'U16 Girls',
    division: '1',
    league: 'Premier',
    club: 'Arizona Storm LC',
    clubRep: 'Karen Brooks',
    clubRepEmail: 'karen.brooks@azstorm.example',
    clubRepPhone: '480-555-0190',
};

/**
 * Representative roster rows used only to fill the live preview. Each is a flat
 * field-key → already-shaped-display-text map (mirrors the server ResolveCell output:
 * "Last, First", money "0.00", phone xxx-xxx-xxxx, SAT summed). Preview only truncates.
 */
const SAMPLE_PLAYERS: readonly Record<string, string>[] = [
    {
        ...SAMPLE_BASE, player: 'Anderson, Maya', uniform: '12', position: 'M', gender: 'F',
        dob: '3/14/2010', gradYear: '2028', schoolGrade: '10', school: 'Desert Vista HS',
        gpa: '3.8', sat: '1340', act: '29', email: 'maya.anderson@example.com', phone: '480-555-0142',
        address: '1425 E Vista Dr, Phoenix, AZ 85048', momName: 'Jennifer Anderson',
        momPhone: '480-555-0143', momEmail: 'jen.a@example.com', dadName: 'Robert Anderson',
        dadPhone: '480-555-0144', dadEmail: 'rob.a@example.com',
        medical: 'Mild peanut allergy — carries EpiPen', paid: '450.00', owed: '0.00',
        jersey: 'M', shorts: 'M', kilt: '12', tshirt: 'M', reversible: 'M', gloves: 'S',
        shoes: '8', uslax: 'US123456', dayGroup: 'A', nightGroup: 'Cabin 1', roommate: 'Brooks, Olivia',
    },
    {
        ...SAMPLE_BASE, player: 'Brooks, Olivia', uniform: '7', position: 'A', gender: 'F',
        dob: '7/2/2010', gradYear: '2028', schoolGrade: '10', school: 'Hamilton HS',
        gpa: '4.0', sat: '1410', act: '31', email: 'olivia.brooks@example.com', phone: '480-555-0151',
        address: '88 W Ranch Rd, Chandler, AZ 85225', momName: 'Karen Brooks',
        momPhone: '480-555-0152', momEmail: 'karen.b@example.com', dadName: 'David Brooks',
        dadPhone: '480-555-0153', dadEmail: 'dave.b@example.com',
        medical: '', paid: '450.00', owed: '0.00',
        jersey: 'S', shorts: 'S', kilt: '10', tshirt: 'S', reversible: 'S', gloves: 'S',
        shoes: '7', uslax: 'US123457', dayGroup: 'A', nightGroup: 'Cabin 1', roommate: 'Anderson, Maya',
    },
    {
        ...SAMPLE_BASE, player: 'Carter, Sophia', uniform: '23', position: 'D', gender: 'F',
        dob: '11/19/2009', gradYear: '2027', schoolGrade: '11', school: 'Desert Vista HS',
        gpa: '3.6', sat: '1280', act: '27', email: 'sophia.carter@example.com', phone: '602-555-0177',
        address: '2210 S Lake Ct, Tempe, AZ 85282', momName: 'Lisa Carter',
        momPhone: '602-555-0178', momEmail: 'lisa.c@example.com', dadName: 'Mark Carter',
        dadPhone: '602-555-0179', dadEmail: 'mark.c@example.com',
        medical: 'Asthma — inhaler in bag', paid: '225.00', owed: '225.00',
        jersey: 'L', shorts: 'L', kilt: '14', tshirt: 'L', reversible: 'L', gloves: 'M',
        shoes: '9', uslax: 'US123458', dayGroup: 'B', nightGroup: 'Cabin 2', roommate: 'Diaz, Emma',
    },
    {
        ...SAMPLE_BASE, player: 'Diaz, Emma', uniform: '4', position: 'G', gender: 'F',
        dob: '1/8/2010', gradYear: '2028', schoolGrade: '10', school: 'Mountain Pointe HS',
        gpa: '3.9', sat: '1360', act: '30', email: 'emma.diaz@example.com', phone: '480-555-0188',
        address: '540 N Palm Ln, Mesa, AZ 85201', momName: 'Maria Diaz',
        momPhone: '480-555-0189', momEmail: 'maria.d@example.com', dadName: 'Carlos Diaz',
        dadPhone: '480-555-0190', dadEmail: 'carlos.d@example.com',
        medical: '', paid: '450.00', owed: '0.00',
        jersey: 'M', shorts: 'M', kilt: '12', tshirt: 'M', reversible: 'M', gloves: 'S',
        shoes: '8', uslax: 'US123459', dayGroup: 'B', nightGroup: 'Cabin 2', roommate: 'Carter, Sophia',
    },
];

/**
 * Roster Table Designer — director-built replacement for the wide-roster Crystal family
 * (Club Rosters, No-Medical, Coaches, WithClubRep, STEPS, Recruiting roster). Pick + order +
 * size columns, choose grouping / sort / orientation / players-only, then generate the PDF
 * in-process. Each retired report survives as a starter preset. Sibling of the Schedule List
 * Designer (same table engine, no scores).
 */
@Component({
    selector: 'app-roster-table-designer',
    standalone: true,
    imports: [CommonModule, DragDropModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './roster-table-designer.component.html',
    styleUrl: './roster-table-designer.component.scss',
})
export class RosterTableDesignerComponent implements OnInit {
    private readonly rosterSvc = inject(RosterTableService);
    private readonly reportingSvc = inject(ReportingService);
    private readonly toast = inject(ToastService);
    private readonly route = inject(ActivatedRoute);

    readonly availableFields = signal<RosterTableFieldDto[]>([]);
    readonly selectedColumns = signal<DesignerColumn[]>([]);

    readonly groupBy = signal<GroupBy>('Team');
    readonly sortBy = signal<SortBy>('Name');
    readonly orientation = signal<Orientation>('Landscape');
    readonly playersOnly = signal(false);
    readonly pageBreakPerGroup = signal(true);
    readonly colorAccent = signal(true);

    /** Which starter preset is active. Radio-style: exactly one is always selected. */
    readonly activePreset = signal<Preset>('club');

    readonly isLoading = signal(true);
    readonly isGenerating = signal(false);

    readonly groupByOptions: readonly { value: GroupBy; label: string }[] = [
        { value: 'None', label: 'None (flat)' },
        { value: 'AgeGroup', label: 'Age Group' },
        { value: 'Division', label: 'Division' },
        { value: 'Team', label: 'Team' },
        { value: 'Club', label: 'Club' },
        { value: 'School', label: 'School' },
        { value: 'DayGroup', label: 'Day Group (camp)' },
        { value: 'NightGroup', label: 'Night Group (camp)' },
        { value: 'Roommate', label: 'Roommate (camp)' },
    ];
    readonly sortByOptions: readonly { value: SortBy; label: string }[] = [
        { value: 'Name', label: 'Name' },
        { value: 'Uniform', label: 'Uniform #' },
        { value: 'School', label: 'School' },
        { value: 'GradYear', label: 'Grad Year' },
    ];
    readonly orientationOptions: readonly { value: Orientation; label: string }[] = [
        { value: 'Portrait', label: 'Portrait' },
        { value: 'Landscape', label: 'Landscape' },
    ];

    readonly canGenerate = computed(() => this.selectedColumns().length > 0 && !this.isGenerating());
    readonly selectedKeys = computed(() => new Set(this.selectedColumns().map(c => c.key)));

    // ── Live preview ──
    readonly previewGroupLabel = computed(() => {
        const p = SAMPLE_PLAYERS[0];
        switch (this.groupBy()) {
            case 'AgeGroup': return p['agegroup'];
            case 'Division': return `${p['agegroup']} — Div ${p['division']}`;
            case 'Team': return `${p['agegroup']} Div ${p['division']} ${p['team']}`;
            case 'Club': return p['club'];
            case 'School': return p['school'];
            case 'DayGroup': return `Day Group: ${p['dayGroup']}`;
            case 'NightGroup': return `Night Group: ${p['nightGroup']}`;
            case 'Roommate': return `Roommate: ${p['roommate']}`;
            default: return '';
        }
    });

    readonly previewColumns = computed(() => this.selectedColumns());

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

        return SAMPLE_PLAYERS.map(p => ({
            cells: cols.map((c): PreviewCell => {
                let text = p[c.key] ?? '';
                if (c.longText !== 'Wrap' && text.length > 0) {
                    const at = c.supportsLongText && c.longText === 'Truncate' ? (c.truncateAt ?? 28) : 0;
                    if (at > 0 && text.length > at) text = text.slice(0, at);
                }
                return {
                    text,
                    widthPct: (Math.max(1, c.widthWeight) / sumW) * 100,
                    align: c.align.toLowerCase(),
                    wrap: c.supportsLongText && c.longText === 'Wrap',
                };
            }),
        }));
    });

    readonly accentColor = computed(() => this.colorAccent() ? SAMPLE_COLOR : null);

    ngOnInit(): void {
        // The camp catalog tile deep-links here with data.mode = 'camp'.
        const isCamp = this.route.snapshot.data['mode'] === 'camp';
        this.rosterSvc.getFields().subscribe({
            next: (fields) => {
                this.availableFields.set(fields);
                // sensible default + proves the picker (camp tile opens on the camp preset)
                if (isCamp) { this.applyCamp(); } else { this.applyClubRoster(); }
                this.isLoading.set(false);
            },
            error: () => {
                this.isLoading.set(false);
                this.toast.show('Failed to load roster fields', 'danger');
            },
        });
    }

    // ── Field selection ──

    isSelected(key: string): boolean {
        return this.selectedKeys().has(key);
    }

    toggleField(field: RosterTableFieldDto): void {
        const cols = this.selectedColumns();
        if (cols.some(c => c.key === field.key)) {
            this.selectedColumns.set(cols.filter(c => c.key !== field.key));
        } else {
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

    applyClubRoster(): void {
        this.activePreset.set('club');
        this.setColumns(['player', 'uniform', 'position', 'dob', 'phone', 'email', 'address',
            'momName', 'momPhone', 'dadName', 'dadPhone', 'medical', 'paid', 'owed']);
        this.groupBy.set('Team');
        this.sortBy.set('Name');
        this.orientation.set('Landscape');
        this.playersOnly.set(false);
        this.pageBreakPerGroup.set(true);
        this.colorAccent.set(true);
    }

    applyNoMedical(): void {
        this.activePreset.set('noMedical');
        this.setColumns(['player', 'uniform', 'position', 'dob', 'phone', 'email', 'address',
            'momName', 'momPhone', 'dadName', 'dadPhone', 'paid', 'owed']);
        this.groupBy.set('Team');
        this.sortBy.set('Name');
        this.orientation.set('Landscape');
        this.playersOnly.set(false);
        this.pageBreakPerGroup.set(true);
        this.colorAccent.set(true);
    }

    applyCoaches(): void {
        this.activePreset.set('coaches');
        this.setColumns(['player', 'uniform', 'position', 'clubRep', 'clubRepPhone']);
        this.groupBy.set('Team');
        this.sortBy.set('Uniform');
        this.orientation.set('Portrait');
        this.playersOnly.set(true);
        this.pageBreakPerGroup.set(true);
        this.colorAccent.set(true);
    }

    applyWithClubRep(): void {
        this.activePreset.set('withClubRep');
        this.setColumns(['player', 'team', 'agegroup', 'clubRep', 'clubRepEmail', 'clubRepPhone']);
        this.groupBy.set('Club');
        this.sortBy.set('Name');
        this.orientation.set('Landscape');
        this.playersOnly.set(true);
        this.pageBreakPerGroup.set(false);
        this.colorAccent.set(false);
    }

    applySteps(): void {
        this.activePreset.set('steps');
        this.setColumns(['player', 'gender', 'dob', 'gradYear', 'school', 'position', 'uslax',
            'jersey', 'shorts', 'kilt', 'tshirt', 'reversible', 'gloves', 'paid', 'owed']);
        this.groupBy.set('Team');
        this.sortBy.set('Name');
        this.orientation.set('Landscape');
        this.playersOnly.set(true);
        this.pageBreakPerGroup.set(true);
        this.colorAccent.set(true);
    }

    applyRecruiting(): void {
        this.activePreset.set('recruiting');
        this.setColumns(['player', 'uniform', 'position', 'gradYear', 'school', 'gpa', 'sat', 'act',
            'phone', 'email', 'club', 'clubRep']);
        this.groupBy.set('AgeGroup');
        this.sortBy.set('GradYear');
        this.orientation.set('Landscape');
        this.playersOnly.set(true);
        this.pageBreakPerGroup.set(false);
        this.colorAccent.set(true);
    }

    /**
     * Camp Groups — campers grouped by day group, with their night group + roommate. Covers the
     * camp_daygroups / camp_nightgroups / camp_roomies Crystal family; flip Group by to Night
     * Group or Roommate for the siblings. Opened by the camp catalog tile (route data.mode).
     */
    applyCamp(): void {
        this.activePreset.set('camp');
        this.setColumns(['player', 'team', 'position', 'nightGroup', 'roommate']);
        this.groupBy.set('DayGroup');
        this.sortBy.set('Name');
        this.orientation.set('Portrait');
        this.playersOnly.set(true);
        this.pageBreakPerGroup.set(true);
        this.colorAccent.set(true);
    }

    // ── Generate ──

    generate(): void {
        if (!this.canGenerate()) return;

        const request: RosterTableRequestDto = {
            groupBy: this.groupBy(),
            sortBy: this.sortBy(),
            columns: this.selectedColumns().map(c => ({
                key: c.key,
                widthWeight: c.widthWeight,
                align: c.align,
                longText: c.supportsLongText ? c.longText : 'Truncate',
                truncateAt: c.supportsLongText && c.longText === 'Truncate' ? c.truncateAt : null,
            })),
            orientation: this.orientation(),
            playersOnly: this.playersOnly(),
            pageBreakPerGroup: this.pageBreakPerGroup(),
            colorAccent: this.colorAccent(),
        };

        this.isGenerating.set(true);
        this.rosterSvc.generate(request).subscribe({
            next: (response) => {
                this.reportingSvc.triggerDownload(response, 'Roster');
                this.isGenerating.set(false);
            },
            error: () => {
                this.isGenerating.set(false);
                this.toast.show('Failed to generate roster', 'danger');
            },
        });
    }

    // ── Helpers ──

    setGroupBy(v: string): void { this.groupBy.set(v as GroupBy); }
    setSortBy(v: string): void { this.sortBy.set(v as SortBy); }
    setOrientation(v: string): void { this.orientation.set(v as Orientation); }

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
            ...overrides,
        };
    }
}
