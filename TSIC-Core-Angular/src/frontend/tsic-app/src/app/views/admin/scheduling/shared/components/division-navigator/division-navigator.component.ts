import { Component, input, output, signal, ChangeDetectionStrategy } from '@angular/core';
import type { AgegroupWithDivisionsDto, DivisionSummaryDto } from '@core/api';
import { contrastText, agTeamCount } from '../../utils/scheduling-helpers';
import type { ScheduleScope } from '../../utils/scheduling-helpers';

@Component({
    selector: 'app-division-navigator',
    standalone: true,
    templateUrl: './division-navigator.component.html',
    styleUrl: './division-navigator.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class DivisionNavigatorComponent {

    // ── Inputs ──
    readonly agegroups = input.required<AgegroupWithDivisionsDto[]>();
    readonly selectedScope = input<ScheduleScope>({ level: 'event' });
    readonly jobName = input<string>('');
    readonly isLoading = input(false);
    readonly showCollapseAll = input(true);

    // ── Outputs ──
    readonly eventSelected = output<void>();
    readonly agegroupSelected = output<{ agegroupId: string }>();
    readonly divisionSelected = output<{ division: DivisionSummaryDto; agegroupId: string }>();

    // ── Internal state ──
    readonly expandedAgegroups = signal<Set<string>>(new Set());

    // ── Helpers (bound as readonly for template) ──
    readonly contrastText = contrastText;
    readonly agTeamCount = agTeamCount;

    // ── Methods ──

    toggleAgegroup(agId: string): void {
        const current = new Set(this.expandedAgegroups());
        if (current.has(agId)) current.delete(agId);
        else current.add(agId);
        this.expandedAgegroups.set(current);
    }

    selectAgegroup(agId: string): void {
        const current = new Set(this.expandedAgegroups());
        current.add(agId);
        this.expandedAgegroups.set(current);
        this.agegroupSelected.emit({ agegroupId: agId });
    }

    isExpanded(agId: string): boolean {
        return this.expandedAgegroups().has(agId);
    }

    collapseAll(): void {
        this.expandedAgegroups.set(new Set());
    }

    selectDivision(div: DivisionSummaryDto, agegroupId: string): void {
        this.divisionSelected.emit({ division: div, agegroupId });
    }

    isAgActive(agId: string): boolean {
        const s = this.selectedScope();
        return s.level === 'agegroup' && s.agegroupId === agId;
    }

    isDivActive(divId: string): boolean {
        const s = this.selectedScope();
        return s.level === 'division' && s.divId === divId;
    }
}
