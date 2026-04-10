import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { DropDownListModule, ChangeEventArgs } from '@syncfusion/ej2-angular-dropdowns';
import type { ClubRosterTeamDto } from '@core/api/models/ClubRosterTeamDto';

@Component({
    selector: 'app-team-dropdown',
    standalone: true,
    imports: [DropDownListModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <ejs-dropdownlist
            [dataSource]="teams()"
            [fields]="{ text: 'teamName', value: 'teamId' }"
            [value]="value()"
            (change)="onDdlChange($event)"
            [allowFiltering]="true"
            [filterBarPlaceholder]="'Type to search...'"
            [placeholder]="placeholder()"
            [popupWidth]="'100%'"
            cssClass="field-ddl">
            <ng-template #itemTemplate let-data>
                <div class="team-item">
                    <span class="team-item__badge">{{ data.agegroupName }}</span>
                    <span class="team-item__name">{{ data.teamName }}</span>
                    <span class="team-item__count">({{ data.playerCount }})</span>
                </div>
            </ng-template>
        </ejs-dropdownlist>
    `,
    styles: [`
        :host { display: block; }

        :host ::ng-deep .e-popup {
            z-index: 100001 !important;
        }

        :host ::ng-deep .team-item {
            display: flex;
            align-items: center;
            gap: var(--space-1);
            font-size: var(--font-size-xs);
        }

        :host ::ng-deep .team-item__badge {
            padding: 0 var(--space-1);
            border-radius: var(--radius-full);
            background: rgba(var(--bs-primary-rgb), 0.12);
            color: var(--bs-primary);
            font-size: 10px;
            font-weight: var(--font-weight-semibold);
            white-space: nowrap;
        }

        :host ::ng-deep .team-item__name {
            font-weight: var(--font-weight-medium);
        }

        :host ::ng-deep .team-item__count {
            color: var(--text-secondary);
            font-size: 10px;
        }
    `]
})
export class TeamDropdownComponent {
    readonly teams = input.required<ClubRosterTeamDto[]>();
    readonly value = input<string | null>(null);
    readonly placeholder = input('Select a team');

    readonly valueChange = output<string>();

    onDdlChange(event: ChangeEventArgs): void {
        if (event.value) this.valueChange.emit(event.value as string);
    }
}
