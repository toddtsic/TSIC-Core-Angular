import { ChangeDetectionStrategy, Component, computed, effect, input, signal } from '@angular/core';
import type { ContactDto } from '@core/api';

/** Formats a 10-digit phone string as xxx-xxx-xxxx. */
function formatPhone(value: string | null | undefined): string | null {
    if (!value) return value ?? null;
    const digits = value.replace(/\D/g, '');
    if (digits.length === 10) return `${digits.slice(0, 3)}-${digits.slice(3, 6)}-${digits.slice(6)}`;
    return value;
}

/** A group of contacts at the team level within the agegroup/div/club hierarchy */
interface TeamContactGroup {
    teamName: string;
    contacts: ContactDto[];
}

interface DivContactGroup {
    divName: string;
    teams: TeamContactGroup[];
}

interface AgContactGroup {
    agegroupName: string;
    divisions: DivContactGroup[];
}

@Component({
    selector: 'app-contacts-tab',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @if (isLoading()) {
            <div class="loading-container">
                <span class="spinner-border spinner-border-sm" role="status"></span>
                Loading contacts...
            </div>
        } @else if (grouped().length === 0) {
            <div class="empty-state">No contacts available.</div>
        } @else {
            <div class="contacts-wrapper">
                <!-- Age group tab bar + expand all -->
                <div class="ag-tabs-row">
                    <div class="ag-tabs">
                        @for (tab of ageGroupTabs(); track tab; let i = $index) {
                            <button class="ag-tab"
                                    [class.active]="activeAgTabIndex() === i"
                                    (click)="activeAgTabIndex.set(i)">
                                {{ tab }}
                            </button>
                        }
                    </div>
                    @if (activeAgeGroup()) {
                        <button class="expand-btn"
                                (click)="toggleAllInActiveAg($event)"
                                [title]="isActiveAgFullyExpanded() ? 'Collapse all' : 'Expand all'">
                            {{ isActiveAgFullyExpanded() ? '−' : '+' }} All
                        </button>
                    }
                </div>

                <!-- Divisions + Teams (2-level accordion within active age group) -->
                @if (activeAgeGroup(); as ag) {
                    @for (div of ag.divisions; track div.divName) {
                        <div class="div-section">
                            <div class="section-row div-header">
                                <button class="section-header"
                                        (click)="toggleSection('div:' + ag.agegroupName + ':' + div.divName)">
                                    <span class="chevron" [class.expanded]="isSectionOpen('div:' + ag.agegroupName + ':' + div.divName)"></span>
                                    {{ div.divName }}
                                </button>
                                @if (isSectionOpen('div:' + ag.agegroupName + ':' + div.divName)) {
                                    <button class="expand-btn" (click)="toggleAllInDiv(ag.agegroupName, div, $event)"
                                            [title]="isDivFullyExpanded(ag.agegroupName, div) ? 'Collapse all teams' : 'Expand all teams'">
                                        {{ isDivFullyExpanded(ag.agegroupName, div) ? '−' : '+' }} Teams
                                    </button>
                                }
                            </div>

                            @if (isSectionOpen('div:' + ag.agegroupName + ':' + div.divName)) {
                                @for (team of div.teams; track team.teamName) {
                                    <div class="team-section">
                                        <button class="section-header team-header"
                                                (click)="toggleSection('team:' + ag.agegroupName + ':' + div.divName + ':' + team.teamName)">
                                            <span class="chevron" [class.expanded]="isSectionOpen('team:' + ag.agegroupName + ':' + div.divName + ':' + team.teamName)"></span>
                                            {{ team.teamName }}
                                            <span class="contact-count">({{ team.contacts.length }})</span>
                                        </button>

                                        @if (isSectionOpen('team:' + ag.agegroupName + ':' + div.divName + ':' + team.teamName)) {
                                            <table class="contacts-table">
                                                <thead>
                                                    <tr>
                                                        <th class="col-name">Name</th>
                                                        <th class="col-phone">Phone</th>
                                                        <th class="col-email">Email</th>
                                                    </tr>
                                                </thead>
                                                <tbody>
                                                    @for (c of team.contacts; track c.firstName + c.lastName + c.email) {
                                                        <tr>
                                                            <td class="col-name">{{ c.firstName }} {{ c.lastName }}</td>
                                                            <td class="col-phone">
                                                                @if (c.cellphone) {
                                                                    <a [href]="'tel:' + c.cellphone" class="contact-link">{{ fmtPhone(c.cellphone) }}</a>
                                                                } @else {
                                                                    <span class="no-data">&mdash;</span>
                                                                }
                                                            </td>
                                                            <td class="col-email">
                                                                @if (c.email) {
                                                                    <a [href]="'mailto:' + c.email" class="contact-link">{{ c.email }}</a>
                                                                } @else {
                                                                    <span class="no-data">&mdash;</span>
                                                                }
                                                            </td>
                                                        </tr>
                                                    }
                                                </tbody>
                                            </table>
                                        }
                                    </div>
                                }
                            }
                        </div>
                    }
                }
            </div>
        }
    `,
    styles: [`
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

        .contacts-wrapper {
            display: flex;
            flex-direction: column;
            gap: var(--space-2);
        }

        /* ── Age Group Tabs ── */

        .ag-tabs-row {
            display: flex;
            align-items: center;
            gap: var(--space-3);
            padding-bottom: var(--space-2);
            border-bottom: 1px solid var(--bs-border-color);
        }

        .ag-tabs {
            display: flex;
            gap: var(--space-1);
            overflow-x: auto;
            scrollbar-width: thin;
            flex: 1;
            min-width: 0;
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
            transition: background-color 0.15s, color 0.15s, border-color 0.15s;
        }

        .ag-tab:hover {
            background: var(--bs-secondary-bg);
            color: var(--bs-body-color);
        }

        .ag-tab.active {
            background: var(--bs-primary);
            color: white;
            border-color: var(--bs-primary);
        }

        /* ── Accordion sections ── */

        .section-row {
            display: flex;
            align-items: center;
        }

        .section-header {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            flex: 1;
            border: none;
            background: none;
            cursor: pointer;
            font-weight: 600;
            color: var(--bs-body-color);
            text-align: left;
            padding: var(--space-1) var(--space-2);
            border-radius: var(--bs-border-radius);
        }

        .section-header:hover {
            background: var(--bs-tertiary-bg);
        }

        .expand-btn {
            border: none;
            background: none;
            cursor: pointer;
            font-size: var(--font-size-xs);
            color: var(--bs-primary);
            padding: var(--space-1) var(--space-2);
            white-space: nowrap;
            flex-shrink: 0;
        }

        .expand-btn:hover {
            text-decoration: underline;
        }

        .div-header {
            font-size: var(--font-size-sm);
            background: var(--bs-secondary-bg);
            padding: var(--space-2) var(--space-3);
        }

        .div-header > .section-header {
            padding: 0;
            background: none;
        }

        .div-header > .section-header:hover {
            background: none;
        }

        .team-header {
            font-size: var(--font-size-sm);
            padding-left: var(--space-5);
            font-weight: 500;
        }

        .contact-count {
            font-weight: 400;
            color: var(--bs-secondary-color);
            font-size: var(--font-size-sm);
        }

        .chevron {
            display: inline-block;
            width: 0;
            height: 0;
            border-left: 5px solid var(--bs-secondary-color);
            border-top: 4px solid transparent;
            border-bottom: 4px solid transparent;
            transition: transform 0.15s ease;
            flex-shrink: 0;
        }

        .chevron.expanded {
            transform: rotate(90deg);
        }

        /* ── Contact Tables ── */

        .contacts-table {
            width: 100%;
            border-collapse: collapse;
            table-layout: fixed;
            font-size: var(--font-size-sm);
            margin-left: var(--space-8);
            margin-bottom: var(--space-2);
            max-width: calc(100% - var(--space-8));
        }

        .contacts-table thead th {
            background: var(--bs-tertiary-bg);
            padding: var(--space-1) var(--space-2);
            border-bottom: 2px solid var(--bs-border-color);
            font-weight: 600;
            color: var(--bs-body-color);
            white-space: nowrap;
        }

        .contacts-table tbody td {
            padding: var(--space-1) var(--space-2);
            border-bottom: 1px solid var(--bs-border-color);
            color: var(--bs-body-color);
        }

        .contacts-table tbody tr:last-child td {
            border-bottom: none;
        }

        .col-name {
            width: 30%;
            text-align: left;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
        }

        .col-phone {
            width: 25%;
            text-align: left;
            white-space: nowrap;
        }

        .col-email {
            width: 45%;
            text-align: left;
            overflow: hidden;
            text-overflow: ellipsis;
        }

        .contact-link {
            color: var(--bs-primary);
            text-decoration: none;
        }

        .contact-link:hover {
            text-decoration: underline;
        }

        .no-data {
            color: var(--bs-secondary-color);
        }

        .div-section {
            border: 1px solid var(--bs-border-color);
            border-radius: var(--bs-border-radius);
            overflow: hidden;
        }

        .team-section {
            border-top: 1px solid var(--bs-border-color);
        }
    `]
})
export class ContactsTabComponent {
    contacts = input<ContactDto[]>([]);
    isLoading = input<boolean>(false);

    private readonly openSections = signal<Set<string>>(new Set());
    readonly activeAgTabIndex = signal(0);

    /** Group flat contacts by AgegroupName -> DivName -> ClubName+TeamName */
    readonly grouped = computed<AgContactGroup[]>(() => {
        const items = this.contacts();
        if (!items || items.length === 0) return [];

        // Build a map: agegroup -> div -> team -> contacts[]
        const agMap = new Map<string, Map<string, Map<string, ContactDto[]>>>();

        for (const c of items) {
            if (!agMap.has(c.agegroupName)) {
                agMap.set(c.agegroupName, new Map());
            }
            const divMap = agMap.get(c.agegroupName)!;

            if (!divMap.has(c.divName)) {
                divMap.set(c.divName, new Map());
            }
            const teamMap = divMap.get(c.divName)!;

            const teamKey = c.clubName ? `${c.clubName} - ${c.teamName}` : c.teamName;
            if (!teamMap.has(teamKey)) {
                teamMap.set(teamKey, []);
            }
            teamMap.get(teamKey)!.push(c);
        }

        // Convert to structured array
        const result: AgContactGroup[] = [];
        for (const [agName, divMap] of agMap) {
            const divisions: DivContactGroup[] = [];
            for (const [divName, teamMap] of divMap) {
                const teams: TeamContactGroup[] = [];
                for (const [teamName, contacts] of teamMap) {
                    teams.push({ teamName, contacts });
                }
                teams.sort((a, b) => a.teamName.localeCompare(b.teamName));
                divisions.push({ divName, teams });
            }
            divisions.sort((a, b) => a.divName.localeCompare(b.divName));
            result.push({ agegroupName: agName, divisions });
        }
        result.sort((a, b) => a.agegroupName.localeCompare(b.agegroupName));
        return result;
    });

    readonly ageGroupTabs = computed<string[]>(() =>
        this.grouped().map(ag => ag.agegroupName)
    );

    readonly activeAgeGroup = computed<AgContactGroup | null>(() => {
        const groups = this.grouped();
        const idx = this.activeAgTabIndex();
        return groups[idx] ?? null;
    });

    readonly isActiveAgFullyExpanded = computed(() => {
        const ag = this.activeAgeGroup();
        if (!ag) return false;
        return this.isAgFullyExpanded(ag);
    });

    constructor() {
        // Reset tab index when data changes
        effect(() => {
            this.grouped(); // track
            this.activeAgTabIndex.set(0);
        });
    }

    toggleSection(key: string): void {
        this.openSections.update(s => {
            const next = new Set(s);
            if (next.has(key)) {
                next.delete(key);
            } else {
                next.add(key);
            }
            return next;
        });
    }

    isSectionOpen(key: string): boolean {
        return this.openSections().has(key);
    }

    fmtPhone(value: string | null | undefined): string | null {
        return formatPhone(value);
    }

    /** Are all divisions + teams under this agegroup expanded? */
    isAgFullyExpanded(ag: AgContactGroup): boolean {
        const s = this.openSections();
        for (const div of ag.divisions) {
            const divKey = `div:${ag.agegroupName}:${div.divName}`;
            if (!s.has(divKey)) return false;
            for (const team of div.teams) {
                if (!s.has(`team:${ag.agegroupName}:${div.divName}:${team.teamName}`)) return false;
            }
        }
        return true;
    }

    /** Are all teams under this division expanded? */
    isDivFullyExpanded(agName: string, div: DivContactGroup): boolean {
        const s = this.openSections();
        for (const team of div.teams) {
            if (!s.has(`team:${agName}:${div.divName}:${team.teamName}`)) return false;
        }
        return true;
    }

    /** Expand or collapse all divisions + teams in the active age group tab. */
    toggleAllInActiveAg(event: Event): void {
        event.stopPropagation();
        const ag = this.activeAgeGroup();
        if (!ag) return;
        this.toggleAllInAgegroup(ag, event);
    }

    /** Expand or collapse every division + team within an agegroup. */
    toggleAllInAgegroup(ag: AgContactGroup, event: Event): void {
        event.stopPropagation();
        const expand = !this.isAgFullyExpanded(ag);
        this.openSections.update(s => {
            const next = new Set(s);
            for (const div of ag.divisions) {
                const divKey = `div:${ag.agegroupName}:${div.divName}`;
                if (expand) {
                    next.add(divKey);
                } else {
                    next.delete(divKey);
                }
                for (const team of div.teams) {
                    const teamKey = `team:${ag.agegroupName}:${div.divName}:${team.teamName}`;
                    if (expand) {
                        next.add(teamKey);
                    } else {
                        next.delete(teamKey);
                    }
                }
            }
            return next;
        });
    }

    /** Expand or collapse every team within a division. */
    toggleAllInDiv(agName: string, div: DivContactGroup, event: Event): void {
        event.stopPropagation();
        const expand = !this.isDivFullyExpanded(agName, div);
        this.openSections.update(s => {
            const next = new Set(s);
            for (const team of div.teams) {
                const teamKey = `team:${agName}:${div.divName}:${team.teamName}`;
                if (expand) {
                    next.add(teamKey);
                } else {
                    next.delete(teamKey);
                }
            }
            return next;
        });
    }
}
