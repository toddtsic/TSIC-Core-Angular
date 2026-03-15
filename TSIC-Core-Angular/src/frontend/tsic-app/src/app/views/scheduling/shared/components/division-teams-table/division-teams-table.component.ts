import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import type { DivisionTeamDto } from '@core/api';

@Component({
    selector: 'app-division-teams-table',
    standalone: true,
    templateUrl: './division-teams-table.component.html',
    styleUrl: './division-teams-table.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class DivisionTeamsTableComponent {
    readonly teams = input<DivisionTeamDto[]>([]);
    readonly isLoading = input(false);

    /** Emitted when the pencil button or double-click fires on a team row. */
    readonly teamEditRequested = output<DivisionTeamDto>();
}
