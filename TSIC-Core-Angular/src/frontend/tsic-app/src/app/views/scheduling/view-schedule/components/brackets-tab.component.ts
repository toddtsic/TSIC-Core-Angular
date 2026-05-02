import {
    ChangeDetectionStrategy, Component, computed, effect,
    ElementRef, EventEmitter, input, OnDestroy, Output,
    signal, ViewChild
} from '@angular/core';
import type { DivisionBracketResponse } from '@core/api';
import {
    Diagram, DiagramTools, SnapConstraints,
    HierarchicalTree, DataBinding
} from '@syncfusion/ej2-diagrams';
import { DataManager } from '@syncfusion/ej2-data';
import { contrastText, agBg } from '../../shared/utils/scheduling-helpers';

// Register Syncfusion modules for imperative Diagram creation
Diagram.Inject(HierarchicalTree, DataBinding);

interface BracketNode {
    gid: number;
    parentGid: number | null;
    t1Name: string;
    t2Name: string;
    t1Id: string | null;
    t2Id: string | null;
    t1Score: number | null;
    t2Score: number | null;
    t1Css: string;
    t2Css: string;
    locationTime: string | null;
    fieldId: string | null;
    roundType: string;
    isPlaceholder?: boolean;
}

@Component({
    selector: 'app-brackets-tab',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @if (isLoading()) {
            <div class="loading-container">
                <span class="spinner-border spinner-border-sm" role="status"></span>
                Loading brackets...
            </div>
        } @else if (brackets().length === 0) {
            <div class="empty-state">No bracket data available.</div>
        } @else {
            <!-- Tab bar -->
            <div class="ag-tabs">
                @for (tab of tabItems(); track tab.index) {
                    <button class="ag-tab"
                            [class.active]="activeTabIndex() === tab.index"
                            [class.has-color]="!!tab.color"
                            [style.background]="tab.color ?? null"
                            [style.color]="tab.color ? tab.contrastColor : null"
                            [style.border-color]="tab.color ?? null"
                            (click)="selectTab(tab.index)">
                        {{ tab.label }}
                    </button>
                }
            </div>

            <!-- Diagram rendered imperatively -->
            <div class="diagram-container">
                <div #diagramHost></div>
            </div>
        }
    `,
    styles: [`
        :host { display: block; }

        .loading-container {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            padding: var(--space-4);
            color: var(--bs-secondary-color);
        }

        .empty-state {
            padding: var(--space-4);
            color: var(--bs-secondary-color);
            text-align: center;
        }

        /* ── Tab bar ── */

        .ag-tabs {
            display: flex;
            gap: var(--space-1);
            padding: var(--space-2) var(--space-3);
            border-bottom: 1px solid var(--bs-border-color);
            overflow-x: auto;
            scrollbar-width: thin;
        }

        .ag-tab {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
            padding: var(--space-1) var(--space-3);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-sm);
            background: var(--bs-body-bg);
            color: var(--bs-secondary-color);
            font-size: var(--font-size-sm);
            font-weight: 500;
            cursor: pointer;
            white-space: nowrap;
            transition: background-color 0.15s, color 0.15s, border-color 0.15s, box-shadow 0.15s, opacity 0.15s;
        }

        .ag-tab:hover {
            background: var(--bs-secondary-bg);
            color: var(--bs-body-color);
        }

        /* Colored (agegroup) tabs: full-bleed colored background, contrast text from inline style.
           Inactive colored tabs are dimmed; active colored tab pops with a focus ring. */
        .ag-tab.has-color { opacity: 0.55; }
        .ag-tab.has-color:hover { opacity: 0.85; }
        .ag-tab.has-color.active {
            opacity: 1;
            font-weight: 700;
            box-shadow: 0 0 0 2px var(--bs-body-bg), 0 0 0 4px var(--bs-body-color);
        }

        /* Fallback when no agegroup color is set */
        .ag-tab:not(.has-color).active {
            background: var(--bs-primary);
            color: white;
            border-color: var(--bs-primary);
        }

        .ag-tab:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
        }

        @media (prefers-reduced-motion: reduce) {
            .ag-tab { transition: none !important; }
        }

        /* ── Diagram container ── */

        .diagram-container {
            border: 1px solid var(--bs-border-color);
            border-top: none;
            border-radius: 0 0 var(--radius-sm) var(--radius-sm);
            overflow: hidden;
            background: var(--bs-body-bg);
        }
    `]
})
export class BracketsTabComponent implements OnDestroy {
    brackets = input<DivisionBracketResponse[]>([]);
    canScore = input<boolean>(false);
    isLoading = input<boolean>(false);
    agegroupColors = input<Record<string, string | null>>({});

    @Output() editBracketScore = new EventEmitter<{
        gid: number;
        t1Name: string;
        t2Name: string;
        t1Score: number | null;
        t2Score: number | null;
    }>();

    @Output() viewTeamResults = new EventEmitter<string>();
    @Output() viewFieldInfo = new EventEmitter<string>();

    @ViewChild('diagramHost', { static: false }) diagramHost!: ElementRef<HTMLDivElement>;

    // ── Tab state ──
    readonly activeTabIndex = signal(0);
    private diagramInstance: Diagram | null = null;
    private clickListener: ((e: MouseEvent) => void) | null = null;

    readonly tabItems = computed(() => {
        const data = this.brackets();
        const colors = this.agegroupColors();
        return data.map((b, i) => {
            const color = colors[b.agegroupName] ?? null;
            return {
                index: i,
                label: b.divName ? `${b.agegroupName} — ${b.divName}` : b.agegroupName,
                champion: b.champion ?? null,
                color,
                contrastColor: contrastText(color)
            };
        });
    });

    readonly activeBracket = computed(() => {
        const data = this.brackets();
        const idx = this.activeTabIndex();
        return data[idx] ?? null;
    });

    constructor() {
        // Reset tab + rebuild diagram when brackets data changes
        effect(() => {
            this.brackets(); // track
            this.activeTabIndex.set(0);
        });

        // Rebuild diagram when active bracket, canScore, or agegroup colors change
        effect(() => {
            const bracket = this.activeBracket();
            this.canScore(); // track — rebuild when capabilities resolve
            this.agegroupColors(); // track — rebuild when LADT tree loads
            // Use setTimeout to ensure ViewChild is available after template renders
            setTimeout(() => this.buildDiagram(bracket), 0);
        });
    }

    ngOnDestroy(): void {
        this.destroyDiagram();
    }

    selectTab(index: number): void {
        this.activeTabIndex.set(index);
    }

    private destroyDiagram(): void {
        if (this.clickListener && this.diagramHost?.nativeElement) {
            this.diagramHost.nativeElement.removeEventListener('click', this.clickListener);
            this.clickListener = null;
        }
        if (this.diagramInstance) {
            this.diagramInstance.destroy();
            this.diagramInstance = null;
        }
    }

    private buildDiagram(bracket: DivisionBracketResponse | null): void {
        this.destroyDiagram();

        if (!bracket || bracket.matches.length === 0 || !this.diagramHost) return;

        const host = this.diagramHost.nativeElement;
        // Clear any previous content and create a fresh div
        host.innerHTML = '<div id="bracket-diagram"></div>';
        const container = host.querySelector('#bracket-diagram') as HTMLElement;

        // Resolve agegroup color for card tinting (12% over body-bg) + left stripe
        const agColor = this.agegroupColors()[bracket.agegroupName] ?? null;
        const cardBg = agBg(agColor);
        const stripeColor = agColor ?? 'var(--bs-border-color)';

        // Normalize parentGid: 0 → null (legacy: Pgid == 0 ? null : Pgid)
        const games: BracketNode[] = bracket.matches.map(m => ({
            gid: m.gid,
            parentGid: (m.parentGid && m.parentGid !== 0) ? m.parentGid : null,
            t1Name: m.t1Name,
            t2Name: m.t2Name,
            t1Id: m.t1Id ?? null,
            t2Id: m.t2Id ?? null,
            t1Score: m.t1Score ?? null,
            t2Score: m.t2Score ?? null,
            t1Css: m.t1Css,
            t2Css: m.t2Css,
            locationTime: m.locationTime ?? null,
            fieldId: m.fieldId ?? null,
            roundType: m.roundType,
            isPlaceholder: false
        }));

        // ── Inject placeholder nodes for balanced tree layout (legacy algorithm) ──
        // For each parent with only 1 child, unshift a blank sibling to the front
        // so it renders ABOVE the real game in the tree (exactly like legacy)
        const gamesClone = [...games];
        for (const g of gamesClone) {
            if (g.parentGid == null) continue;
            const siblings = games.filter(s => s.parentGid === g.parentGid);
            if (siblings.length === 1) {
                games.unshift({
                    gid: -g.parentGid,
                    parentGid: g.parentGid,
                    t1Name: '', t2Name: '',
                    t1Id: null, t2Id: null,
                    t1Score: null, t2Score: null,
                    t1Css: 'pending', t2Css: 'pending',
                    locationTime: null, fieldId: null, roundType: '',
                    isPlaceholder: true
                });
            }
        }

        // ── Compute dynamic height from leaf count (including placeholders) ──
        const nodeHeight = 80;
        const verticalSpacing = 16;
        const parentGids = new Set(games.map(g => g.parentGid).filter(Boolean));
        const leafCount = games.filter(g => !parentGids.has(g.gid)).length;
        const diagramHeight = Math.max(300, leafCount * (nodeHeight + verticalSpacing) + 60);

        // ── Resolve palette colors for SVG connectors ──
        const connectorColor = getComputedStyle(document.documentElement)
            .getPropertyValue('--bs-secondary-color').trim() || '#78716c';

        // ── DOM delegation for all click interactions ──
        // Syncfusion strips inline onclick from HTML node content,
        // but data-* attributes survive. DOM delegation on the host works.
        const matchesRef = bracket.matches;
        this.clickListener = (e: MouseEvent) => {
            const target = e.target as HTMLElement;

            // Field name click → Field Directions
            const fieldEl = target.closest('[data-field-id]') as HTMLElement | null;
            if (fieldEl) {
                const fieldId = fieldEl.getAttribute('data-field-id');
                if (fieldId) {
                    this.viewFieldInfo.emit(fieldId);
                }
                return;
            }

            // Team name click → Game History modal
            const teamEl = target.closest('[data-team-id]') as HTMLElement | null;
            if (teamEl) {
                const teamId = teamEl.getAttribute('data-team-id');
                if (teamId) {
                    this.viewTeamResults.emit(teamId);
                }
                return;
            }

            // Pencil icon click → Score Edit modal
            const scoreEl = target.closest('[data-score-gid]') as HTMLElement | null;
            if (scoreEl) {
                const gid = parseInt(scoreEl.getAttribute('data-score-gid')!, 10);
                if (gid > 0) {
                    const match = matchesRef.find(m => m.gid === gid);
                    if (match) {
                        this.editBracketScore.emit({
                            gid: match.gid,
                            t1Name: match.t1Name,
                            t2Name: match.t2Name,
                            t1Score: match.t1Score ?? null,
                            t2Score: match.t2Score ?? null
                        });
                    }
                }
            }
        };
        host.addEventListener('click', this.clickListener);

        // Create diagram using vanilla Syncfusion JS API
        const diagram = new Diagram({
            width: '100%',
            height: diagramHeight,
            snapSettings: { constraints: SnapConstraints.None },

            dataSourceSettings: {
                id: 'gid',
                parentId: 'parentGid',
                dataSource: new DataManager(games as any)
            },

            tool: DiagramTools.ZoomPan,

            layout: {
                type: 'HierarchicalTree',
                orientation: 'RightToLeft',
                horizontalSpacing: 40,
                verticalSpacing: verticalSpacing,
                enableAnimation: true
            },

            getNodeDefaults: (obj: any) => {
                obj.shape = { type: 'HTML' };
                obj.width = 260;
                obj.height = nodeHeight;
                return obj;
            },

            setNodeTemplate: (node: any) => {
                const data = node.data as BracketNode | undefined;
                if (!data) return;

                // Placeholder: empty card that reserves layout space (like legacy)
                if (data.isPlaceholder) {
                    node.shape = {
                        type: 'HTML',
                        content: `<div style="background:var(--bs-body-bg);border:1px dashed var(--bs-border-color);border-radius:6px;width:100%;height:100%;box-sizing:border-box;opacity:0.4;"></div>`
                    };
                    return;
                }

                const t1Score = data.t1Score != null ? data.t1Score : '';
                const t2Score = data.t2Score != null ? data.t2Score : '';
                const loc = data.locationTime ? this.escapeHtml(data.locationTime) : '';

                // Score-based styling: green winner, red loser, default on tie/pending
                const bothScored = data.t1Score != null && data.t2Score != null;
                let t1RowStyle = 'color:var(--bs-body-color);';
                let t2RowStyle = 'color:var(--bs-body-color);';
                let t1ScoreColor = '';
                let t2ScoreColor = '';

                if (bothScored && data.t1Score !== data.t2Score) {
                    const t1Wins = data.t1Score! > data.t2Score!;
                    t1RowStyle = t1Wins ? 'font-weight:700;' : 'opacity:0.7;';
                    t2RowStyle = t1Wins ? 'opacity:0.7;' : 'font-weight:700;';
                    t1ScoreColor = t1Wins ? 'color:var(--bs-success);' : 'color:var(--bs-danger);';
                    t2ScoreColor = t1Wins ? 'color:var(--bs-danger);' : 'color:var(--bs-success);';
                }

                // Team name spans — clickable via DOM delegation when team ID exists
                const t1NameHtml = data.t1Id
                    ? `<span data-team-id="${data.t1Id}" style="flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:12px;text-decoration:underline;cursor:pointer;">${this.escapeHtml(data.t1Name)}</span>`
                    : `<span style="flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:12px;">${this.escapeHtml(data.t1Name)}</span>`;

                const t2NameHtml = data.t2Id
                    ? `<span data-team-id="${data.t2Id}" style="flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:12px;text-decoration:underline;cursor:pointer;">${this.escapeHtml(data.t2Name)}</span>`
                    : `<span style="flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:12px;">${this.escapeHtml(data.t2Name)}</span>`;

                // Location line — clickable link to field directions when fieldId exists
                const locHtml = data.fieldId && loc
                    ? `<span data-field-id="${data.fieldId}" style="text-decoration:underline;cursor:pointer;">${loc}</span>`
                    : loc;

                // Pencil icon — boxed button, top-right corner, only for authenticated admins
                const canClick = this.canScore();
                const pencilSvg = `<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" fill="currentColor" viewBox="0 0 16 16"><path d="M15.502 1.94a.5.5 0 0 1 0 .706L14.459 3.69l-2-2L13.502.646a.5.5 0 0 1 .707 0l1.293 1.293zm-1.75 2.456-2-2L4.939 9.21a.5.5 0 0 0-.121.196l-.805 2.414a.25.25 0 0 0 .316.316l2.414-.805a.5.5 0 0 0 .196-.12l6.813-6.814z"/><path fill-rule="evenodd" d="M1 13.5A1.5 1.5 0 0 0 2.5 15h11a1.5 1.5 0 0 0 1.5-1.5v-6a.5.5 0 0 0-1 0v6a.5.5 0 0 1-.5.5h-11a.5.5 0 0 1-.5-.5v-11a.5.5 0 0 1 .5-.5H9a.5.5 0 0 0 0-1H2.5A1.5 1.5 0 0 0 1 2.5v11z"/></svg>`;
                const pencilHtml = canClick
                    ? `<span data-score-gid="${data.gid}" style="position:absolute;top:3px;right:3px;cursor:pointer;display:inline-flex;align-items:center;justify-content:center;width:22px;height:22px;border-radius:4px;background:var(--bs-secondary-bg);border:1px solid var(--bs-border-color);color:var(--bs-primary);line-height:1;" title="Edit Score">${pencilSvg}</span>`
                    : '';

                node.shape = {
                    type: 'HTML',
                    content: `
                        <div style="position:relative;background:${cardBg};border:1px solid var(--bs-border-color);border-left:5px solid ${stripeColor};border-radius:6px;overflow:hidden;width:100%;height:100%;box-sizing:border-box;display:flex;flex-direction:column;justify-content:center;padding:6px 8px 6px 6px;">
                            ${pencilHtml}
                            <div style="text-align:center;font-size:10px;color:var(--bs-secondary-color);margin-bottom:3px;line-height:1.2;">${locHtml}</div>
                            <div style="display:flex;justify-content:space-between;align-items:center;padding:2px 4px;${t1RowStyle}">
                                ${t1NameHtml}
                                <span style="font-weight:700;font-size:12px;min-width:1.2rem;text-align:right;margin-left:6px;${t1ScoreColor}">${t1Score}</span>
                            </div>
                            <div style="height:1px;background:var(--bs-border-color);margin:2px 0;"></div>
                            <div style="display:flex;justify-content:space-between;align-items:center;padding:2px 4px;${t2RowStyle}">
                                ${t2NameHtml}
                                <span style="font-weight:700;font-size:12px;min-width:1.2rem;text-align:right;margin-left:6px;${t2ScoreColor}">${t2Score}</span>
                            </div>
                        </div>
                    `
                };
            },

            getConnectorDefaults: (obj: any) => {
                obj.targetDecorator = { shape: 'None' };
                obj.type = 'Orthogonal';
                obj.constraints = 0;
                obj.cornerRadius = 5;
                obj.style = { strokeColor: connectorColor, strokeWidth: 1.5 };
                return obj;
            }
        });

        diagram.appendTo(container);
        this.diagramInstance = diagram;
    }

    // ── Helpers ──

    private escapeHtml(text: string): string {
        return text
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }
}
