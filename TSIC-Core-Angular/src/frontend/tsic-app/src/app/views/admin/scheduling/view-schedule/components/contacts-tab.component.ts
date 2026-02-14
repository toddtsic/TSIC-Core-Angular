import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import type { ContactDto } from '@core/api';

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
                @for (ag of grouped(); track ag.agegroupName) {
                    <div class="ag-section">
                        <button class="section-header ag-header"
                                (click)="toggleSection('ag:' + ag.agegroupName)">
                            <span class="chevron" [class.expanded]="isSectionOpen('ag:' + ag.agegroupName)"></span>
                            {{ ag.agegroupName }}
                        </button>

                        @if (isSectionOpen('ag:' + ag.agegroupName)) {
                            @for (div of ag.divisions; track div.divName) {
                                <div class="div-section">
                                    <button class="section-header div-header"
                                            (click)="toggleSection('div:' + ag.agegroupName + ':' + div.divName)">
                                        <span class="chevron" [class.expanded]="isSectionOpen('div:' + ag.agegroupName + ':' + div.divName)"></span>
                                        {{ div.divName }}
                                    </button>

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
                                                                            <a [href]="'tel:' + c.cellphone" class="contact-link">{{ c.cellphone }}</a>
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

        .section-header {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            width: 100%;
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

        .ag-header {
            font-size: var(--font-size-base, 1rem);
            background: var(--bs-secondary-bg);
            padding: var(--space-2) var(--space-3);
        }

        .ag-header:hover {
            background: var(--bs-secondary-bg);
            opacity: 0.85;
        }

        .div-header {
            font-size: var(--font-size-sm);
            padding-left: var(--space-5);
        }

        .team-header {
            font-size: var(--font-size-sm);
            padding-left: var(--space-8);
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

        .contacts-table {
            width: 100%;
            border-collapse: collapse;
            font-size: var(--font-size-sm);
            margin-left: var(--space-10);
            margin-bottom: var(--space-2);
            max-width: calc(100% - var(--space-10));
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
            text-align: left;
            white-space: nowrap;
        }

        .col-phone {
            text-align: left;
            white-space: nowrap;
        }

        .col-email {
            text-align: left;
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

        .ag-section {
            border: 1px solid var(--bs-border-color);
            border-radius: var(--bs-border-radius);
            overflow: hidden;
        }

        .div-section {
            border-top: 1px solid var(--bs-border-color);
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
}
