import { Component, ChangeDetectionStrategy, input, output, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import type { PairingDto, DivisionTeamDto } from '../../services/schedule-division.service';
import { teamDes } from '../../../shared/utils/scheduling-helpers';
import { WpwMatrixComponent } from '../../../shared/components/wpw-matrix/wpw-matrix.component';
import { DivisionTeamsTableComponent } from '../../../shared/components/division-teams-table/division-teams-table.component';

@Component({
    selector: 'app-pairings-panel',
    standalone: true,
    imports: [CommonModule, WpwMatrixComponent, DivisionTeamsTableComponent],
    templateUrl: './pairings-panel.component.html',
    styleUrl: './pairings-panel.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class PairingsPanelComponent {
    // ── Inputs ──
    readonly pairings = input<PairingDto[]>([]);
    readonly isPairingsLoading = input(false);
    readonly placementMode = input<'mouse' | 'keyboard'>('mouse');
    readonly selectedPairingAi = input<number | null>(null);
    readonly teamCount = input(0);
    readonly whoPlaysWhoMatrix = input<number[][] | null>(null);
    readonly divisionTeams = input<DivisionTeamDto[]>([]);

    // ── Outputs ──
    readonly pairingClicked = output<PairingDto>();
    readonly placementModeChanged = output<'mouse' | 'keyboard'>();
    readonly teamEditRequested = output<DivisionTeamDto>();
    readonly pairingLocated = output<PairingDto>();

    // ── Derived: split pairings into RR vs Championship ──
    readonly rrPairings = computed(() => this.pairings().filter(p => p.t1Type === 'T'));
    readonly champPairings = computed(() => this.pairings().filter(p => p.t1Type !== 'T'));
    readonly allRrScheduled = computed(() => {
        const rr = this.rrPairings();
        return rr.length > 0 && rr.every(p => !p.bAvailable);
    });
    readonly rrUnplacedCount = computed(() => this.rrPairings().filter(p => p.bAvailable).length);
    readonly champUnplacedCount = computed(() => this.champPairings().filter(p => p.bAvailable).length);

    // ── Local UI state ──
    readonly whoPlaysWhoOpen = signal(false);
    readonly divisionTeamsOpen = signal(false);
    readonly rrSectionOpen = signal<boolean | null>(null); // null = use auto default

    // ── Helpers ──
    readonly teamDes = teamDes;

    /** RR section: open by default unless all RR are scheduled (then collapsed). Manual toggle overrides. */
    isRrOpen(): boolean {
        const manual = this.rrSectionOpen();
        if (manual !== null) return manual;
        return !this.allRrScheduled();
    }

    toggleRrSection(): void {
        this.rrSectionOpen.set(!this.isRrOpen());
    }

    isPairingSelected(pairing: PairingDto): boolean {
        return this.selectedPairingAi() === pairing.ai;
    }

    onPairingClick(pairing: PairingDto): void {
        if (pairing.bAvailable) {
            this.pairingClicked.emit(pairing);
        } else {
            this.pairingLocated.emit(pairing);
        }
    }
}
