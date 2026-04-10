import { Component, ChangeDetectionStrategy, input, output, ViewEncapsulation, ElementRef, inject } from '@angular/core';
import { DropDownListModule, ChangeEventArgs, PopupEventArgs } from '@syncfusion/ej2-angular-dropdowns';
import type { ClubRosterTeamDto } from '@core/api/models/ClubRosterTeamDto';

@Component({
    selector: 'app-team-dropdown',
    standalone: true,
    imports: [DropDownListModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    encapsulation: ViewEncapsulation.None,
    template: `
        <ejs-dropdownlist
            [dataSource]="teams()"
            [fields]="{ text: 'teamName', value: 'teamId' }"
            [value]="value()"
            (change)="onDdlChange($event)"
            (open)="onOpen($event)"
            [allowFiltering]="true"
            [filterBarPlaceholder]="'Type to search...'"
            [placeholder]="placeholder()"
            [popupWidth]="'100%'"
            [itemTemplate]="itemTpl"
            cssClass="team-ddl">
        </ejs-dropdownlist>
    `,
    styles: [`
        .team-ddl-item {
            display: flex;
            align-items: center;
            gap: 6px;
            font-size: 12px;
            line-height: 1.4;
        }

        .team-ddl-item__badge {
            display: inline-block;
            padding: 1px 6px;
            border-radius: 9999px;
            background: rgba(var(--bs-primary-rgb), 0.12);
            color: var(--bs-primary);
            font-size: 10px;
            font-weight: 600;
            white-space: nowrap;
        }

        .team-ddl-item__name {
            font-weight: 500;
        }

        .team-ddl-item__count {
            color: var(--text-secondary);
            font-size: 10px;
        }
    `]
})
export class TeamDropdownComponent {
    private readonly el = inject(ElementRef);

    readonly teams = input.required<ClubRosterTeamDto[]>();
    readonly value = input<string | null>(null);
    readonly placeholder = input('Select a team');

    readonly valueChange = output<string>();

    readonly itemTpl = '<div class="team-ddl-item">'
        + '<span class="team-ddl-item__badge">${agegroupName}</span>'
        + '<span class="team-ddl-item__name">${teamName}</span>'
        + '<span class="team-ddl-item__count">(${playerCount})</span>'
        + '</div>';

    onDdlChange(event: ChangeEventArgs): void {
        if (event.value) this.valueChange.emit(event.value as string);
    }

    /**
     * When this DDL lives inside a native <dialog> (top-layer), the SF popup
     * appends to <body> which sits below the top-layer — no z-index can fix this.
     * Solution: move the popup element inside the closest <dialog> ancestor so it
     * participates in the same top-layer stacking context.
     */
    onOpen(event: PopupEventArgs): void {
        const dialog = this.el.nativeElement.closest('dialog');
        if (dialog && event.popup) {
            dialog.appendChild(event.popup);
        }
    }
}
