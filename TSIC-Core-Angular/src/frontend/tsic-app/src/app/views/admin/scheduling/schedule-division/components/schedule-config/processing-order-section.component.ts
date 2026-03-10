import {
    ChangeDetectionStrategy, Component, computed, effect, input, output, signal, untracked
} from '@angular/core';
import { DragDropModule, CdkDragDrop, moveItemInArray } from '@angular/cdk/drag-drop';
import type { AgegroupWithDivisionsDto } from '@core/api/models/AgegroupWithDivisionsDto';
import type { AgegroupCanvasReadinessDto } from '@core/api';

// ── Local item shape for the reorderable list ──
interface DivisionOrderItem {
    divId: string;
    divName: string;
    agegroupId: string;
    agegroupName: string;
    agegroupColor: string | null;
    teamCount: number;
    wave: number;
    /** ISO dates this division plays on (derived from agegroup gameDays) */
    playDates: string[];
}

interface DayGroup {
    isoDate: string;
    dow: string;
    dateLabel: string;
    waveGroups: { wave: number; items: DivisionOrderItem[] }[];
    /** True when all items in this day share the same wave */
    singleWave: boolean;
}

@Component({
    selector: 'app-processing-order-section',
    standalone: true,
    imports: [DragDropModule],
    templateUrl: './processing-order-section.component.html',
    styleUrl: './processing-order-section.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ProcessingOrderSectionComponent {

    // ── Inputs ──
    readonly agegroups = input<AgegroupWithDivisionsDto[]>([]);
    readonly suggestedDivisionOrder = input<string[]>([]);
    readonly waveAssignments = input<Record<string, number>>({});
    readonly readinessMap = input<Record<string, AgegroupCanvasReadinessDto>>({});
    readonly isExpanded = input(false);

    // ── Outputs ──
    readonly toggleExpanded = output<void>();
    readonly orderChanged = output<string[]>();

    // ── Local state ──
    readonly localOrder = signal<DivisionOrderItem[]>([]);
    private readonly baselineOrder = signal<string[]>([]);

    // ── Computed ──
    readonly isDirty = computed(() => {
        const current = this.localOrder().map(i => i.divId);
        const baseline = this.baselineOrder();
        if (current.length !== baseline.length) return false; // structural mismatch, not user edit
        return current.some((id, i) => id !== baseline[i]);
    });

    readonly dayGroups = computed((): DayGroup[] => {
        const items = this.localOrder();
        if (items.length === 0) return [];

        // Collect all unique dates across all items, sorted chronologically
        const dateSet = new Map<string, string>(); // isoDate → dow
        for (const item of items) {
            for (const d of item.playDates) {
                if (!dateSet.has(d)) {
                    // Derive dow from readinessMap gameDays
                    const dow = this.getDowForDate(d);
                    dateSet.set(d, dow);
                }
            }
        }

        const sortedDates = [...dateSet.entries()].sort(([a], [b]) => a.localeCompare(b));

        return sortedDates.map(([isoDate, dow]) => {
            // Items playing on this date
            const dayItems = items.filter(i => i.playDates.includes(isoDate));

            // Group by wave
            const waveMap = new Map<number, DivisionOrderItem[]>();
            for (const item of dayItems) {
                const arr = waveMap.get(item.wave) ?? [];
                arr.push(item);
                waveMap.set(item.wave, arr);
            }

            const waveGroups = [...waveMap.entries()]
                .sort(([a], [b]) => a - b)
                .map(([wave, waveItems]) => ({ wave, items: waveItems }));

            return {
                isoDate,
                dow,
                dateLabel: this.formatDateLabel(isoDate, dow),
                waveGroups,
                singleWave: waveGroups.length <= 1
            };
        });
    });

    readonly summaryLabel = computed((): string => {
        const items = this.localOrder();
        if (items.length === 0) return 'No divisions';
        const dayCount = new Set(items.flatMap(i => i.playDates)).size;
        if (dayCount <= 1) return `${items.length} divisions`;
        return `${items.length} divisions across ${dayCount} days`;
    });

    readonly isComplete = computed(() => this.localOrder().length > 0);

    readonly sourceBadge = computed((): string | null => {
        if (this.isDirty()) return 'Custom order';
        if (this.suggestedDivisionOrder().length > 0) return 'From source';
        return null;
    });

    constructor() {
        // Re-build localOrder when inputs change
        effect(() => {
            const ags = this.agegroups();
            const suggestedOrder = this.suggestedDivisionOrder();
            const waves = this.waveAssignments();
            const readiness = this.readinessMap();

            // Build flat list of all divisions
            const allDivs: DivisionOrderItem[] = [];
            for (const ag of ags) {
                if (!ag.divisions) continue;
                // Get play dates for this agegroup from readinessMap
                const r = readiness[ag.agegroupId];
                const playDates = r?.gameDays?.map(gd => gd.date) ?? [];

                for (const div of ag.divisions) {
                    allDivs.push({
                        divId: div.divId,
                        divName: div.divName,
                        agegroupId: ag.agegroupId,
                        agegroupName: ag.agegroupName,
                        agegroupColor: ag.color ?? null,
                        teamCount: div.teamCount,
                        wave: waves[div.divId] ?? 1,
                        playDates
                    });
                }
            }

            // Sort by suggested order if available, else by wave → AG name → div name
            if (suggestedOrder.length > 0) {
                const orderMap = new Map(suggestedOrder.map((id, i) => [id, i]));
                allDivs.sort((a, b) => {
                    const aIdx = orderMap.get(a.divId) ?? 9999;
                    const bIdx = orderMap.get(b.divId) ?? 9999;
                    return aIdx - bIdx;
                });
            } else {
                allDivs.sort((a, b) => {
                    if (a.wave !== b.wave) return a.wave - b.wave;
                    const agCmp = a.agegroupName.localeCompare(b.agegroupName);
                    return agCmp !== 0 ? agCmp : a.divName.localeCompare(b.divName);
                });
            }

            untracked(() => {
                this.localOrder.set(allDivs);
                this.baselineOrder.set(allDivs.map(i => i.divId));
            });
        });
    }

    // ── Drag-drop handler (constrained within day+wave) ──
    onDrop(event: CdkDragDrop<DivisionOrderItem[]>, isoDate: string, wave: number): void {
        if (event.previousIndex === event.currentIndex) return;

        const all = this.localOrder().slice();

        // Extract items for this day+wave, preserving their positions in the full array
        const matchIndices: number[] = [];
        const matchItems: DivisionOrderItem[] = [];
        for (let i = 0; i < all.length; i++) {
            if (all[i].wave === wave && all[i].playDates.includes(isoDate)) {
                matchIndices.push(i);
                matchItems.push(all[i]);
            }
        }

        moveItemInArray(matchItems, event.previousIndex, event.currentIndex);

        // Put reordered items back into their original positions in the full array
        for (let i = 0; i < matchIndices.length; i++) {
            all[matchIndices[i]] = matchItems[i];
        }

        this.localOrder.set(all);
    }

    onResetToSource(): void {
        const suggestedOrder = this.suggestedDivisionOrder();
        const items = this.localOrder().slice();

        if (suggestedOrder.length > 0) {
            const orderMap = new Map(suggestedOrder.map((id, i) => [id, i]));
            items.sort((a, b) => {
                const aIdx = orderMap.get(a.divId) ?? 9999;
                const bIdx = orderMap.get(b.divId) ?? 9999;
                return aIdx - bIdx;
            });
        } else {
            items.sort((a, b) => {
                if (a.wave !== b.wave) return a.wave - b.wave;
                const agCmp = a.agegroupName.localeCompare(b.agegroupName);
                return agCmp !== 0 ? agCmp : a.divName.localeCompare(b.divName);
            });
        }

        this.localOrder.set(items);
        this.baselineOrder.set(items.map(i => i.divId));
    }

    onApply(): void {
        // Flatten day groups in order: day → wave → item position
        const dayGs = this.dayGroups();
        const seen = new Set<string>();
        const order: string[] = [];

        for (const day of dayGs) {
            for (const wg of day.waveGroups) {
                for (const item of wg.items) {
                    if (!seen.has(item.divId)) {
                        seen.add(item.divId);
                        order.push(item.divId);
                    }
                }
            }
        }

        this.orderChanged.emit(order);
        this.baselineOrder.set(order);
    }

    onToggle(): void {
        this.toggleExpanded.emit();
    }

    // ── Contrast text helper (same as calendar-section) ──
    contrastText(hex: string | null | undefined): string {
        if (!hex) return '#fff';
        const r = parseInt(hex.slice(1, 3), 16);
        const g = parseInt(hex.slice(3, 5), 16);
        const b = parseInt(hex.slice(5, 7), 16);
        return (r * 299 + g * 587 + b * 114) / 1000 > 128 ? '#000' : '#fff';
    }

    /** Unique cdkDropList ID per day+wave combo */
    dropListId(isoDate: string, wave: number): string {
        return `order-${isoDate}-w${wave}`;
    }

    // ── Private helpers ──

    private getDowForDate(isoDate: string): string {
        const readiness = this.readinessMap();
        for (const r of Object.values(readiness)) {
            if (!r.gameDays) continue;
            const gd = r.gameDays.find(d => d.date === isoDate);
            if (gd) return gd.dow;
        }
        // Fallback: derive from Date object
        const d = new Date(isoDate + 'T12:00:00');
        return d.toLocaleDateString('en-US', { weekday: 'long' });
    }

    private formatDateLabel(isoDate: string, dow: string): string {
        // Format: "Saturday 06/07/2026"
        const parts = isoDate.split('-');
        if (parts.length === 3) {
            return `${dow} ${parts[1]}/${parts[2]}/${parts[0]}`;
        }
        return `${dow} ${isoDate}`;
    }
}
