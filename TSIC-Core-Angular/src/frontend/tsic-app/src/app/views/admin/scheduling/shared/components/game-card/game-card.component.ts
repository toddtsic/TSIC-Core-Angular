import { Component, input, output, ChangeDetectionStrategy } from '@angular/core';
import type { ScheduleGameDto } from '@core/api';
import { agBg, teamDes } from '../../utils/scheduling-helpers';

@Component({
    selector: 'app-game-card',
    standalone: true,
    templateUrl: './game-card.component.html',
    styleUrl: './game-card.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class GameCardComponent {

    // ── Inputs ──
    readonly game = input.required<ScheduleGameDto>();
    readonly showMatchupCodes = input(false);
    readonly showGameId = input(false);
    readonly showTeamDesignators = input(false);
    readonly showActions = input(false);
    readonly isSelected = input(false);
    readonly isOtherDivision = input(false);
    readonly showSwapHint = input(false);
    readonly conflictIcons = input<{ slotCollision: boolean; timeClash: boolean; backToBack: boolean }>(
        { slotCollision: false, timeClash: false, backToBack: false }
    );

    // ── Outputs ──
    readonly moveClicked = output<void>();
    readonly deleteClicked = output<void>();

    // ── Helpers ──
    readonly teamDes = teamDes;
    readonly agBg = agBg;
}
