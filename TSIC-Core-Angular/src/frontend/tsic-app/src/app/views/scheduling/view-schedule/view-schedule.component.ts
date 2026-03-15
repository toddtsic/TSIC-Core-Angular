import {
    ChangeDetectionStrategy, Component, inject, OnInit, signal, computed
} from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { AuthService } from '../../../infrastructure/services/auth.service';
import { JobService } from '../../../infrastructure/services/job.service';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import type {
    ScheduleFilterOptionsDto,
    ScheduleFilterRequest,
    ScheduleCapabilitiesDto,
    CadtClubNode,
    ViewGameDto,
    StandingsByDivisionResponse,
    DivisionBracketResponse,
    ContactDto,
    TeamResultDto,
    FieldDisplayDto,
    EditScoreRequest,
    EditGameRequest,
    LadtAgegroupNode
} from '@core/api';
import { ViewScheduleService } from './services/view-schedule.service';
import { CadtTreeFilterComponent } from '../shared/components/cadt-tree-filter/cadt-tree-filter.component';
import { GamesTabComponent } from './components/games-tab.component';
import { StandingsTabComponent } from './components/standings-tab.component';
import { BracketsTabComponent } from './components/brackets-tab.component';
import { ContactsTabComponent } from './components/contacts-tab.component';
import { TeamResultsModalComponent } from './components/team-results-modal.component';
import { EditGameModalComponent } from './components/edit-game-modal.component';
import { TsicDialogComponent } from '../../../shared-ui/components/tsic-dialog/tsic-dialog.component';

type TabId = 'games' | 'standings' | 'brackets' | 'contacts';
type PanelId = 'cadt' | 'ladt';

interface FilterChip {
    category: string;
    label: string;
    type: 'cadt' | 'ladt' | 'gameDay' | 'time' | 'field' | 'unscored';
    nodeId?: string;
}

@Component({
    selector: 'app-view-schedule',
    standalone: true,
    imports: [
        FormsModule,
        CadtTreeFilterComponent,
        GamesTabComponent,
        StandingsTabComponent,
        BracketsTabComponent,
        ContactsTabComponent,
        TeamResultsModalComponent,
        EditGameModalComponent,
        TsicDialogComponent
    ],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <div class="view-schedule-page">
            <!-- Header -->
            <div class="page-header">
                <h1 class="page-title">
                    Schedule
                    @if (activeTab() === 'games' && games().length > 0) {
                        <span class="title-badge">{{ games().length }}</span>
                    }
                </h1>
                @if (eventName()) {
                    <p class="page-subtitle">{{ eventName() }}</p>
                }
            </div>

            <!-- ═══ Desktop Filter Bar (≥992px) ═══ -->
            <div class="desktop-filter-bar">
                <!-- CADT dropdown -->
                @if (hasCadtData()) {
                    <div class="filter-dropdown">
                        <button class="filter-dd-btn"
                                [class.active]="openPanel() === 'cadt' || cadtFilterCount() > 0"
                                (click)="togglePanel('cadt')">
                            <i class="bi bi-building"></i> By Club
                            @if (cadtFilterCount() > 0) {
                                <span class="dd-badge">{{ cadtFilterCount() }}</span>
                            }
                        </button>
                        @if (openPanel() === 'cadt') {
                            <div class="filter-panel" (click)="$event.stopPropagation()">
                                <app-cadt-tree-filter
                                    [treeData]="filterOptions()?.clubs ?? []"
                                    [checkedIds]="checkedIds"
                                    searchPlaceholder="Filter clubs..."
                                    (checkedIdsChange)="onCadtSelectionChange($event)" />
                            </div>
                        }
                    </div>
                }

                <!-- LADT dropdown -->
                @if (hasLadtData()) {
                    <div class="filter-dropdown">
                        <button class="filter-dd-btn"
                                [class.active]="openPanel() === 'ladt' || ladtFilterCount() > 0"
                                (click)="togglePanel('ladt')">
                            <i class="bi bi-people"></i> By Age
                            @if (ladtFilterCount() > 0) {
                                <span class="dd-badge">{{ ladtFilterCount() }}</span>
                            }
                        </button>
                        @if (openPanel() === 'ladt') {
                            <div class="filter-panel" (click)="$event.stopPropagation()">
                                <app-cadt-tree-filter
                                    [treeData]="ladtAsCadtNodes()"
                                    [checkedIds]="ladtCheckedIds"
                                    [hideRootLevel]="true"
                                    searchPlaceholder="Filter age groups..."
                                    (checkedIdsChange)="onLadtSelectionChange($event)" />
                            </div>
                        }
                    </div>
                }

                <!-- Date select -->
                @if (filterOptions()?.gameDays?.length) {
                    <select class="filter-select"
                            [ngModel]="selectedGameDay()"
                            (ngModelChange)="selectedGameDay.set($event); refreshTab()">
                        <option value="">Date</option>
                        @for (day of filterOptions()!.gameDays; track day) {
                            <option [value]="day">{{ formatGameDay(day) }}</option>
                        }
                    </select>
                }

                <!-- Time select -->
                @if (filterOptions()?.times?.length) {
                    <select class="filter-select"
                            [ngModel]="selectedTime()"
                            (ngModelChange)="selectedTime.set($event); refreshTab()">
                        <option value="">Time</option>
                        @for (time of filterOptions()!.times; track time) {
                            <option [value]="time">{{ formatTime(time) }}</option>
                        }
                    </select>
                }

                <!-- Location select -->
                @if (filterOptions()?.fields?.length) {
                    <select class="filter-select"
                            [ngModel]="selectedFieldId()"
                            (ngModelChange)="selectedFieldId.set($event); refreshTab()">
                        <option value="">Location</option>
                        @for (field of filterOptions()!.fields; track field.fieldId) {
                            <option [value]="field.fieldId">{{ field.fName }}</option>
                        }
                    </select>
                }

                <!-- Unscored checkbox -->
                <label class="filter-check-inline">
                    <input type="checkbox"
                           [ngModel]="unscoredOnly()"
                           (ngModelChange)="unscoredOnly.set($event); refreshTab()" />
                    Unscored
                </label>

                <!-- Reset -->
                @if (hasActiveFilters()) {
                    <button class="filter-reset-btn" (click)="clearFilters()">
                        <i class="bi bi-x-circle"></i> Reset
                    </button>
                }
            </div>

            <!-- Backdrop for closing desktop dropdown panels -->
            @if (openPanel()) {
                <div class="panel-backdrop" (click)="openPanel.set(null)"></div>
            }

            <!-- ═══ Toolbar: mobile funnel + segment tabs ═══ -->
            <div class="toolbar">
                <button class="filter-trigger mobile-only"
                        (click)="filterModalVisible.set(true)"
                        aria-label="Open filters">
                    <i class="bi bi-funnel"></i>
                    @if (activeFilterCount() > 0) {
                        <span class="filter-badge">{{ activeFilterCount() }}</span>
                    }
                </button>

                <div class="segment-tabs" role="tablist">
                    <button class="segment-btn"
                            [class.active]="activeTab() === 'games'"
                            (click)="switchTab('games')" role="tab">Games</button>
                    <button class="segment-btn"
                            [class.active]="activeTab() === 'standings'"
                            (click)="switchTab('standings')" role="tab">Standings</button>
                    <button class="segment-btn"
                            [class.active]="activeTab() === 'brackets'"
                            (click)="switchTab('brackets')" role="tab">Brackets</button>
                    @if (!capabilities()?.hideContacts) {
                        <button class="segment-btn"
                                [class.active]="activeTab() === 'contacts'"
                                (click)="switchTab('contacts')" role="tab">Contacts</button>
                    }
                </div>
            </div>

            <!-- Filter chips -->
            @if (activeFilterChips().length > 0) {
                <div class="filter-chips-strip">
                    @for (chip of activeFilterChips(); track chip.nodeId ?? chip.type + chip.label) {
                        <span class="filter-chip">
                            <span class="chip-category">{{ chip.category }}:</span>
                            <span class="chip-label">{{ chip.label }}</span>
                            <button type="button" class="chip-remove"
                                    (click)="removeChip(chip)"
                                    aria-label="Remove filter">&times;</button>
                        </span>
                    }
                    <button type="button" class="chip-clear-all" (click)="clearFilters()">Clear All</button>
                </div>
            }

            <!-- Tab content -->
            <div class="tab-content">
                @switch (activeTab()) {
                    @case ('games') {
                        <app-games-tab
                            [games]="games()"
                            [canScore]="auth.isAdmin()"
                            [isLoading]="tabLoading()"
                            (quickScore)="onQuickScore($event)"
                            (editGame)="onEditGameOpen($event)"
                            (viewTeamResults)="onViewTeamResults($event)" />
                    }
                    @case ('standings') {
                        <app-standings-tab
                            [standings]="standings()"
                            [records]="records()"
                            [isLoading]="tabLoading()"
                            (viewTeamResults)="onViewTeamResults($event)" />
                    }
                    @case ('brackets') {
                        <app-brackets-tab
                            [brackets]="brackets()"
                            [canScore]="auth.isAdmin()"
                            [isLoading]="tabLoading()"
                            (editBracketScore)="onBracketScoreEdit($event)"
                            (viewTeamResults)="onViewTeamResults($event)"
                            (viewFieldInfo)="onViewFieldInfo($event)" />
                    }
                    @case ('contacts') {
                        <app-contacts-tab
                            [contacts]="contacts()"
                            [isLoading]="tabLoading()" />
                    }
                }
            </div>
        </div>

        <!-- ═══ Mobile Filter Modal (<992px) ═══ -->
        @if (filterModalVisible()) {
            <tsic-dialog size="sm" (requestClose)="closeFilterModal()">
                <div class="modal-content filter-modal-content">
                    <div class="modal-header">
                        <h5 class="modal-title">
                            <i class="bi bi-funnel me-2"></i>Filters
                        </h5>
                        <button class="btn-close" (click)="closeFilterModal()"></button>
                    </div>
                    <div class="modal-body filter-modal-body">
                        <!-- Game Days -->
                        @if (filterOptions()?.gameDays?.length) {
                            <div class="filter-group">
                                <label class="filter-group-label">Date</label>
                                <select class="form-select form-select-sm"
                                        [ngModel]="selectedGameDay()"
                                        (ngModelChange)="selectedGameDay.set($event); refreshTab()">
                                    <option value="">All Dates</option>
                                    @for (day of filterOptions()!.gameDays; track day) {
                                        <option [value]="day">{{ formatGameDay(day) }}</option>
                                    }
                                </select>
                            </div>
                        }

                        <!-- Time -->
                        @if (filterOptions()?.times?.length) {
                            <div class="filter-group">
                                <label class="filter-group-label">Time</label>
                                <select class="form-select form-select-sm"
                                        [ngModel]="selectedTime()"
                                        (ngModelChange)="selectedTime.set($event); refreshTab()">
                                    <option value="">All Times</option>
                                    @for (time of filterOptions()!.times; track time) {
                                        <option [value]="time">{{ formatTime(time) }}</option>
                                    }
                                </select>
                            </div>
                        }

                        <!-- Location -->
                        @if (filterOptions()?.fields?.length) {
                            <div class="filter-group">
                                <label class="filter-group-label">Location</label>
                                <select class="form-select form-select-sm"
                                        [ngModel]="selectedFieldId()"
                                        (ngModelChange)="selectedFieldId.set($event); refreshTab()">
                                    <option value="">All Locations</option>
                                    @for (field of filterOptions()!.fields; track field.fieldId) {
                                        <option [value]="field.fieldId">{{ field.fName }}</option>
                                    }
                                </select>
                            </div>
                        }

                        <!-- Unscored toggle -->
                        <div class="filter-group">
                            <label class="filter-check">
                                <input type="checkbox"
                                       [ngModel]="unscoredOnly()"
                                       (ngModelChange)="unscoredOnly.set($event); refreshTab()" />
                                Show unscored games only
                            </label>
                        </div>

                        <!-- CADT Tree -->
                        @if (hasCadtData()) {
                            <div class="filter-group filter-group-tree">
                                <label class="filter-group-label">By Club</label>
                                <app-cadt-tree-filter
                                    [treeData]="filterOptions()?.clubs ?? []"
                                    [checkedIds]="checkedIds"
                                    searchPlaceholder="Filter clubs..."
                                    (checkedIdsChange)="onCadtSelectionChange($event)" />
                            </div>
                        }

                        <!-- LADT Tree -->
                        @if (hasLadtData()) {
                            <div class="filter-group filter-group-tree">
                                <label class="filter-group-label">By Age Group</label>
                                <app-cadt-tree-filter
                                    [treeData]="ladtAsCadtNodes()"
                                    [checkedIds]="ladtCheckedIds"
                                    [hideRootLevel]="true"
                                    searchPlaceholder="Filter age groups..."
                                    (checkedIdsChange)="onLadtSelectionChange($event)" />
                            </div>
                        }
                    </div>
                    <div class="modal-footer">
                        <button class="btn btn-sm btn-outline-danger me-auto"
                                [disabled]="!hasActiveFilters()"
                                (click)="clearFilters()">
                            <i class="bi bi-x-circle me-1"></i>Reset
                        </button>
                        <button class="btn btn-sm btn-primary"
                                (click)="closeFilterModal()">Done</button>
                    </div>
                </div>
            </tsic-dialog>
        }

        <!-- Team Results Modal -->
        <app-team-results-modal
            [results]="teamResults()"
            [teamName]="teamResultsName()"
            [visible]="teamResultsVisible()"
            (close)="teamResultsVisible.set(false)"
            (viewOpponent)="onViewTeamResults($event)" />

        <!-- Edit Game Modal -->
        <app-edit-game-modal
            [game]="editingGame()"
            [visible]="editGameVisible()"
            [teams]="flatTeamList()"
            (close)="editGameVisible.set(false)"
            (save)="onEditGameSave($event)" />

        <!-- Field Info Modal (used by brackets tab) -->
        @if (fieldInfoVisible()) {
            <tsic-dialog size="sm" (requestClose)="fieldInfoVisible.set(false)">
                <div class="modal-content">
                    <div class="modal-header">
                        <h5 class="modal-title">{{ fieldInfo()?.fName }}</h5>
                        <button class="btn-close" (click)="fieldInfoVisible.set(false)"></button>
                    </div>
                    <div class="modal-body">
                        @if (fieldInfo()?.address) {
                            <p class="mb-1">{{ fieldInfo()!.address }}</p>
                        }
                        @if (fieldInfo()?.city) {
                            <p class="mb-1">{{ fieldInfo()!.city }}</p>
                        }
                        @if (fieldInfo()?.state || fieldInfo()?.zip) {
                            <p class="mb-1">{{ fieldInfo()!.state ?? '' }} {{ fieldInfo()!.zip ?? '' }}</p>
                        }
                        @if (fieldInfo()?.directions) {
                            <p class="mb-0 text-muted" style="white-space:pre-wrap;">{{ fieldInfo()!.directions }}</p>
                        }
                        @if (fieldInfo()?.latitude && fieldInfo()?.longitude) {
                            <a href="https://www.google.com/maps?q={{ fieldInfo()!.latitude }},{{ fieldInfo()!.longitude }}"
                               target="_blank" rel="noopener"
                               class="btn btn-sm btn-outline-primary mt-2">
                                <i class="bi bi-geo-alt"></i> View Map
                            </a>
                        }
                    </div>
                </div>
            </tsic-dialog>
        }
    `,
    styles: [`
        .view-schedule-page {
            display: flex;
            flex-direction: column;
            gap: var(--space-3);
            padding: var(--space-3);
            max-width: 1400px;
            margin: 0 auto;
        }

        /* ── Header ── */
        .page-header {
            text-align: center;
            padding: var(--space-2) 0;
        }

        .page-title {
            margin: 0;
            font-size: var(--font-size-3xl);
            font-weight: 700;
            color: var(--bs-body-color);
        }

        .title-badge {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            vertical-align: middle;
            min-width: 28px;
            padding: 2px 8px;
            margin-left: var(--space-2);
            background: var(--bs-primary);
            color: white;
            border-radius: var(--radius-full);
            font-size: var(--font-size-xs);
            font-weight: 600;
            line-height: 1.4;
        }

        .page-subtitle {
            margin: var(--space-1) 0 0;
            font-size: var(--font-size-sm);
            color: var(--bs-secondary-color);
            font-weight: 400;
        }

        /* ═══ Desktop Filter Bar ═══ */
        .desktop-filter-bar {
            display: none; /* hidden by default (mobile) */
            align-items: center;
            gap: var(--space-2);
            padding: var(--space-2) var(--space-3);
            background: var(--bs-card-bg);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-md);
            flex-wrap: wrap;
        }

        @media (min-width: 992px) {
            .desktop-filter-bar { display: flex; }
        }

        /* Dropdown trigger button */
        .filter-dd-btn {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
            padding: var(--space-1) var(--space-3);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-sm);
            background: var(--bs-body-bg);
            color: var(--bs-body-color);
            font-size: var(--font-size-sm);
            font-weight: 500;
            cursor: pointer;
            white-space: nowrap;
            transition: all 0.15s;
        }

        .filter-dd-btn:hover {
            border-color: var(--bs-primary);
            color: var(--bs-primary);
        }

        .filter-dd-btn.active {
            border-color: var(--bs-primary);
            background: rgba(var(--bs-primary-rgb), 0.08);
            color: var(--bs-primary);
        }

        .dd-badge {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            min-width: 18px;
            height: 18px;
            padding: 0 4px;
            background: var(--bs-primary);
            color: white;
            border-radius: var(--radius-full);
            font-size: 10px;
            font-weight: 700;
            line-height: 1;
        }

        /* Dropdown container (relative anchor for panel) */
        .filter-dropdown {
            position: relative;
        }

        /* Dropdown panel (tree inside) */
        .filter-panel {
            position: absolute;
            top: calc(100% + 4px);
            left: 0;
            z-index: 1010;
            width: 320px;
            max-height: 420px;
            overflow-y: auto;
            background: var(--bs-card-bg);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-md);
            box-shadow: var(--shadow-lg);
            padding: var(--space-2);
        }

        /* Invisible backdrop to close panels on outside click */
        .panel-backdrop {
            position: fixed;
            inset: 0;
            z-index: 1005;
        }

        /* Filter select (desktop bar) */
        .filter-select {
            padding: var(--space-1) var(--space-3);
            padding-right: var(--space-6);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-sm);
            background: var(--bs-body-bg);
            color: var(--bs-body-color);
            font-size: var(--font-size-sm);
            cursor: pointer;
            appearance: auto;
        }

        .filter-select:focus {
            border-color: var(--bs-primary);
            outline: none;
            box-shadow: var(--shadow-focus);
        }

        /* Inline checkbox (desktop bar) */
        .filter-check-inline {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
            font-size: var(--font-size-sm);
            color: var(--bs-body-color);
            cursor: pointer;
            white-space: nowrap;
        }

        .filter-check-inline input { accent-color: var(--bs-primary); }

        /* Reset button */
        .filter-reset-btn {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
            padding: var(--space-1) var(--space-3);
            border: 1px solid var(--bs-danger);
            border-radius: var(--radius-sm);
            background: transparent;
            color: var(--bs-danger);
            font-size: var(--font-size-sm);
            font-weight: 500;
            cursor: pointer;
            transition: all 0.15s;
        }

        .filter-reset-btn:hover {
            background: var(--bs-danger);
            color: white;
        }

        /* ═══ Toolbar ═══ */
        .toolbar {
            display: flex;
            align-items: center;
            gap: var(--space-3);
        }

        /* Mobile-only filter trigger */
        .mobile-only { display: inline-flex; }

        @media (min-width: 992px) {
            .mobile-only { display: none !important; }
        }

        .filter-trigger {
            align-items: center;
            justify-content: center;
            position: relative;
            width: 40px;
            height: 40px;
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-md);
            background: var(--bs-card-bg);
            color: var(--bs-secondary-color);
            font-size: var(--font-size-lg);
            cursor: pointer;
            flex-shrink: 0;
            transition: background 0.15s, border-color 0.15s, color 0.15s;
        }

        .filter-trigger:hover {
            background: var(--bs-secondary-bg);
            color: var(--bs-body-color);
        }

        .filter-badge {
            position: absolute;
            top: -4px;
            right: -4px;
            display: inline-flex;
            align-items: center;
            justify-content: center;
            min-width: 18px;
            height: 18px;
            padding: 0 4px;
            background: var(--bs-primary);
            color: white;
            border-radius: var(--radius-full);
            font-size: 10px;
            font-weight: 700;
            line-height: 1;
        }

        /* ── Segment Tabs ── */
        .segment-tabs {
            display: inline-flex;
            background: var(--bs-tertiary-bg);
            border-radius: var(--radius-full);
            padding: 3px;
            gap: 2px;
        }

        .segment-btn {
            padding: var(--space-2) var(--space-4);
            border: none;
            border-radius: var(--radius-full);
            background: transparent;
            color: var(--bs-secondary-color);
            font-weight: 600;
            font-size: var(--font-size-sm);
            cursor: pointer;
            white-space: nowrap;
            transition: background 0.15s, color 0.15s, box-shadow 0.15s;
        }

        .segment-btn:hover:not(.active) {
            color: var(--bs-body-color);
            background: rgba(0, 0, 0, 0.04);
        }

        .segment-btn.active {
            background: var(--bs-primary);
            color: white;
            box-shadow: var(--shadow-sm);
        }

        /* ── Filter Chips ── */
        .filter-chips-strip {
            display: flex;
            flex-wrap: wrap;
            gap: var(--space-2);
            align-items: center;
        }

        .filter-chip {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
            padding: var(--space-1) var(--space-2);
            background: var(--bs-primary);
            color: var(--bs-white, #fff);
            border-radius: var(--radius-full);
            font-size: var(--font-size-xs);
            font-weight: 500;
            line-height: 1.2;
        }

        .chip-category {
            opacity: 0.8;
            font-weight: 600;
        }

        .chip-remove {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 16px;
            height: 16px;
            padding: 0;
            margin-left: 2px;
            background: transparent;
            border: none;
            border-radius: 50%;
            color: var(--bs-white, #fff);
            cursor: pointer;
            opacity: 0.7;
            font-size: var(--font-size-sm);
            line-height: 1;
            transition: opacity 0.15s, background 0.15s;
        }

        .chip-remove:hover {
            opacity: 1;
            background: rgba(255, 255, 255, 0.2);
        }

        .chip-clear-all {
            padding: var(--space-1) var(--space-2);
            background: transparent;
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-full);
            font-size: var(--font-size-xs);
            font-weight: 500;
            color: var(--bs-secondary-color);
            cursor: pointer;
            transition: all 0.15s;
        }

        .chip-clear-all:hover {
            background: var(--bs-secondary-bg);
            color: var(--bs-body-color);
        }

        /* ── Filter Modal (mobile) ── */
        .filter-modal-content {
            display: flex;
            flex-direction: column;
            /* Override TsicDialog's overflow:auto on .modal-content —
               without this the header/footer scroll away with the body */
            overflow: hidden;
        }

        .filter-modal-body {
            display: flex;
            flex-direction: column;
            gap: var(--space-4);
            overflow-y: auto;
            flex: 1;
            min-height: 0;
            max-height: calc(90vh - 120px); /* room for header + footer */
        }

        .filter-group-tree {
            flex-shrink: 0;
        }

        .filter-group {
            display: flex;
            flex-direction: column;
            gap: var(--space-2);
            flex-shrink: 0;
        }

        .filter-group-label {
            font-weight: 600;
            font-size: var(--font-size-sm);
            color: var(--bs-body-color);
        }

        .filter-check {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            font-size: var(--font-size-sm);
            color: var(--bs-body-color);
            cursor: pointer;
        }

        .filter-check input { accent-color: var(--bs-primary); }

        /* ── Responsive ── */
        @media (max-width: 767px) {
            .view-schedule-page {
                padding: var(--space-1) var(--space-2);
                gap: var(--space-1);
            }

            .page-header {
                padding: 0;
            }

            .page-title {
                font-size: var(--font-size-sm);
                font-weight: 600;
            }

            .title-badge {
                padding: 1px 6px;
                font-size: 10px;
                min-width: 22px;
                margin-left: var(--space-1);
            }

            .page-subtitle {
                display: none;
            }

            .toolbar {
                flex-wrap: wrap;
            }

            .segment-tabs {
                flex: 1;
                min-width: 0;
                overflow-x: auto;
                scrollbar-width: none;
                padding: 2px;
            }

            .segment-tabs::-webkit-scrollbar {
                display: none;
            }

            .segment-btn {
                padding: var(--space-1) var(--space-2);
                font-size: var(--font-size-xs);
            }
        }
    `]
})
export class ViewScheduleComponent implements OnInit {
    private readonly svc = inject(ViewScheduleService);
    private readonly route = inject(ActivatedRoute);
    protected readonly auth = inject(AuthService);
    private readonly jobService = inject(JobService);

    // ── Route state ──
    private jobPath: string | undefined;

    // ── Filter options + capabilities ──
    readonly filterOptions = signal<ScheduleFilterOptionsDto | null>(null);
    readonly capabilities = signal<ScheduleCapabilitiesDto | null>(null);
    readonly filterModalVisible = signal(false);

    // ── Desktop dropdown panel ──
    readonly openPanel = signal<PanelId | null>(null);

    // ── CADT selection ──
    checkedIds = new Set<string>();
    readonly cadtTeamIds = signal<string[]>([]);

    // ── LADT selection ──
    ladtCheckedIds = new Set<string>();
    readonly ladtTeamIds = signal<string[]>([]);

    // ── Simple filters ──
    readonly selectedGameDay = signal('');
    readonly selectedTime = signal('');
    readonly selectedFieldId = signal('');
    readonly unscoredOnly = signal(false);

    // ── Tab state ──
    readonly activeTab = signal<TabId>('games');
    readonly tabLoading = signal(false);

    // Per-tab data
    readonly games = signal<ViewGameDto[]>([]);
    readonly standings = signal<StandingsByDivisionResponse | null>(null);
    readonly records = signal<StandingsByDivisionResponse | null>(null);
    readonly brackets = signal<DivisionBracketResponse[]>([]);
    readonly contacts = signal<ContactDto[]>([]);

    // Track which tabs have been loaded with current filters
    private loadedTabs = new Set<TabId>();
    private requestId = 0; // race-condition guard for tab loading

    // ── Modal state ──
    readonly teamResults = signal<TeamResultDto[]>([]);
    readonly teamResultsName = signal('');
    readonly teamResultsVisible = signal(false);

    readonly editingGame = signal<ViewGameDto | null>(null);
    readonly editGameVisible = signal(false);

    readonly fieldInfo = signal<FieldDisplayDto | null>(null);
    readonly fieldInfoVisible = signal(false);

    // ══════════════════════════════════════════════════════════════════
    // Computed helpers
    // ══════════════════════════════════════════════════════════════════

    readonly eventName = computed(() => this.jobService.currentJob()?.jobName ?? '');

    readonly hasCadtData = computed(() => (this.filterOptions()?.clubs?.length ?? 0) > 0);
    readonly hasLadtData = computed(() => (this.filterOptions()?.agegroups?.length ?? 0) > 0);

    /** Flat team list for edit-game modal team picker (grouped by division) */
    readonly flatTeamList = computed(() => {
        const clubs = this.filterOptions()?.clubs ?? [];
        const teams: { teamId: string; teamName: string; divName: string }[] = [];
        for (const club of clubs) {
            for (const ag of club.agegroups ?? []) {
                for (const div of ag.divisions ?? []) {
                    for (const t of div.teams ?? []) {
                        teams.push({ teamId: t.teamId, teamName: t.teamName, divName: `${ag.agegroupName} ${div.divName}` });
                    }
                }
            }
        }
        return teams;
    });

    /** LADT data wrapped as CadtClubNode[] for tree component reuse (single virtual club) */
    readonly ladtAsCadtNodes = computed<CadtClubNode[]>(() => {
        const agegroups = this.filterOptions()?.agegroups ?? [];
        if (agegroups.length === 0) return [];
        return [{
            clubName: '__ladt__',
            agegroups: agegroups.map(ag => ({
                agegroupId: ag.agegroupId,
                agegroupName: ag.agegroupName,
                color: ag.color,
                divisions: (ag.divisions ?? []).map(div => ({
                    divId: div.divId,
                    divName: div.divName,
                    teams: (div.teams ?? []).map(t => ({
                        teamId: t.teamId,
                        teamName: t.teamName
                    }))
                }))
            }))
        }];
    });

    readonly cadtFilterCount = computed(() => this.cadtTeamIds().length > 0 ? this.checkedIds.size : 0);
    readonly ladtFilterCount = computed(() => this.ladtTeamIds().length > 0 ? this.ladtCheckedIds.size : 0);

    readonly hasActiveFilters = computed(() => {
        return this.cadtTeamIds().length > 0
            || this.ladtTeamIds().length > 0
            || this.selectedGameDay() !== ''
            || this.selectedTime() !== ''
            || this.selectedFieldId() !== ''
            || this.unscoredOnly();
    });

    readonly activeFilterCount = computed(() => {
        let count = 0;
        if (this.cadtTeamIds().length > 0) count++;
        if (this.ladtTeamIds().length > 0) count++;
        if (this.selectedGameDay()) count++;
        if (this.selectedTime()) count++;
        if (this.selectedFieldId()) count++;
        if (this.unscoredOnly()) count++;
        return count;
    });

    readonly activeFilterChips = computed<FilterChip[]>(() => {
        const chips: FilterChip[] = [];
        const opts = this.filterOptions();

        // CADT chips
        if (opts?.clubs && this.cadtTeamIds().length > 0) {
            this.buildTreeChips(opts.clubs, this.checkedIds, 'cadt', chips);
        }

        // LADT chips
        if (opts?.agegroups && this.ladtTeamIds().length > 0) {
            this.buildLadtChips(opts.agegroups, chips);
        }

        // Game Day chip
        if (this.selectedGameDay()) {
            chips.push({ category: 'Day', label: this.formatGameDay(this.selectedGameDay()), type: 'gameDay' });
        }

        // Time chip
        if (this.selectedTime()) {
            chips.push({ category: 'Time', label: this.formatTime(this.selectedTime()), type: 'time' });
        }

        // Field chip
        if (this.selectedFieldId()) {
            const field = opts?.fields?.find(f => f.fieldId === this.selectedFieldId());
            chips.push({ category: 'Location', label: field?.fName ?? this.selectedFieldId(), type: 'field' });
        }

        // Unscored chip
        if (this.unscoredOnly()) {
            chips.push({ category: 'Filter', label: 'Unscored only', type: 'unscored' });
        }

        return chips;
    });

    // ══════════════════════════════════════════════════════════════════
    // Lifecycle
    // ══════════════════════════════════════════════════════════════════

    ngOnInit(): void {
        const data = this.route.snapshot.data;
        if (data['publicMode']) {
            this.jobPath = this.route.snapshot.params['jobPath'];
        }

        this.svc.getFilterOptions(this.jobPath).subscribe(opts => {
            this.filterOptions.set(opts);
        });

        this.svc.getCapabilities(this.jobPath).subscribe(caps => {
            this.capabilities.set(caps);
        });

        this.loadTabData('games');
    }

    // ══════════════════════════════════════════════════════════════════
    // Desktop panel toggle
    // ══════════════════════════════════════════════════════════════════

    togglePanel(panel: PanelId): void {
        this.openPanel.set(this.openPanel() === panel ? null : panel);
    }

    // ══════════════════════════════════════════════════════════════════
    // CADT / LADT selection handlers
    // ══════════════════════════════════════════════════════════════════

    onCadtSelectionChange(checked: Set<string>): void {
        this.checkedIds = checked;
        this.deriveCadtTeamIds();
        this.refreshTab();
    }

    onLadtSelectionChange(checked: Set<string>): void {
        this.ladtCheckedIds = checked;
        this.deriveLadtTeamIds();
        this.refreshTab();
    }

    // ══════════════════════════════════════════════════════════════════
    // Filter actions
    // ══════════════════════════════════════════════════════════════════

    closeFilterModal(): void {
        this.filterModalVisible.set(false);
    }

    clearFilters(): void {
        this.checkedIds = new Set<string>();
        this.cadtTeamIds.set([]);
        this.ladtCheckedIds = new Set<string>();
        this.ladtTeamIds.set([]);
        this.selectedGameDay.set('');
        this.selectedTime.set('');
        this.selectedFieldId.set('');
        this.unscoredOnly.set(false);
        this.openPanel.set(null);
        this.refreshTab();
    }

    removeChip(chip: FilterChip): void {
        switch (chip.type) {
            case 'cadt':
                if (chip.nodeId) {
                    const next = new Set(this.checkedIds);
                    next.delete(chip.nodeId);
                    this.removeTreeDescendants(chip.nodeId, next, this.filterOptions()?.clubs ?? []);
                    this.removeTreeAncestors(chip.nodeId, next, this.filterOptions()?.clubs ?? []);
                    this.checkedIds = next;
                    this.deriveCadtTeamIds();
                    this.refreshTab();
                }
                break;
            case 'ladt':
                if (chip.nodeId) {
                    const next = new Set(this.ladtCheckedIds);
                    next.delete(chip.nodeId);
                    this.removeTreeDescendants(chip.nodeId, next, this.ladtAsCadtNodes());
                    this.removeTreeAncestors(chip.nodeId, next, this.ladtAsCadtNodes());
                    this.ladtCheckedIds = next;
                    this.deriveLadtTeamIds();
                    this.refreshTab();
                }
                break;
            case 'gameDay':
                this.selectedGameDay.set('');
                this.refreshTab();
                break;
            case 'time':
                this.selectedTime.set('');
                this.refreshTab();
                break;
            case 'field':
                this.selectedFieldId.set('');
                this.refreshTab();
                break;
            case 'unscored':
                this.unscoredOnly.set(false);
                this.refreshTab();
                break;
        }
    }

    refreshTab(): void {
        this.loadedTabs.clear();
        this.loadTabData(this.activeTab());
    }

    // ══════════════════════════════════════════════════════════════════
    // Tab switching & data loading
    // ══════════════════════════════════════════════════════════════════

    switchTab(tab: TabId): void {
        this.activeTab.set(tab);
        if (!this.loadedTabs.has(tab)) {
            this.loadTabData(tab);
        }
    }

    private buildFilterRequest(): ScheduleFilterRequest {
        const req: ScheduleFilterRequest = {};

        // CADT + LADT → merged teamIds
        const cadtTeams = this.cadtTeamIds();
        const ladtTeams = this.ladtTeamIds();

        if (cadtTeams.length > 0 && ladtTeams.length > 0) {
            // Both active → intersection
            const ladtSet = new Set(ladtTeams);
            const intersection = cadtTeams.filter(id => ladtSet.has(id));
            req.teamIds = intersection.length > 0
                ? intersection
                : ['00000000-0000-0000-0000-000000000000']; // sentinel — no real team matches
        } else if (cadtTeams.length > 0) {
            req.teamIds = cadtTeams;
        } else if (ladtTeams.length > 0) {
            req.teamIds = ladtTeams;
        }

        if (this.selectedGameDay()) req.gameDays = [this.selectedGameDay()];
        if (this.selectedTime()) req.times = [this.selectedTime()];
        if (this.selectedFieldId()) req.fieldIds = [this.selectedFieldId()];
        if (this.unscoredOnly()) req.unscoredOnly = true;
        return req;
    }

    private loadTabData(tab: TabId): void {
        this.tabLoading.set(true);
        const request = this.buildFilterRequest();
        const rid = ++this.requestId;

        switch (tab) {
            case 'games':
                this.svc.getGames(request, this.jobPath).subscribe({
                    next: data => {
                        if (rid !== this.requestId) return;
                        this.games.set(data);
                        this.loadedTabs.add('games');
                    },
                    error: err => console.error('[FILTER] games error:', err),
                    complete: () => { if (rid === this.requestId) this.tabLoading.set(false); }
                });
                break;
            case 'standings':
                forkJoin({
                    standings: this.svc.getStandings(request, this.jobPath),
                    records: this.svc.getTeamRecords(request, this.jobPath)
                }).subscribe({
                    next: ({ standings, records }) => {
                        if (rid !== this.requestId) return;
                        this.standings.set(standings);
                        this.records.set(records);
                        this.loadedTabs.add('standings');
                    },
                    complete: () => { if (rid === this.requestId) this.tabLoading.set(false); }
                });
                break;
            case 'brackets':
                this.svc.getBrackets(request, this.jobPath).subscribe({
                    next: data => {
                        if (rid !== this.requestId) return;
                        this.brackets.set(data);
                        this.loadedTabs.add('brackets');
                    },
                    complete: () => { if (rid === this.requestId) this.tabLoading.set(false); }
                });
                break;
            case 'contacts':
                this.svc.getContacts(request).subscribe({
                    next: data => {
                        if (rid !== this.requestId) return;
                        this.contacts.set(data);
                        this.loadedTabs.add('contacts');
                    },
                    complete: () => { if (rid === this.requestId) this.tabLoading.set(false); }
                });
                break;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // Team Results Modal
    // ══════════════════════════════════════════════════════════════════

    onViewTeamResults(teamId: string): void {
        this.teamResultsVisible.set(true);
        this.teamResultsName.set('');
        this.svc.getTeamResults(teamId, this.jobPath).subscribe(response => {
            this.teamResults.set(response.games);
            const parts = [response.agegroupName, response.clubName, response.teamName].filter(Boolean);
            this.teamResultsName.set(parts.join(' — '));
        });
    }

    // ══════════════════════════════════════════════════════════════════
    // Score Editing
    // ══════════════════════════════════════════════════════════════════

    onQuickScore(event: { gid: number; t1Score: number; t2Score: number }): void {
        const request: EditScoreRequest = {
            gid: event.gid,
            t1Score: event.t1Score,
            t2Score: event.t2Score
        };
        this.svc.quickEditScore(request).subscribe(() => {
            this.loadedTabs.delete('games');
            this.loadedTabs.delete('standings');
            this.loadedTabs.delete('brackets');
            this.loadTabData(this.activeTab());
        });
    }

    onBracketScoreEdit(event: { gid: number; t1Name: string; t2Name: string; t1Score: number | null; t2Score: number | null }): void {
        const mockGame: ViewGameDto = {
            gid: event.gid,
            gDate: '',
            fName: '',
            fieldId: '',
            agDiv: '',
            t1Name: event.t1Name,
            t2Name: event.t2Name,
            t1Score: event.t1Score ?? undefined,
            t2Score: event.t2Score ?? undefined,
            t1Type: 'F',
            t2Type: 'F',
            rnd: 0,
            gStatusCode: event.t1Score != null ? 2 : 1
        };
        this.editingGame.set(mockGame);
        this.editGameVisible.set(true);
    }

    onEditGameOpen(gid: number): void {
        const game = this.games().find(g => g.gid === gid);
        if (game) {
            this.editingGame.set(game);
            this.editGameVisible.set(true);
        }
    }

    onEditGameSave(request: EditGameRequest): void {
        this.svc.editGame(request).subscribe(() => {
            this.editGameVisible.set(false);
            this.loadedTabs.clear();
            this.loadTabData(this.activeTab());
        });
    }

    // ══════════════════════════════════════════════════════════════════
    // Field Info (used by brackets tab)
    // ══════════════════════════════════════════════════════════════════

    onViewFieldInfo(fieldId: string): void {
        this.svc.getFieldInfo(fieldId).subscribe(info => {
            if (info) {
                this.fieldInfo.set(info);
                this.fieldInfoVisible.set(true);
            }
        });
    }

    // ══════════════════════════════════════════════════════════════════
    // Formatting helpers
    // ══════════════════════════════════════════════════════════════════

    formatGameDay(isoDate: string): string {
        const d = new Date(isoDate);
        if (isNaN(d.getTime())) return isoDate;
        const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
        const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
        return `${days[d.getDay()]} ${months[d.getMonth()]} ${d.getDate()}`;
    }

    formatTime(time: string): string {
        const [h, m] = time.split(':').map(Number);
        const ampm = h >= 12 ? 'PM' : 'AM';
        const h12 = h % 12 || 12;
        return `${h12}:${m.toString().padStart(2, '0')} ${ampm}`;
    }

    // ══════════════════════════════════════════════════════════════════
    // CADT/LADT team-ID derivation
    // ══════════════════════════════════════════════════════════════════

    /**
     * Resolve all checked CADT nodes down to team IDs.
     * Always resolves to the leaf (team) level for precision.
     */
    private deriveCadtTeamIds(): void {
        const teamIdSet = new Set<string>();
        const clubs = this.filterOptions()?.clubs ?? [];

        for (const club of clubs) {
            if (this.checkedIds.has(`club:${club.clubName}`)) {
                for (const ag of club.agegroups ?? []) {
                    for (const div of ag.divisions ?? []) {
                        for (const team of div.teams ?? []) teamIdSet.add(team.teamId);
                    }
                }
                continue;
            }
            for (const ag of club.agegroups ?? []) {
                for (const div of ag.divisions ?? []) {
                    if (this.checkedIds.has(`div:${club.clubName}|${div.divId}`)) {
                        for (const team of div.teams ?? []) teamIdSet.add(team.teamId);
                        continue;
                    }
                    for (const team of div.teams ?? []) {
                        if (this.checkedIds.has(`team:${team.teamId}`)) teamIdSet.add(team.teamId);
                    }
                }
            }
        }

        this.cadtTeamIds.set([...teamIdSet]);
    }

    /**
     * Resolve all checked LADT nodes down to team IDs.
     * The LADT tree is Agegroup → Division → Team (no club level).
     * Node IDs use __ladt__ as the virtual club name.
     */
    private deriveLadtTeamIds(): void {
        const teamIdSet = new Set<string>();
        const agegroups = this.filterOptions()?.agegroups ?? [];
        const clubName = '__ladt__';

        for (const ag of agegroups) {
            if (this.ladtCheckedIds.has(`ag:${clubName}|${ag.agegroupId}`)) {
                for (const div of ag.divisions ?? []) {
                    for (const team of div.teams ?? []) teamIdSet.add(team.teamId);
                }
                continue;
            }
            for (const div of ag.divisions ?? []) {
                if (this.ladtCheckedIds.has(`div:${clubName}|${div.divId}`)) {
                    for (const team of div.teams ?? []) teamIdSet.add(team.teamId);
                    continue;
                }
                for (const team of div.teams ?? []) {
                    if (this.ladtCheckedIds.has(`team:${team.teamId}`)) teamIdSet.add(team.teamId);
                }
            }
        }

        this.ladtTeamIds.set([...teamIdSet]);
    }

    // ══════════════════════════════════════════════════════════════════
    // Filter chip builders
    // ══════════════════════════════════════════════════════════════════

    /** Build chips for highest-level checked nodes in a CADT tree */
    private buildTreeChips(clubs: CadtClubNode[], checked: Set<string>, chipType: 'cadt' | 'ladt', chips: FilterChip[]): void {
        for (const club of clubs) {
            if (checked.has(`club:${club.clubName}`)) {
                chips.push({ category: 'Club', label: club.clubName, type: chipType, nodeId: `club:${club.clubName}` });
                continue;
            }
            for (const ag of club.agegroups ?? []) {
                if (checked.has(`ag:${club.clubName}|${ag.agegroupId}`)) {
                    chips.push({ category: 'Agegroup', label: ag.agegroupName, type: chipType, nodeId: `ag:${club.clubName}|${ag.agegroupId}` });
                    continue;
                }
                for (const div of ag.divisions ?? []) {
                    if (checked.has(`div:${club.clubName}|${div.divId}`)) {
                        chips.push({ category: 'Division', label: div.divName, type: chipType, nodeId: `div:${club.clubName}|${div.divId}` });
                        continue;
                    }
                    for (const team of div.teams ?? []) {
                        if (checked.has(`team:${team.teamId}`)) {
                            chips.push({ category: 'Team', label: team.teamName, type: chipType, nodeId: `team:${team.teamId}` });
                        }
                    }
                }
            }
        }
    }

    /** Build chips for LADT — uses the LADT tree directly (not wrapped) */
    private buildLadtChips(agegroups: LadtAgegroupNode[], chips: FilterChip[]): void {
        const clubName = '__ladt__';
        for (const ag of agegroups) {
            if (this.ladtCheckedIds.has(`ag:${clubName}|${ag.agegroupId}`)) {
                chips.push({ category: 'Age', label: ag.agegroupName, type: 'ladt', nodeId: `ag:${clubName}|${ag.agegroupId}` });
                continue;
            }
            for (const div of ag.divisions ?? []) {
                if (this.ladtCheckedIds.has(`div:${clubName}|${div.divId}`)) {
                    chips.push({ category: 'Division', label: div.divName, type: 'ladt', nodeId: `div:${clubName}|${div.divId}` });
                    continue;
                }
                for (const team of div.teams ?? []) {
                    if (this.ladtCheckedIds.has(`team:${team.teamId}`)) {
                        chips.push({ category: 'Team', label: team.teamName, type: 'ladt', nodeId: `team:${team.teamId}` });
                    }
                }
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // Tree node removal helpers (shared for CADT and LADT)
    // ══════════════════════════════════════════════════════════════════

    /** Remove a node's descendants from the checked set */
    private removeTreeDescendants(nodeId: string, checked: Set<string>, clubs: CadtClubNode[]): void {
        for (const club of clubs) {
            const clubId = `club:${club.clubName}`;
            if (clubId === nodeId) {
                for (const ag of club.agegroups ?? []) {
                    checked.delete(`ag:${club.clubName}|${ag.agegroupId}`);
                    for (const div of ag.divisions ?? []) {
                        checked.delete(`div:${club.clubName}|${div.divId}`);
                        for (const team of div.teams ?? []) checked.delete(`team:${team.teamId}`);
                    }
                }
                return;
            }
            for (const ag of club.agegroups ?? []) {
                const agId = `ag:${club.clubName}|${ag.agegroupId}`;
                if (agId === nodeId) {
                    for (const div of ag.divisions ?? []) {
                        checked.delete(`div:${club.clubName}|${div.divId}`);
                        for (const team of div.teams ?? []) checked.delete(`team:${team.teamId}`);
                    }
                    return;
                }
                for (const div of ag.divisions ?? []) {
                    if (`div:${club.clubName}|${div.divId}` === nodeId) {
                        for (const team of div.teams ?? []) checked.delete(`team:${team.teamId}`);
                        return;
                    }
                }
            }
        }
    }

    /** Remove all ancestors of a node from the checked set */
    private removeTreeAncestors(nodeId: string, checked: Set<string>, clubs: CadtClubNode[]): void {
        for (const club of clubs) {
            const clubId = `club:${club.clubName}`;
            for (const ag of club.agegroups ?? []) {
                const agId = `ag:${club.clubName}|${ag.agegroupId}`;
                if (agId === nodeId) { checked.delete(clubId); return; }
                for (const div of ag.divisions ?? []) {
                    const divId = `div:${club.clubName}|${div.divId}`;
                    if (divId === nodeId) { checked.delete(agId); checked.delete(clubId); return; }
                    for (const team of div.teams ?? []) {
                        if (`team:${team.teamId}` === nodeId) {
                            checked.delete(divId); checked.delete(agId); checked.delete(clubId);
                            return;
                        }
                    }
                }
            }
        }
    }
}
