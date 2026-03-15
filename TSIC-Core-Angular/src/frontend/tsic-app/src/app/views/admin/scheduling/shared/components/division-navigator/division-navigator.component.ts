import { Component, input, output, signal, ChangeDetectionStrategy, HostListener, ElementRef, inject } from '@angular/core';
import type { AgegroupWithDivisionsDto, AgegroupCanvasReadinessDto, DivisionSummaryDto } from '@core/api';
import { contrastText, agTeamCount, AGEGROUP_COLORS } from '../../utils/scheduling-helpers';
import type { ScheduleScope } from '../../utils/scheduling-helpers';

@Component({
    selector: 'app-division-navigator',
    standalone: true,
    templateUrl: './division-navigator.component.html',
    styleUrl: './division-navigator.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class DivisionNavigatorComponent {
    private readonly el = inject(ElementRef);

    // ── Inputs ──
    readonly agegroups = input.required<AgegroupWithDivisionsDto[]>();
    readonly selectedScope = input<ScheduleScope>({ level: 'event' });
    readonly jobName = input<string>('');
    readonly isLoading = input(false);
    readonly showCollapseAll = input(true);
    readonly readinessMap = input<Record<string, AgegroupCanvasReadinessDto>>({});

    // ── Outputs ──
    readonly eventSelected = output<void>();
    readonly agegroupSelected = output<{ agegroupId: string }>();
    readonly divisionSelected = output<{ division: DivisionSummaryDto; agegroupId: string }>();
    readonly colorChanged = output<{ agegroupId: string; color: string | null }>();

    // ── Internal state ──
    readonly expandedAgegroups = signal<Set<string>>(new Set());
    readonly colorPickerAgId = signal<string | null>(null);

    // ── Helpers (bound as readonly for template) ──
    readonly contrastText = contrastText;
    readonly agTeamCount = agTeamCount;
    readonly colorOptions = AGEGROUP_COLORS;

    // ── Close color picker on outside click ──
    @HostListener('document:click', ['$event'])
    onDocClick(e: MouseEvent): void {
        if (this.colorPickerAgId() && !this.el.nativeElement.contains(e.target)) {
            this.colorPickerAgId.set(null);
        }
    }

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

    /** null = no readiness data yet, true = configured, false = not configured */
    isAgConfigured(agId: string): boolean | null {
        const map = this.readinessMap();
        if (!map || Object.keys(map).length === 0) return null;
        return map[agId]?.isConfigured ?? false;
    }

    readinessTooltip(agId: string): string {
        const map = this.readinessMap();
        const r = map?.[agId];
        if (!r) return 'Not configured — click to set up';
        if (r.isConfigured) return `${r.dateCount} dates, ${r.fieldCount} field schedules`;
        const parts: string[] = [];
        if (r.dateCount === 0) parts.push('no dates');
        if (r.fieldCount === 0) parts.push('no field schedules');
        return `Incomplete — ${parts.join(', ')}`;
    }

    // ── Color picker ──

    openColorPicker(agId: string, e: MouseEvent): void {
        e.preventDefault();
        e.stopPropagation();
        this.colorPickerAgId.set(this.colorPickerAgId() === agId ? null : agId);
    }

    selectColor(agId: string, color: string | null): void {
        this.colorPickerAgId.set(null);
        this.colorChanged.emit({ agegroupId: agId, color: color?.toUpperCase() ?? null });
    }

    getColorName(hex: string | null | undefined): string {
        if (!hex) return 'None';
        return this.colorOptions.find(c => c.value === hex.toUpperCase())?.name ?? hex;
    }
}
