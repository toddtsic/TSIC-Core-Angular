import { ChangeDetectionStrategy, Component, computed, input, OnChanges, signal, SimpleChanges, output } from '@angular/core';
import type { DivisionBracketResponse } from '@core/api';
import { contrastText } from '../../shared/utils/scheduling-helpers';
import { AgeGroupPickerComponent, type AgePickerItem } from '../../shared/components/age-group-picker/age-group-picker.component';
import { BracketViewComponent, type BracketScoreEdit } from './bracket-view.component';

@Component({
    selector: 'app-brackets-tab',
    standalone: true,
    imports: [AgeGroupPickerComponent, BracketViewComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @if (isLoading()) {
            <div class="loading-container">
                <span class="spinner-border spinner-border-sm" role="status"></span>
                Loading brackets...
            </div>
        } @else if (brackets().length === 0) {
            <div class="empty-state">No bracket data available.</div>
        } @else {
            <!-- Age-group nav: scrollable pill strip on desktop, compact dot+name
                 dropdown on mobile. Both drive the same selectTab. -->
            <div class="ag-picker-bar">
                <app-age-group-picker stepper noun="bracket"
                    [items]="agePickerItems()"
                    [selectedId]="activeAgId()"
                    (selectionChange)="onAgePicked($event)" />
            </div>
            <div class="ag-tabs">
                @for (tab of tabItems(); track tab.index) {
                    <button class="ag-tab"
                            [class.active]="activeTabIndex() === tab.index"
                            (click)="selectTab(tab.index)">
                        <span class="ag-dot" [class.ag-dot--empty]="!tab.color"
                              [style.background]="tab.color || null"></span>
                        {{ tab.label }}
                    </button>
                }
            </div>

            <app-bracket-view
                [bracket]="activeBracket()"
                [canScore]="canScore()"
                [agColor]="activeAgColor()"
                [followedTeamIds]="followedTeamIds()"
                (editBracketScore)="editBracketScore.emit($event)"
                (viewTeamResults)="viewTeamResults.emit($event)"
                (viewFieldInfo)="viewFieldInfo.emit($event)" />
        }
    `,
    styles: [`
        :host { display: block; }

        .loading-container {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            padding: var(--space-4);
            color: var(--bs-secondary-color);
        }

        .empty-state {
            padding: var(--space-4);
            color: var(--bs-secondary-color);
            text-align: center;
        }

        /* ── Tab bar ── */

        /* Mobile-only dropdown bar; the pill strip owns desktop (see mobile @media). */
        .ag-picker-bar { display: none; }

        .ag-tabs {
            display: flex;
            gap: var(--space-1);
            padding: var(--space-2) var(--space-3);
            border-bottom: 1px solid var(--bs-border-color);
            overflow-x: auto;
            scrollbar-width: thin;
        }

        .ag-tab {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
            padding: var(--space-1) var(--space-3);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-sm);
            background: var(--bs-body-bg);
            color: var(--bs-secondary-color);
            font-size: var(--font-size-sm);
            font-weight: 500;
            cursor: pointer;
            white-space: nowrap;
            transition: background-color 0.15s, color 0.15s, border-color 0.15s, box-shadow 0.15s, opacity 0.15s;
        }

        .ag-tab:hover {
            background: var(--bs-secondary-bg);
            color: var(--bs-body-color);
        }

        /* Age-group identity is carried by a quiet color DOT, not a flooded pill — one
           affordance language shared with the mobile picker and the standings strip.
           Every chip is neutral; the active one gets a primary border + bold. */
        .ag-tab.active {
            background: var(--bs-primary-bg-subtle);
            border-color: var(--bs-primary);
            box-shadow: inset 0 0 0 1px var(--bs-primary);
            color: var(--bs-body-color);
            font-weight: 700;
        }

        /* Small filled dot of the age-group's color; inset hairline ring keeps light
           dots visible; dashed neutral ring when the group has no color set. */
        .ag-dot {
            flex-shrink: 0;
            display: inline-block;
            width: 10px;
            height: 10px;
            border-radius: 50%;
            background: var(--bs-secondary-bg);
            box-shadow: inset 0 0 0 1px var(--bs-border-color);
        }
        .ag-dot--empty {
            background: transparent;
            box-shadow: none;
            border: 1px dashed var(--bs-border-color);
        }

        .ag-tab:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
        }

        @media (prefers-reduced-motion: reduce) {
            .ag-tab { transition: none !important; }
        }

        /* Swap the pill strip for the dropdown at phone width. */
        @media (max-width: 767px) {
            .ag-tabs { display: none; }
            .ag-picker-bar {
                display: flex;
                justify-content: flex-start;
                padding: var(--space-2) var(--space-3);
                border-bottom: 1px solid var(--bs-border-color);
            }
        }
    `]
})
export class BracketsTabComponent implements OnChanges {
    brackets = input<DivisionBracketResponse[]>([]);
    canScore = input<boolean>(false);
    isLoading = input<boolean>(false);
    agegroupColors = input<Record<string, string | null>>({});
    followedTeamIds = input<readonly string[]>([]);

    readonly editBracketScore = output<BracketScoreEdit>();
    readonly viewTeamResults = output<string>();
    readonly viewFieldInfo = output<string>();

    // ── Tab state ──
    readonly activeTabIndex = signal(0);

    readonly tabItems = computed(() => {
        const data = this.brackets();
        const colors = this.agegroupColors();
        return data.map((b, i) => {
            const color = colors[b.agegroupName] ?? null;
            return {
                index: i,
                label: b.divName ? `${b.agegroupName} — ${b.divName}` : b.agegroupName,
                champion: b.champion ?? null,
                color,
                contrastColor: contrastText(color)
            };
        });
    });

    readonly activeBracket = computed(() => {
        const data = this.brackets();
        const idx = this.activeTabIndex();
        return data[idx] ?? null;
    });

    // Agegroup tint for the active division.
    readonly activeAgColor = computed(() =>
        this.agegroupColors()[this.activeBracket()?.agegroupName ?? ''] ?? null);

    ngOnChanges(changes: SimpleChanges): void {
        if (changes['brackets']) this.activeTabIndex.set(0);
    }

    selectTab(index: number): void {
        this.activeTabIndex.set(index);
    }

    /** Mobile dropdown feed — same age groups as the pill strip, id = tab index. */
    readonly agePickerItems = computed<AgePickerItem[]>(() =>
        this.tabItems().map(t => ({ id: String(t.index), label: t.label, color: t.color }))
    );
    readonly activeAgId = computed(() => String(this.activeTabIndex()));
    onAgePicked(id: string): void {
        this.selectTab(Number(id));
    }
}
