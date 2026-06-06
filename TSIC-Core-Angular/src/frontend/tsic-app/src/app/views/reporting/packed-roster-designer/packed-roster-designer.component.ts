import {
    ChangeDetectionStrategy, Component, computed, inject, OnInit, signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { DragDropModule, CdkDragDrop, moveItemInArray } from '@angular/cdk/drag-drop';
import { ToastService } from '@shared-ui/toast.service';
import { PackedRosterService } from '@infrastructure/services/packed-roster.service';
import { ReportingService } from '@infrastructure/services/reporting.service';
import type { PackedRosterFieldDto, PackedRosterRequestDto } from '@core/api';

/** Which report this Designer is currently building. */
type ReportStyle = 'packed' | 'recruiter';

/** Within-card player order. */
type PackedSortBy = 'Uniform' | 'Position' | 'Name';

/** One representative recruiter card used only to fill the recruiter-mode preview. */
interface RecruiterSampleCard {
    uniformNo: string;
    name: string;
    gradYear: string;
    metrics: string;        // e.g. "GPA 4.0  ·  SAT 1480"
    email: string;
    address: string;
    phone: string;
    clubSchool: string;     // "CLUB / High School"
    collegeCommit: string;  // italic line; '' when uncommitted
}

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

/** One representative roster row used only to fill the live preview. */
interface SampleRow {
    isStaff: boolean;
    isCommitted: boolean;
    uniformNo: string;
    player: string;
    position: string;
    schoolName: string;
    gradYear: string;
    gpa: string;
    collegeCommit: string;
    club: string;       // player's own club — shown as "NAME / CLUB" when affiliation is on
    dayGroup: string;   // camp/showcase day-group label
}

/** A resolved preview cell — text already projected through the same rules the PDF uses. */
interface PreviewCell {
    text: string;
    widthPct: number;
    align: string;   // lowercased for CSS text-align
    bold: boolean;
    wrap: boolean;
}

// ── Preview geometry (points; 1:1 with PackedRosterPdfService constants) ─────────
// Scaled to px by PT so the schematic card matches the PDF's proportions exactly.
const PT = 1.6;                       // px per point in the preview
const CONTENT_W = 554.4;
const CARD_GAP = 3;
const CARD_OUTER_H = 314;
const HEADER_H = 14, REP_LINE_H = 12, UNDERLINE_Y = 26, ROSTER_TOP = 28, ROSTER_H = 282, BASE_ROW_H = 11;
const INSET_L = 4, INSET_R = 2;

/** Sample team that fills the preview — never sent to the backend. */
const SAMPLE_TEAM = {
    divisionTitle: 'U16 Boys — Gold',
    clubTeamName: 'Arizona Thunder FC',
    clubRepName: 'Jamie Smith',
    clubRepEmail: 'jsmith@azthunder.org',
    clubRepCellphone: '(602) 555-0142',
};

const SAMPLE_ROWS: readonly SampleRow[] = [
    { isStaff: true,  isCommitted: false, uniformNo: '', player: 'Jamie Smith',   position: '', schoolName: '(602) 555-0142', gradYear: '', gpa: '', collegeCommit: '', club: '', dayGroup: '' },
    { isStaff: true,  isCommitted: false, uniformNo: '', player: 'Pat Rivera',    position: '', schoolName: '(602) 555-0188', gradYear: '', gpa: '', collegeCommit: '', club: '', dayGroup: '' },
    { isStaff: false, isCommitted: false, uniformNo: '7',  player: 'Avery Lee',      position: 'MF', schoolName: 'Brophy College Prep',    gradYear: '2026', gpa: '3.8', collegeCommit: '', club: 'AZ Thunder',     dayGroup: 'AM' },
    { isStaff: false, isCommitted: false, uniformNo: '10', player: 'Brandon Cruz',   position: 'GK', schoolName: 'Desert Vista High School', gradYear: '2026', gpa: '3.5', collegeCommit: '', club: 'Desert FC',      dayGroup: 'AM' },
    { isStaff: false, isCommitted: false, uniformNo: '4',  player: 'Carlos Mendez',  position: 'D',  schoolName: 'Mountain Pointe',         gradYear: '2027', gpa: '3.9', collegeCommit: '', club: 'Phoenix Rising', dayGroup: 'AM' },
    { isStaff: false, isCommitted: true,  uniformNo: '11', player: 'Diego Santos',   position: 'F',  schoolName: 'Hamilton High School',    gradYear: '2026', gpa: '4.0', collegeCommit: 'Duke University', club: 'AZ Thunder', dayGroup: 'PM' },
    { isStaff: false, isCommitted: false, uniformNo: '22', player: 'Ethan Park',     position: 'MF', schoolName: 'Chaparral',               gradYear: '2027', gpa: '3.6', collegeCommit: '', club: 'Desert FC',      dayGroup: 'PM' },
    { isStaff: false, isCommitted: false, uniformNo: '8',  player: "Finn O'Brien",   position: 'D',  schoolName: 'Pinnacle',                gradYear: '2026', gpa: '3.7', collegeCommit: '', club: 'Phoenix Rising', dayGroup: 'AM' },
    { isStaff: false, isCommitted: false, uniformNo: '9',  player: 'Gabriel Ruiz',   position: 'F',  schoolName: 'Corona del Sol',          gradYear: '2026', gpa: '3.4', collegeCommit: '', club: 'AZ Thunder',     dayGroup: 'PM' },
    { isStaff: false, isCommitted: false, uniformNo: '3',  player: 'Henry Nguyen',   position: 'GK', schoolName: 'Red Mountain',            gradYear: '2028', gpa: '3.9', collegeCommit: '', club: 'Desert FC',      dayGroup: 'AM' },
    { isStaff: false, isCommitted: false, uniformNo: '14', player: 'Isaac Romero',   position: 'MF', schoolName: 'Perry High School',       gradYear: '2027', gpa: '3.2', collegeCommit: '', club: 'Phoenix Rising', dayGroup: 'PM' },
    { isStaff: false, isCommitted: false, uniformNo: '5',  player: 'Jacob Kim',      position: 'D',  schoolName: 'Basha',                   gradYear: '2026', gpa: '3.8', collegeCommit: '', club: 'AZ Thunder',     dayGroup: 'AM' },
    { isStaff: false, isCommitted: false, uniformNo: '17', player: 'Kevin Patel',    position: 'F',  schoolName: 'Highland',                gradYear: '2026', gpa: '3.5', collegeCommit: '', club: 'Desert FC',      dayGroup: 'PM' },
    { isStaff: false, isCommitted: false, uniformNo: '6',  player: 'Liam Walsh',     position: 'MF', schoolName: 'Casteel',                 gradYear: '2027', gpa: '3.6', collegeCommit: '', club: 'Phoenix Rising', dayGroup: 'AM' },
];

// ── Recruiter-mode preview sample (one team's page; never sent to the backend) ──
// Mirrors PackedRosterPdfService.GenerateRecruiterAsync: page = team, title is
// "{Agegroup}  {CLUB:TEAM}", a coach line, then 2-up player cards.
const RECRUITER_SAMPLE_TITLE = '2027  COPPERMINE:2027 NORTH';
const RECRUITER_SAMPLE_COACH = 'Coach: Jamie Smith   jsmith@coppermine.org   602-555-0142';

const RECRUITER_SAMPLE_CARDS: readonly RecruiterSampleCard[] = [
    {
        uniformNo: '11', name: 'Diego Santos', gradYear: '2027',
        metrics: 'GPA 4.0   ·   SAT 1480',
        email: 'diego.santos@email.com',
        address: '123 Main St, Phoenix, AZ 85001',
        phone: '602-555-0143',
        clubSchool: 'COPPERMINE / Hamilton High School',
        collegeCommit: 'Committed: Duke University',
    },
    {
        uniformNo: '7', name: 'Avery Lee', gradYear: '2028',
        metrics: 'GPA 3.8   ·   SAT 1390',
        email: 'avery.lee@email.com',
        address: '456 Oak Ave, Tempe, AZ 85281',
        phone: '480-555-0177',
        clubSchool: 'COPPERMINE / Brophy College Prep',
        collegeCommit: '',
    },
];

/**
 * PackedRoster Designer — director-built replacement for the canned "Tournament Roster
 * Packed" Bold RDLs. Pick + order + size player columns, toggle the card chrome, then
 * generate the PDF in-process. The two retired RDLs survive as the starter presets.
 */
@Component({
    selector: 'app-packed-roster-designer',
    standalone: true,
    imports: [CommonModule, DragDropModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './packed-roster-designer.component.html',
    styleUrl: './packed-roster-designer.component.scss',
})
export class PackedRosterDesignerComponent implements OnInit {
    private readonly packedSvc = inject(PackedRosterService);
    private readonly reportingSvc = inject(ReportingService);
    private readonly toast = inject(ToastService);
    private readonly route = inject(ActivatedRoute);

    /**
     * Which report the Designer is building. Recruiter is a fixed-layout report (no column
     * picker), so it shares this shell but swaps the body. Initial value comes from the
     * route's `data.mode` (the catalog seeds a recruiter tile that deep-links here), then
     * the user can flip styles in-place.
     */
    readonly reportStyle = signal<ReportStyle>('packed');

    // Recruiter-mode preview data (exposed read-only to the template).
    readonly recruiterTitle = RECRUITER_SAMPLE_TITLE;
    readonly recruiterCoach = RECRUITER_SAMPLE_COACH;
    readonly recruiterCards = RECRUITER_SAMPLE_CARDS;

    readonly availableFields = signal<PackedRosterFieldDto[]>([]);
    readonly selectedColumns = signal<DesignerColumn[]>([]);
    readonly nUp = signal<2 | 3>(3);

    /** Which starter preset is active. Radio-style: exactly one is always selected. */
    readonly activePreset = signal<'classic3' | 'collegeCommit2' | 'byPosition' | 'xpo'>('classic3');

    // Card chrome toggles
    readonly showCoaches = signal(true);
    readonly showRepName = signal(true);
    readonly showRepEmail = signal(true);
    readonly showRepPhone = signal(true);
    readonly schoolShowsCommit = signal(false);

    /** Append the player's own club to the name ("NAME / CLUB") — the PackedByPosition look. */
    readonly showClubAffiliation = signal(false);
    /** Within-card player order. */
    readonly sortBy = signal<PackedSortBy>('Uniform');

    readonly sortByOptions: readonly { value: PackedSortBy; label: string }[] = [
        { value: 'Uniform', label: 'Uniform #' },
        { value: 'Position', label: 'Position' },
        { value: 'Name', label: 'Name' },
    ];

    readonly isLoading = signal(true);
    readonly isGenerating = signal(false);

    // Recruiter is fixed-layout, so it's always generatable; packed needs ≥1 column.
    readonly canGenerate = computed(() =>
        !this.isGenerating() &&
        (this.reportStyle() === 'recruiter' || this.selectedColumns().length > 0));
    readonly selectedKeys = computed(() => new Set(this.selectedColumns().map(c => c.key)));

    // Header copy adapts to the active report style.
    readonly headerTitle = computed(() =>
        this.reportStyle() === 'recruiter' ? 'Recruiting Report' : 'PackedRoster Designer');
    readonly headerSubtitle = computed(() =>
        this.reportStyle() === 'recruiter'
            ? 'A college-coach recruiting packet: one card per player — grades, contact, club & high '
              + 'school, and any college commit — grouped by team. Fixed layout, so just generate.'
            : 'Build a packed tournament roster: pick the player columns, set the layout, and '
              + 'generate the PDF. Start from a preset or design your own.');

    // ── Live preview (schematic; mirrors PackedRosterPdfService geometry) ──
    readonly divisionTitle = SAMPLE_TEAM.divisionTitle;
    readonly teamName = SAMPLE_TEAM.clubTeamName;

    // Fixed pixel geometry (constant across nUp).
    readonly cardHeightPx = CARD_OUTER_H * PT;
    readonly headerHPx = HEADER_H * PT;
    readonly repTopPx = HEADER_H * PT;
    readonly repLineHPx = REP_LINE_H * PT;
    readonly repInsetPx = 2 * PT;
    readonly underlineTopPx = UNDERLINE_Y * PT;
    readonly rosterTopPx = ROSTER_TOP * PT;
    readonly rosterLeftPx = INSET_L * PT;
    readonly rosterHeightPx = ROSTER_H * PT;
    readonly baseRowHPx = BASE_ROW_H * PT;

    // Font sizes (pt × scale) — key columns (uniform/name) print bold at 7pt.
    readonly titlePx = 12 * PT;
    readonly headerPx = 8 * PT;
    readonly repPx = 6.5 * PT;
    readonly keyPx = 7 * PT;
    readonly cellPx = 6.5 * PT;

    // Card width + inner roster width depend on nUp (newspaper grid math).
    readonly cardWidthPx = computed(() => ((CONTENT_W / this.nUp()) - CARD_GAP * 2) * PT);
    readonly rosterWidthPx = computed(() => this.cardWidthPx() - (INSET_L + INSET_R) * PT);

    /** The club-rep line under the team header, built from the same toggles the PDF uses. */
    readonly previewRepLine = computed(() => {
        const parts: string[] = [];
        if (this.showRepName()) parts.push(SAMPLE_TEAM.clubRepName);
        if (this.showRepEmail()) parts.push(SAMPLE_TEAM.clubRepEmail);
        if (this.showRepPhone()) parts.push(SAMPLE_TEAM.clubRepCellphone);
        return parts.join(' ');
    });

    /** Sample rows projected to cells through the exact rules PackedRosterPdfService applies. */
    readonly previewRows = computed<{ cells: PreviewCell[] }[]>(() => {
        const cols = this.selectedColumns();
        const sumW = cols.reduce((s, c) => s + Math.max(1, c.widthWeight), 0) || 1;
        const showCommit = this.schoolShowsCommit();

        return this.sortSample(SAMPLE_ROWS.filter(r => this.showCoaches() || !r.isStaff))
            .map(r => ({
                cells: cols.map((c): PreviewCell => {
                    const truncAt = c.supportsLongText && c.longText === 'Truncate'
                        ? (c.truncateAt ?? 14) : null;
                    let text = this.resolveCell(r, c.key, showCommit);
                    if (truncAt && truncAt > 0 && text.length > truncAt) {
                        text = text.slice(0, truncAt);
                    }
                    // Staff phone in the school column is right-aligned (legacy base RDL).
                    const align = (c.key === 'school_name' && r.isStaff) ? 'right' : c.align.toLowerCase();
                    return {
                        text,
                        widthPct: (Math.max(1, c.widthWeight) / sumW) * 100,
                        align,
                        bold: c.key === 'uniform_no' || c.key === 'player',
                        wrap: c.supportsLongText && c.longText === 'Wrap',
                    };
                }),
            }));
    });

    private resolveCell(r: SampleRow, key: string, showCommit: boolean): string {
        switch (key) {
            case 'uniform_no': return r.isStaff ? '' : r.uniformNo;
            case 'player': return this.resolveName(r, showCommit);
            case 'position': return r.isStaff ? '' : r.position;
            case 'school_name': return this.resolveSchool(r, showCommit);
            case 'gradYear': return r.isStaff ? '' : r.gradYear;
            case 'gpa': return r.isStaff || r.isCommitted ? '' : r.gpa;
            case 'collegeCommit': return r.isStaff ? '' : r.collegeCommit;
            case 'dayGroup': return r.isStaff ? '' : r.dayGroup;
            default: return '';
        }
    }

    private resolveName(r: SampleRow, showCommit: boolean): string {
        if (r.isStaff) return 'Coach ' + r.player;
        const showAsterisk = r.isCommitted && !showCommit;
        let name = showAsterisk ? '* ' + r.player : r.player;
        // PackedByPosition appends the player's own club; the No-Club sibling (toggle off) omits it.
        if (this.showClubAffiliation() && r.club.length > 0) {
            name = `${name} / ${r.club.toUpperCase()}`;
        }
        return name;
    }

    /** Staff first, then players by the chosen key — mirrors the PDF's within-card order. */
    private sortSample(rows: readonly SampleRow[]): SampleRow[] {
        const by = this.sortBy();
        const uni = (u: string) => { const n = parseInt(u, 10); return isNaN(n) ? Number.MAX_SAFE_INTEGER : n; };
        return rows.slice().sort((a, b) => {
            if (a.isStaff !== b.isStaff) return a.isStaff ? -1 : 1;
            if (by === 'Position') return a.position.localeCompare(b.position) || uni(a.uniformNo) - uni(b.uniformNo);
            if (by === 'Name') return a.player.localeCompare(b.player);
            return uni(a.uniformNo) - uni(b.uniformNo) || a.player.localeCompare(b.player);
        });
    }

    private resolveSchool(r: SampleRow, showCommit: boolean): string {
        if (r.isStaff) return r.schoolName;        // proc overloads school_name with the staff phone
        if (showCommit && r.isCommitted) return r.collegeCommit;
        return r.schoolName;
    }

    ngOnInit(): void {
        // The recruiter catalog tile deep-links here with data.mode = 'recruiter'.
        if (this.route.snapshot.data['mode'] === 'recruiter') {
            this.reportStyle.set('recruiter');
        }

        // Always load the packed field pool (cheap) so flipping to packed needs no reload.
        this.packedSvc.getFields().subscribe({
            next: (fields) => {
                this.availableFields.set(fields);
                this.applyClassic3Up(); // sensible default + proves the picker
                this.isLoading.set(false);
            },
            error: () => {
                this.isLoading.set(false);
                this.toast.show('Failed to load packed-roster fields', 'danger');
            },
        });
    }

    setReportStyle(style: ReportStyle): void {
        this.reportStyle.set(style);
    }

    // ── Field selection ──

    isSelected(key: string): boolean {
        return this.selectedKeys().has(key);
    }

    toggleField(field: PackedRosterFieldDto): void {
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

    // ── Starter presets (the two retired RDLs) ──

    applyClassic3Up(): void {
        this.activePreset.set('classic3');
        this.nUp.set(3);
        this.selectedColumns.set([
            this.buildColumn('uniform_no'),
            this.buildColumn('player'),
            this.buildColumn('position'),
            this.buildColumn('school_name', { longText: 'Truncate', truncateAt: 14 }),
        ]);
        this.showCoaches.set(true);
        this.showRepName.set(true);
        this.showRepEmail.set(true);
        this.showRepPhone.set(true);
        this.schoolShowsCommit.set(false);
        this.showClubAffiliation.set(false);
        this.sortBy.set('Uniform');
    }

    applyCollegeCommit2Up(): void {
        this.activePreset.set('collegeCommit2');
        this.nUp.set(2);
        this.selectedColumns.set([
            this.buildColumn('uniform_no'),
            this.buildColumn('player'),
            this.buildColumn('position'),
            this.buildColumn('gradYear'),
            this.buildColumn('gpa'),
            this.buildColumn('collegeCommit', { longText: 'Wrap' }),
        ]);
        this.showCoaches.set(false);
        this.showRepName.set(true);
        this.showRepEmail.set(true);
        this.showRepPhone.set(true);
        this.schoolShowsCommit.set(false);
        this.showClubAffiliation.set(false);
        this.sortBy.set('Uniform');
    }

    /**
     * Packed By Position — the legacy PackedByPosition look: players ordered by position with
     * their own club appended ("NAME / CLUB"). Flip the affiliation toggle off for the
     * "No Club Players" sibling.
     */
    applyByPosition(): void {
        this.activePreset.set('byPosition');
        this.nUp.set(3);
        this.selectedColumns.set([
            this.buildColumn('uniform_no'),
            this.buildColumn('player'),
            this.buildColumn('position'),
            this.buildColumn('gradYear'),
        ]);
        this.showCoaches.set(true);
        this.showRepName.set(true);
        this.showRepEmail.set(true);
        this.showRepPhone.set(true);
        this.schoolShowsCommit.set(false);
        this.showClubAffiliation.set(true);
        this.sortBy.set('Position');
    }

    /**
     * Packed XPO — approximate replacement for the showcase XPO layout (the original .rpt isn't
     * available to clone). A compact by-position card with the day-group column and a name-only
     * rep line; editable like any preset.
     */
    applyXpo(): void {
        this.activePreset.set('xpo');
        this.nUp.set(3);
        this.selectedColumns.set([
            this.buildColumn('uniform_no'),
            this.buildColumn('player'),
            this.buildColumn('position'),
            this.buildColumn('dayGroup'),
        ]);
        this.showCoaches.set(true);
        this.showRepName.set(true);
        this.showRepEmail.set(false);
        this.showRepPhone.set(false);
        this.schoolShowsCommit.set(false);
        this.showClubAffiliation.set(true);
        this.sortBy.set('Position');
    }

    // ── Generate ──

    generate(): void {
        if (!this.canGenerate()) return;
        if (this.reportStyle() === 'recruiter') {
            this.generateRecruiter();
        } else {
            this.generatePacked();
        }
    }

    private generatePacked(): void {
        const request: PackedRosterRequestDto = {
            nUp: this.nUp(),
            columns: this.selectedColumns().map(c => ({
                key: c.key,
                widthWeight: c.widthWeight,
                align: c.align,
                longText: c.supportsLongText ? c.longText : 'Truncate',
                truncateAt: c.supportsLongText && c.longText === 'Truncate' ? c.truncateAt : null,
            })),
            showCoaches: this.showCoaches(),
            showRepName: this.showRepName(),
            showRepEmail: this.showRepEmail(),
            showRepPhone: this.showRepPhone(),
            schoolShowsCommit: this.schoolShowsCommit(),
            showClubAffiliation: this.showClubAffiliation(),
            sortBy: this.sortBy(),
        };

        this.isGenerating.set(true);
        this.packedSvc.generate(request).subscribe({
            next: (response) => {
                this.reportingSvc.triggerDownload(response, 'PackedRoster');
                this.isGenerating.set(false);
            },
            error: () => {
                this.isGenerating.set(false);
                this.toast.show('Failed to generate packed roster', 'danger');
            },
        });
    }

    private generateRecruiter(): void {
        this.isGenerating.set(true);
        this.packedSvc.generateRecruiter().subscribe({
            next: (response) => {
                this.reportingSvc.triggerDownload(response, 'RecruitingReport');
                this.isGenerating.set(false);
            },
            error: () => {
                this.isGenerating.set(false);
                this.toast.show('Failed to generate recruiting report', 'danger');
            },
        });
    }

    // ── Helpers ──

    setNUp(n: 2 | 3): void {
        this.nUp.set(n);
    }

    setSortBy(v: string): void {
        this.sortBy.set(v as PackedSortBy);
    }

    setChecked(setter: (v: boolean) => void, event: Event): void {
        setter((event.target as HTMLInputElement).checked);
    }

    private buildColumn(key: string, overrides: Partial<DesignerColumn> = {}): DesignerColumn {
        const f = this.availableFields().find(x => x.key === key);
        return {
            key,
            label: f?.label ?? key,
            widthWeight: f?.defaultWidthWeight ?? 30,
            align: f?.defaultAlign ?? 'Left',
            supportsLongText: f?.supportsLongText ?? false,
            longText: f?.supportsLongText ? 'Wrap' : 'Truncate',
            truncateAt: 14,
            ...overrides,
        };
    }
}
