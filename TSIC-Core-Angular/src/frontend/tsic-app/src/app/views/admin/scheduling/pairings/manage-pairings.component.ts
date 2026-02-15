import { Component, computed, inject, OnInit, signal, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
    PairingsService,
    type AgegroupWithDivisionsDto,
    type DivisionSummaryDto,
    type PairingDto,
    type DivisionPairingsResponse,
    type DivisionTeamDto
} from './services/pairings.service';
import { DivisionNavigatorComponent } from '../shared/components/division-navigator/division-navigator.component';

/** Team-type code legend for tooltips. */
const TYPE_LABELS: Record<string, string> = {
    T: 'Team', Q: 'Quarterfinal', S: 'Semifinal', F: 'Final',
    X: 'Round of 16', Y: 'Round of 32', Z: 'Round of 64', C: 'Consolation',
    RRD1: 'RR Div 1', RRD2: 'RR Div 2', RRD3: 'RR Div 3', RRD4: 'RR Div 4',
    RRD5: 'RR Div 5', RRD6: 'RR Div 6', RRD7: 'RR Div 7', RRD8: 'RR Div 8'
};

/** Bracket keys available for single-elimination generation. */
const BRACKET_OPTIONS = [
    { key: 'Z', label: 'Z → F (Round of 64 through Finals)' },
    { key: 'Y', label: 'Y → F (Round of 32 through Finals)' },
    { key: 'X', label: 'X → F (Round of 16 through Finals)' },
    { key: 'Q', label: 'Q → F (Quarterfinals through Finals)' },
    { key: 'S', label: 'S → F (Semifinals through Finals)' },
    { key: 'F', label: 'F (Finals only)' }
];

type TabId = 'pairings' | 'teams' | 'wpw';

@Component({
    selector: 'app-manage-pairings',
    standalone: true,
    imports: [CommonModule, FormsModule, DivisionNavigatorComponent],
    templateUrl: './manage-pairings.component.html',
    styleUrl: './manage-pairings.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ManagePairingsComponent implements OnInit {
    private readonly svc = inject(PairingsService);

    // ── Navigator state ──
    readonly agegroups = signal<AgegroupWithDivisionsDto[]>([]);
    readonly selectedDivision = signal<DivisionSummaryDto | null>(null);

    // ── Tab state ──
    readonly activeTab = signal<TabId>('pairings');

    // ── Pairings state ──
    readonly divisionResponse = signal<DivisionPairingsResponse | null>(null);
    readonly pairings = signal<PairingDto[]>([]);
    readonly isLoading = signal(false);
    readonly isNavLoading = signal(false);

    // ── Who Plays Who ──
    readonly whoPlaysWhoMatrix = signal<number[][] | null>(null);

    // ── Add Block controls ──
    readonly blockRounds = signal(1);
    readonly showBlockDropdown = signal(false);
    readonly isAddingBlock = signal(false);

    // ── Add Elimination controls ──
    readonly bracketOptions = BRACKET_OPTIONS;
    readonly showBracketDropdown = signal(false);
    readonly isAddingElimination = signal(false);

    // ── Add Single ──
    readonly isAddingSingle = signal(false);

    // ── Remove All ──
    readonly isRemovingAll = signal(false);
    readonly showRemoveConfirm = signal(false);

    // ── Inline editing ──
    readonly editingAi = signal<number | null>(null);
    readonly isSavingEdit = signal(false);

    // ── Division Teams ──
    readonly divisionTeams = signal<DivisionTeamDto[]>([]);
    readonly editingTeamId = signal<string | null>(null);
    readonly isSavingTeam = signal(false);

    // ── Computed: separate round-robin from bracket pairings ──
    readonly roundRobinPairings = computed(() =>
        this.pairings().filter(p => p.t1Type === 'T' && p.t2Type === 'T')
    );

    readonly bracketPairings = computed(() =>
        this.pairings().filter(p => p.t1Type !== 'T' || p.t2Type !== 'T')
    );

    readonly teamCount = computed(() => this.divisionResponse()?.teamCount ?? 0);

    readonly teamRange = computed(() => {
        const tc = this.teamCount();
        return Array.from({ length: tc }, (_, i) => i + 1);
    });

    readonly rankOptions = computed(() => {
        const count = this.divisionTeams().length;
        return Array.from({ length: count }, (_, i) => i + 1);
    });

    ngOnInit(): void {
        this.loadAgegroups();
    }

    // ── Navigator ──

    loadAgegroups(): void {
        this.isNavLoading.set(true);
        this.svc.getAgegroups().subscribe({
            next: (data) => {
                const filtered = data
                    .filter(ag => {
                        const name = (ag.agegroupName ?? '').toUpperCase();
                        return name !== 'DROPPED TEAMS' && !name.startsWith('WAITLIST');
                    })
                    .map(ag => ({
                        ...ag,
                        divisions: ag.divisions.filter(d =>
                            (d.divName ?? '').toUpperCase() !== 'UNASSIGNED'
                        )
                    }))
                    .filter(ag => ag.divisions.length > 0)
                    .sort((a, b) => (a.agegroupName ?? '').localeCompare(b.agegroupName ?? ''));
                this.agegroups.set(filtered);
                this.isNavLoading.set(false);
            },
            error: () => this.isNavLoading.set(false)
        });
    }

    onDivisionSelected(event: { division: DivisionSummaryDto; agegroupId: string }): void {
        this.selectedDivision.set(event.division);
        this.editingAi.set(null);
        this.editingTeamId.set(null);
        this.activeTab.set('pairings');
        this.whoPlaysWhoMatrix.set(null);
        this.divisionTeams.set([]);
        this.loadDivisionPairings(event.division.divId);
    }

    loadDivisionPairings(divId: string): void {
        this.isLoading.set(true);
        this.svc.getDivisionPairings(divId).subscribe({
            next: (resp) => {
                this.divisionResponse.set(resp);
                this.pairings.set(resp.pairings);
                this.isLoading.set(false);
            },
            error: () => this.isLoading.set(false)
        });
    }

    loadDivisionTeams(divId: string): void {
        this.svc.getDivisionTeams(divId).subscribe({
            next: (teams) => this.divisionTeams.set(teams),
            error: () => this.divisionTeams.set([])
        });
    }

    // ── Tabs ──

    setActiveTab(tab: TabId): void {
        this.activeTab.set(tab);

        if (tab === 'teams' && this.divisionTeams().length === 0) {
            const div = this.selectedDivision();
            if (div) this.loadDivisionTeams(div.divId);
        }

        if (tab === 'wpw' && !this.whoPlaysWhoMatrix()) {
            const tc = this.teamCount();
            if (tc > 0) {
                this.svc.getWhoPlaysWho(tc).subscribe({
                    next: (resp) => this.whoPlaysWhoMatrix.set(resp.matrix)
                });
            }
        }
    }

    // ── Add Block (Round-Robin) ──

    toggleBlockDropdown(): void {
        this.showBlockDropdown.update(v => !v);
        this.showBracketDropdown.set(false);
    }

    addBlock(): void {
        const tc = this.teamCount();
        if (tc === 0) return;

        this.isAddingBlock.set(true);
        this.showBlockDropdown.set(false);
        this.svc.addBlock({ noRounds: this.blockRounds(), teamCount: tc }).subscribe({
            next: (newPairings) => {
                this.pairings.update(curr => [...curr, ...newPairings]);
                this.isAddingBlock.set(false);
            },
            error: () => this.isAddingBlock.set(false)
        });
    }

    // ── Add Single-Elimination ──

    toggleBracketDropdown(): void {
        this.showBracketDropdown.update(v => !v);
        this.showBlockDropdown.set(false);
    }

    addElimination(startKey: string): void {
        const tc = this.teamCount();
        if (tc === 0) return;

        this.isAddingElimination.set(true);
        this.showBracketDropdown.set(false);
        this.svc.addElimination({ startKey, teamCount: tc }).subscribe({
            next: (newPairings) => {
                this.pairings.update(curr => [...curr, ...newPairings]);
                this.isAddingElimination.set(false);
            },
            error: () => this.isAddingElimination.set(false)
        });
    }

    // ── Add Single Pairing ──

    addSingle(): void {
        const tc = this.teamCount();
        if (tc === 0) return;

        this.isAddingSingle.set(true);
        this.svc.addSingle({ teamCount: tc }).subscribe({
            next: (pairing) => {
                this.pairings.update(curr => [...curr, pairing]);
                this.isAddingSingle.set(false);
            },
            error: () => this.isAddingSingle.set(false)
        });
    }

    // ── Remove All ──

    confirmRemoveAll(): void {
        this.showRemoveConfirm.set(true);
    }

    cancelRemoveAll(): void {
        this.showRemoveConfirm.set(false);
    }

    removeAll(): void {
        const tc = this.teamCount();
        if (tc === 0) return;

        this.isRemovingAll.set(true);
        this.showRemoveConfirm.set(false);
        this.svc.removeAll({ teamCount: tc }).subscribe({
            next: () => {
                this.pairings.set([]);
                this.isRemovingAll.set(false);
            },
            error: () => this.isRemovingAll.set(false)
        });
    }

    // ── Delete single pairing ──

    deletePairing(ai: number): void {
        this.svc.deletePairing(ai).subscribe({
            next: () => {
                this.pairings.update(curr => curr.filter(p => p.ai !== ai));
            }
        });
    }

    // ── Inline editing ──

    startEdit(ai: number): void {
        this.editingAi.set(ai);
    }

    cancelEdit(): void {
        this.editingAi.set(null);
        // Reload to discard changes
        const div = this.selectedDivision();
        if (div) this.loadDivisionPairings(div.divId);
    }

    saveEdit(pairing: PairingDto): void {
        this.isSavingEdit.set(true);
        this.svc.editPairing({
            ai: pairing.ai,
            gameNumber: pairing.gameNumber,
            rnd: pairing.rnd,
            t1: pairing.t1,
            t2: pairing.t2,
            t1Type: pairing.t1Type,
            t2Type: pairing.t2Type,
            t1GnoRef: pairing.t1GnoRef,
            t2GnoRef: pairing.t2GnoRef,
            t1CalcType: pairing.t1CalcType,
            t2CalcType: pairing.t2CalcType,
            t1Annotation: pairing.t1Annotation,
            t2Annotation: pairing.t2Annotation
        }).subscribe({
            next: () => {
                this.editingAi.set(null);
                this.isSavingEdit.set(false);
            },
            error: () => this.isSavingEdit.set(false)
        });
    }

    // ── Division Teams ──

    startTeamEdit(teamId: string): void {
        this.editingTeamId.set(teamId);
    }

    cancelTeamEdit(): void {
        this.editingTeamId.set(null);
        // Reload to discard unsaved changes
        const div = this.selectedDivision();
        if (div) this.loadDivisionTeams(div.divId);
    }

    saveTeamEdit(team: DivisionTeamDto): void {
        this.isSavingTeam.set(true);
        this.svc.editDivisionTeam({
            teamId: team.teamId,
            divRank: team.divRank,
            teamName: team.teamName
        }).subscribe({
            next: (updatedTeams) => {
                this.divisionTeams.set(updatedTeams);
                this.editingTeamId.set(null);
                this.isSavingTeam.set(false);
            },
            error: () => this.isSavingTeam.set(false)
        });
    }

    // ── Helpers ──

    typeLabel(code: string): string {
        return TYPE_LABELS[code] ?? code;
    }

    typeShort(t1Type: string, t2Type: string): string {
        return `${t1Type}▸${t2Type}`;
    }

    gameRef(gnoRef: number | null | undefined, calcType: string | null | undefined): string {
        if (!gnoRef) return '';
        const calc = calcType === 'W' ? 'Winner' : calcType === 'L' ? 'Loser' : calcType ?? '';
        return `${calc} of G${gnoRef}`;
    }

    roundClass(rnd: number): string {
        return rnd % 2 === 0 ? 'round-even' : 'round-odd';
    }
}
