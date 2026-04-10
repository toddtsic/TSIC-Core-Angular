import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import type { ClubRosterTeamDto } from '@core/api/models/ClubRosterTeamDto';

@Component({
    selector: 'app-team-dropdown',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <select class="field-select" [value]="value()" (change)="onChange($event)">
            <option value="" disabled selected>{{ placeholder() }}</option>
            @for (team of teams(); track team.teamId) {
                <option [value]="team.teamId">
                    {{ team.agegroupName }} — {{ team.teamName }} ({{ team.playerCount }})
                </option>
            }
        </select>
    `
})
export class TeamDropdownComponent {
    readonly teams = input.required<ClubRosterTeamDto[]>();
    readonly value = input<string | null>(null);
    readonly placeholder = input('Select a team');

    readonly valueChange = output<string>();

    onChange(event: Event): void {
        const val = (event.target as HTMLSelectElement).value;
        if (val) this.valueChange.emit(val);
    }
}
