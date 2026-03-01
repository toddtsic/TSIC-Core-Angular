import { Component, ChangeDetectionStrategy, input, output, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import type { PairingDto, DivisionTeamDto } from '../../services/schedule-division.service';
import { teamDes } from '../../../shared/utils/scheduling-helpers';

@Component({
    selector: 'app-pairings-panel',
    standalone: true,
    imports: [CommonModule],
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

    // ── Local UI state ──
    readonly whoPlaysWhoOpen = signal(false);
    readonly divisionTeamsOpen = signal(false);

    // ── Computed ──
    readonly teamRange = computed(() => Array.from({ length: this.teamCount() }, (_, i) => i + 1));
    readonly rankOptions = computed(() => Array.from({ length: this.divisionTeams().length }, (_, i) => i + 1));

    // ── Helpers ──
    readonly teamDes = teamDes;

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
