import {
    ChangeDetectionStrategy, Component, inject, OnInit, signal, computed, CUSTOM_ELEMENTS_SCHEMA
} from '@angular/core';
import { ActivatedRoute, ActivatedRouteSnapshot } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { AuthService } from '../../../infrastructure/services/auth.service';
import { JobService } from '../../../infrastructure/services/job.service';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { MultiSelectModule, CheckBoxSelectionService } from '@syncfusion/ej2-angular-dropdowns';
import { environment } from '@environments/environment';
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
    LadtAgegroupNode,
    FamilyPlayersResponseDto
} from '@core/api';
import { ViewScheduleService } from './services/view-schedule.service';
import { ScheduleFiltersStore } from './services/schedule-filters.store';
import { JobFilterTreeService } from '../../../core/services/job-filter-tree.service';
import { CadtTreeFilterComponent } from '../shared/components/cadt-tree-filter/cadt-tree-filter.component';
import { LadtTreeFilterComponent } from '../shared/components/ladt-tree-filter/ladt-tree-filter.component';
import { GamesTabComponent } from './components/games-tab.component';
import { StandingsTabComponent } from './components/standings-tab.component';
import { BracketsTabComponent } from './components/brackets-tab.component';
import { ContactsTabComponent } from './components/contacts-tab.component';
import { TeamResultsModalComponent } from './components/team-results-modal.component';
import { EditGameModalComponent } from './components/edit-game-modal.component';
import { GameClockModalComponent } from './components/game-clock-modal.component';
import { InlineGameClockComponent } from './components/inline-game-clock.component';
import { TsicDialogComponent } from '../../../shared-ui/components/tsic-dialog/tsic-dialog.component';

type TabId = 'games' | 'standings' | 'brackets' | 'contacts';
type PanelId = 'cadt' | 'ladt';

interface DirectTeamOption {
    teamId: string;
    displayText: string;
}

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
        MultiSelectModule,
        CadtTreeFilterComponent,
        LadtTreeFilterComponent,
        GamesTabComponent,
        StandingsTabComponent,
        BracketsTabComponent,
        ContactsTabComponent,
        TeamResultsModalComponent,
        EditGameModalComponent,
        GameClockModalComponent,
        InlineGameClockComponent,
        TsicDialogComponent
    ],
    schemas: [CUSTOM_ELEMENTS_SCHEMA],
    providers: [CheckBoxSelectionService],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <div class="view-schedule-page">
            <!-- Header -->
            <div class="page-header">
                <h1 class="page-title">
                    Schedule
                    @if (activeTab() === 'games') {
                        <span class="title-badge">{{ gameCountLabel() }}</span>
                    }
                </h1>
                @if (currentJobId() && hasGameClockGames()) {
                    <app-inline-game-clock
                        [jobId]="currentJobId()"
                        (expand)="gameClockVisible.set(true)" />
                }
            </div>

            <!-- ═══ Desktop Filter Bar (≥992px) ═══ -->
            <div class="desktop-filter-bar">
                <div class="filter-row">
                <!-- Direct team multiselect (primary parent affordance) -->
                @if (directTeamOptions().length > 0) {
                    <div class="team-typeahead-wrap">
                        <span class="team-typeahead-label">
                            <i class="bi bi-people" aria-hidden="true"></i>
                            <span>Find Your Teams</span>
                        </span>
                        <ejs-multiselect
                            [dataSource]="directTeamOptions()"
                            [fields]="directTeamFields"
                            [value]="directTeamIds()"
                            [mode]="'CheckBox'"
                            [allowFiltering]="true"
                            [showDropDownIcon]="true"
                            [closePopupOnSelect]="false"
                            [changeOnBlur]="false"
                            [popupHeight]="'360px'"
                            [filterBarPlaceholder]="'Type a team or club name…'"
                            placeholder="type to search teams"
                            cssClass="team-typeahead"
                            (change)="onDirectTeamChange($event)">
                        </ejs-multiselect>
                    </div>
                }

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
                                    [treeData]="cadtTree()"
                                    [checkedIds]="checkedIds"
                                    [requireScheduled]="true"
                                    [requireClubRep]="true"
                                    [excludeWaitlistDropped]="true"
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
                                <app-ladt-tree-filter
                                    [treeData]="ladtTree()"
                                    [checkedIds]="ladtCheckedIds"
                                    [requireScheduled]="true"
                                    [excludeWaitlistDropped]="true"
                                    searchPlaceholder="Filter age groups..."
                                    (checkedIdsChange)="onLadtSelectionChange($event)" />
                            </div>
                        }
                    </div>
                }

                <!-- Date select -->
                @if (filterOptions()?.gameDays?.length) {
                    <span class="filter-select-wrap">
                        <i class="bi bi-calendar3 filter-select-icon" aria-hidden="true"></i>
                        <select class="filter-select"
                                [ngModel]="selectedGameDay()"
                                (ngModelChange)="selectedGameDay.set($event); onSimpleFilterChange()">
                            <option value="">Date</option>
                            @for (day of filterOptions()!.gameDays; track day) {
                                <option [value]="day">{{ formatGameDay(day) }}</option>
                            }
                        </select>
                    </span>
                }

                <!-- Time select (admin only — used to bulk-record at the same time slot) -->
                @if (auth.isAdmin() && filterOptions()?.times?.length) {
                    <span class="filter-select-wrap">
                        <i class="bi bi-clock filter-select-icon" aria-hidden="true"></i>
                        <select class="filter-select"
                                [ngModel]="selectedTime()"
                                (ngModelChange)="selectedTime.set($event); onSimpleFilterChange()">
                            <option value="">Time</option>
                            @for (time of filterOptions()!.times; track time) {
                                <option [value]="time">{{ formatTime(time) }}</option>
                            }
                        </select>
                    </span>
                }

                <!-- Location select -->
                @if (filterOptions()?.fields?.length) {
                    <span class="filter-select-wrap">
                        <i class="bi bi-geo-alt filter-select-icon" aria-hidden="true"></i>
                        <select class="filter-select"
                                [ngModel]="selectedFieldId()"
                                (ngModelChange)="selectedFieldId.set($event); onSimpleFilterChange()">
                            <option value="">Location</option>
                            @for (field of filterOptions()!.fields; track field.fieldId) {
                                <option [value]="field.fieldId">{{ field.fName }}</option>
                            }
                        </select>
                    </span>
                }

                <!-- Unscored checkbox (admin only — domain term, not parent-facing) -->
                @if (auth.isAdmin()) {
                    <label class="filter-check-inline">
                        <input type="checkbox"
                               [ngModel]="unscoredOnly()"
                               (ngModelChange)="unscoredOnly.set($event); onSimpleFilterChange()" />
                        Unscored
                    </label>
                }

                </div>

                <!-- Active-filters echo zone: shows every applied filter as a removable chip.
                     Followed teams render first (star-tinted), then non-team filter chips
                     (CADT/LADT picks, Date, Time, Location, Unscored). Reset lives at the
                     right edge so it's adjacent to the chips it will clear. -->
                @if (hasActiveFilters()) {
                    <div class="active-chips-row">
                        <span class="active-chips-prefix">
                            <i class="bi bi-funnel-fill" aria-hidden="true"></i>
                            <span>Filters</span>
                            <span class="active-chips-count">{{ totalChipCount() }}</span>
                        </span>
                        <div class="active-chips-list">
                            @for (chip of directTeamChips(); track chip.teamId) {
                                <span class="team-chip team-chip--following">
                                    <i class="bi bi-bookmark-star-fill team-chip-icon" aria-hidden="true"></i>
                                    <span class="team-chip-label">{{ chip.displayText }}</span>
                                    <button type="button" class="team-chip-remove"
                                            (click)="toggleDirectTeam(chip.teamId)"
                                            [attr.aria-label]="'Remove ' + chip.displayText">&times;</button>
                                </span>
                            }
                            @for (chip of activeFilterChips(); track chip.nodeId ?? chip.type + chip.label) {
                                <span class="team-chip">
                                    <span class="team-chip-category">{{ chip.category }}:</span>
                                    <span class="team-chip-label">{{ chip.label }}</span>
                                    <button type="button" class="team-chip-remove"
                                            (click)="removeChip(chip)"
                                            [attr.aria-label]="'Remove ' + chip.category + ' ' + chip.label">&times;</button>
                                </span>
                            }
                        </div>
                        <button class="filter-reset-btn" (click)="clearFilters()">
                            <i class="bi bi-x-circle"></i> Reset
                        </button>
                    </div>
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
                    @if (filterOptions()?.jobHasBrackets) {
                        <button class="segment-btn"
                                [class.active]="activeTab() === 'brackets'"
                                (click)="switchTab('brackets')" role="tab">Brackets</button>
                    }
                    @if (!capabilities()?.hideContacts) {
                        <button class="segment-btn"
                                [class.active]="activeTab() === 'contacts'"
                                (click)="switchTab('contacts')" role="tab">Contacts</button>
                    }
                </div>
            </div>

            <!-- Tab content -->
            <div class="tab-content">
                @switch (activeTab()) {
                    @case ('games') {
                        <app-games-tab
                            [games]="games()"
                            [canScore]="auth.isAdmin()"
                            [isLoading]="tabLoading()"
                            [followedTeamIds]="directTeamIds()"
                            (quickScore)="onQuickScore($event)"
                            (editGame)="onEditGameOpen($event)"
                            (viewTeamResults)="onViewTeamResults($event)"
                            (toggleFollow)="toggleDirectTeam($event)" />
                    }
                    @case ('standings') {
                        <app-standings-tab
                            [standings]="standings()"
                            [records]="records()"
                            [isLoading]="tabLoading()"
                            [followedTeamIds]="directTeamIds()"
                            (viewTeamResults)="onViewTeamResults($event)" />
                    }
                    @case ('brackets') {
                        <app-brackets-tab
                            [brackets]="brackets()"
                            [canScore]="auth.isAdmin()"
                            [isLoading]="tabLoading()"
                            [agegroupColors]="agegroupColors()"
                            [followedTeamIds]="directTeamIds()"
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
                        <!-- Direct team multiselect (primary parent affordance) -->
                        @if (directTeamOptions().length > 0) {
                            <div class="filter-group">
                                <label class="filter-group-label">Find Your Teams</label>
                                <ejs-multiselect
                                    [dataSource]="directTeamOptions()"
                                    [fields]="directTeamFields"
                                    [value]="directTeamIds()"
                                    [mode]="'CheckBox'"
                                    [allowFiltering]="true"
                                    [showDropDownIcon]="true"
                                    [closePopupOnSelect]="false"
                                    [changeOnBlur]="false"
                                    [popupHeight]="'360px'"
                                    [filterBarPlaceholder]="'Type a team or club name…'"
                                    placeholder="Type to search teams"
                                    cssClass="team-typeahead"
                                    (change)="onDirectTeamChange($event)">
                                </ejs-multiselect>
                            </div>
                        }

                        <!-- Game Days -->
                        @if (filterOptions()?.gameDays?.length) {
                            <div class="filter-group">
                                <label class="filter-group-label">Date</label>
                                <select class="form-select form-select-sm"
                                        [ngModel]="selectedGameDay()"
                                        (ngModelChange)="selectedGameDay.set($event); onSimpleFilterChange()">
                                    <option value="">All Dates</option>
                                    @for (day of filterOptions()!.gameDays; track day) {
                                        <option [value]="day">{{ formatGameDay(day) }}</option>
                                    }
                                </select>
                            </div>
                        }

                        <!-- Time (admin only — used to bulk-record at the same time slot) -->
                        @if (auth.isAdmin() && filterOptions()?.times?.length) {
                            <div class="filter-group">
                                <label class="filter-group-label">Time</label>
                                <select class="form-select form-select-sm"
                                        [ngModel]="selectedTime()"
                                        (ngModelChange)="selectedTime.set($event); onSimpleFilterChange()">
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
                                        (ngModelChange)="selectedFieldId.set($event); onSimpleFilterChange()">
                                    <option value="">All Locations</option>
                                    @for (field of filterOptions()!.fields; track field.fieldId) {
                                        <option [value]="field.fieldId">{{ field.fName }}</option>
                                    }
                                </select>
                            </div>
                        }

                        <!-- Unscored toggle (admin only — domain term, not parent-facing) -->
                        @if (auth.isAdmin()) {
                            <div class="filter-group">
                                <label class="filter-check">
                                    <input type="checkbox"
                                           [ngModel]="unscoredOnly()"
                                           (ngModelChange)="unscoredOnly.set($event); onSimpleFilterChange()" />
                                    Show unscored games only
                                </label>
                            </div>
                        }

                        <!-- CADT Tree -->
                        @if (hasCadtData()) {
                            <div class="filter-group filter-group-tree">
                                <label class="filter-group-label">By Club</label>
                                <app-cadt-tree-filter
                                    [treeData]="cadtTree()"
                                    [checkedIds]="checkedIds"
                                    [requireScheduled]="true"
                                    [requireClubRep]="true"
                                    [excludeWaitlistDropped]="true"
                                    searchPlaceholder="Filter clubs..."
                                    (checkedIdsChange)="onCadtSelectionChange($event)" />
                            </div>
                        }

                        <!-- LADT Tree -->
                        @if (hasLadtData()) {
                            <div class="filter-group filter-group-tree">
                                <label class="filter-group-label">By Age Group</label>
                                <app-ladt-tree-filter
                                    [treeData]="ladtTree()"
                                    [checkedIds]="ladtCheckedIds"
                                    [requireScheduled]="true"
                                    [excludeWaitlistDropped]="true"
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
            [statusOptions]="capabilities()?.gameStatusOptions ?? []"
            (close)="editGameVisible.set(false)"
            (save)="onEditGameSave($event)" />

        <!-- Game Clock FAB + Modal — only shown when current/upcoming RR or PO games exist -->
        @if (currentJobId() && hasGameClockGames()) {
            <button type="button" class="game-clock-fab"
                    (click)="gameClockVisible.set(true)"
                    aria-label="Open game clock">
                <i class="bi bi-stopwatch"></i>
            </button>
        }
        @if (gameClockVisible() && currentJobId()) {
            <app-game-clock-modal
                [jobId]="currentJobId()"
                (close)="gameClockVisible.set(false)" />
        }

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
                        @if (fieldMapUrl(); as url) {
                            <a [href]="url"
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

        /* ── Game Clock FAB ── */
        .game-clock-fab {
            position: fixed;
            bottom: var(--space-6);
            right: var(--space-6);
            z-index: 1040;
            width: 56px;
            height: 56px;
            border-radius: var(--radius-full, 999px);
            background: var(--bs-primary);
            color: #fff;
            border: none;
            box-shadow: var(--shadow-lg);
            display: flex;
            align-items: center;
            justify-content: center;
            font-size: 1.5rem;
            cursor: pointer;
        }
        .game-clock-fab:hover {
            filter: brightness(1.05);
        }
        .game-clock-fab:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus, 0 0 0 0.25rem rgba(13,110,253,.25));
        }
        @media (prefers-reduced-motion: reduce) {
            .game-clock-fab { transition: none !important; }
        }

        /* ── Header ── */
        .page-header {
            position: sticky;
            top: 0;
            z-index: 10;
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: var(--space-4);
            padding: var(--space-2) var(--space-3);
            margin: 0 calc(var(--space-3) * -1);
            background: var(--bs-body-bg);
            border-bottom: 1px solid var(--bs-border-color);
        }

        .page-title {
            margin: 0;
            font-size: var(--font-size-3xl);
            font-weight: 700;
            color: var(--bs-body-color);
            flex-shrink: 0;
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
            margin: 0;
            font-size: var(--font-size-sm);
            color: var(--bs-secondary-color);
            font-weight: 400;
            text-align: right;
            flex-shrink: 0;
        }

        @media (max-width: 768px) {
            .page-header {
                flex-direction: column;
                align-items: center;
                gap: var(--space-1);
            }
            .page-subtitle { text-align: center; }
        }

        /* ═══ Desktop Filter Bar ═══
           Outer card stacks two zones: the filter-row (choose) on top,
           and an echo zone below (active-chips-row, chosen) when populated.
           NOTE: no overflow:hidden — the By Club / By Age dropdown panels
           are position:absolute and need to escape this container. The
           chip-row rounds its own bottom corners to keep the card look. */
        .desktop-filter-bar {
            display: none; /* hidden by default (mobile) */
            flex-direction: column;
            background: var(--bs-card-bg);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-md);
        }

        @media (min-width: 992px) {
            .desktop-filter-bar { display: flex; }
        }

        /* Top zone: the filters themselves, all on one line where possible. */
        .filter-row {
            display: flex;
            align-items: center;
            flex-wrap: wrap;
            gap: var(--space-2);
            padding: var(--space-2) var(--space-3);
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

        /* ── Team typeahead (Syncfusion ejs-multiselect, CheckBox mode) ──
           Single inline control: [label] + [multiselect input], all on the
           same line as the other filters. Chosen-teams chips are a separate
           echo zone below (.active-chips-row). */
        .team-typeahead-wrap {
            display: inline-flex;
            align-items: stretch;
            min-width: 280px;
            max-width: 460px;
            flex: 1 1 280px;
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-sm);
            background: var(--bs-body-bg);
            overflow: hidden;
            transition: border-color 0.15s, box-shadow 0.15s;
        }

        .team-typeahead-wrap:focus-within {
            border-color: var(--bs-primary);
            box-shadow: var(--shadow-focus);
        }

        /* Visible label on the left — "Find Your Teams" — gives context to "4 selected" */
        .team-typeahead-label {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
            padding: 0 var(--space-2);
            background: color-mix(in srgb, var(--bs-primary) 8%, var(--bs-tertiary-bg));
            border-right: 1px solid var(--bs-border-color);
            color: var(--bs-primary);
            font-size: var(--font-size-sm);
            font-weight: 600;
            white-space: nowrap;
        }

        /* Strip the multiselect's own border so the wrap looks like one cohesive control */
        .team-typeahead-wrap ::ng-deep .e-multiselect {
            flex: 1 1 auto;
            min-width: 0;
        }
        .team-typeahead-wrap ::ng-deep .e-multi-select-wrapper {
            border: none !important;
            min-height: 32px;
            background: transparent;
        }

        /* ── Bottom zone: chosen-teams echo strip ──
           Subtle primary tint so it reads as "owned by you" rather than as
           another filter. Top border separates it from the filter row above. */
        .active-chips-row {
            display: flex;
            align-items: center;
            flex-wrap: wrap;
            gap: var(--space-2);
            padding: var(--space-2) var(--space-3);
            background: color-mix(in srgb, var(--bs-primary) 5%, var(--bs-tertiary-bg));
            border-top: 1px solid var(--bs-border-color);
            border-radius: 0 0 calc(var(--radius-md) - 1px) calc(var(--radius-md) - 1px);
        }

        .active-chips-prefix {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
            color: var(--bs-primary);
            font-size: var(--font-size-sm);
            font-weight: 600;
            white-space: nowrap;
            padding-right: var(--space-2);
            border-right: 1px solid color-mix(in srgb, var(--bs-primary) 25%, transparent);
        }

        .active-chips-prefix > i { font-size: 0.95em; }

        .active-chips-count {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            min-width: 20px;
            height: 20px;
            padding: 0 6px;
            background: var(--bs-primary);
            color: white;
            border-radius: var(--radius-full);
            font-size: 11px;
            font-weight: 700;
            line-height: 1;
        }

        .active-chips-list {
            display: flex;
            flex-wrap: wrap;
            gap: var(--space-1);
            flex: 1 1 auto;
            min-width: 0;
        }

        .team-chip {
            display: inline-flex;
            align-items: center;
            gap: 6px;
            padding: 3px 4px 3px 10px;
            background: var(--bs-card-bg);
            border: 1px solid color-mix(in srgb, var(--bs-secondary-color) 30%, transparent);
            border-radius: var(--radius-full);
            font-size: var(--font-size-xs);
            color: var(--bs-body-color);
            line-height: 1.3;
            box-shadow: 0 1px 2px rgba(0, 0, 0, 0.04);
            transition: border-color 0.15s, box-shadow 0.15s, transform 0.15s;
        }

        .team-chip:hover {
            border-color: var(--bs-primary);
            box-shadow: 0 2px 4px rgba(0, 0, 0, 0.06);
        }

        /* Followed-team chip variant — star-tinted to read as "yours" */
        .team-chip--following {
            border-color: color-mix(in srgb, var(--bs-warning) 50%, transparent);
            background: color-mix(in srgb, var(--bs-warning) 8%, var(--bs-card-bg));
        }

        .team-chip--following:hover {
            border-color: var(--bs-warning);
        }

        .team-chip-icon {
            color: var(--bs-warning);
            font-size: 0.85em;
            flex-shrink: 0;
        }

        .team-chip-category {
            font-weight: 600;
            color: var(--bs-secondary-color);
            text-transform: uppercase;
            font-size: 10px;
            letter-spacing: 0.04em;
            white-space: nowrap;
        }

        .team-chip-label {
            font-weight: 500;
            white-space: nowrap;
            color: var(--bs-body-color);
        }

        .team-chip-remove {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 18px;
            height: 18px;
            padding: 0;
            border: none;
            border-radius: 50%;
            background: transparent;
            color: var(--bs-secondary-color);
            font-size: 16px;
            line-height: 1;
            cursor: pointer;
            transition: background 0.15s, color 0.15s, transform 0.15s;
        }

        .team-chip-remove:hover {
            background: var(--bs-danger);
            color: white;
            transform: scale(1.05);
        }

        .team-chip-remove:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
        }

        @media (prefers-reduced-motion: reduce) {
            .team-chip,
            .team-chip-remove,
            .team-typeahead-wrap { transition: none !important; }
            .team-chip-remove:hover { transform: none; }
        }

        /* Mobile modal: full-width without the left-label wrapper */
        .filter-modal-body ::ng-deep .e-multiselect.team-typeahead { width: 100%; }

        /* Filter select icon prefix wrappers (Date/Time/Location) */
        .filter-select-wrap {
            position: relative;
            display: inline-flex;
            align-items: center;
        }

        .filter-select-wrap .filter-select-icon {
            position: absolute;
            left: var(--space-2);
            color: var(--bs-secondary-color);
            font-size: var(--font-size-sm);
            pointer-events: none;
        }

        .filter-select-wrap .filter-select {
            padding-left: calc(var(--space-2) + 1.1rem);
        }

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

        /* ═══ Toolbar ═══
           Asymmetric vertical spacing — extra breathing room above to separate
           the segment-tabs from the busy chip strip; tighter below so the data
           grid reads as connected to the active tab. Page-level gap is space-3;
           we add space-2 above and pull space-2 off below. */
        .toolbar {
            display: flex;
            align-items: center;
            gap: var(--space-3);
            margin-top: var(--space-2);
            margin-bottom: calc(-1 * var(--space-2));
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

        /* ── Segment Tabs ──
           margin:0 auto self-centers the pill within the flex toolbar so it
           reads as the primary data-view picker, distinct from the chip-strip
           controls above. The mobile filter funnel stays at the toolbar's left
           edge — distinct role, distinct position. */
        .segment-tabs {
            display: inline-flex;
            margin: 0 auto;
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
    private readonly jobFilterTreeSvc = inject(JobFilterTreeService);
    private readonly route = inject(ActivatedRoute);
    protected readonly auth = inject(AuthService);
    private readonly jobService = inject(JobService);
    private readonly filtersStore = inject(ScheduleFiltersStore);
    private readonly http = inject(HttpClient);

    // ── Route state ──
    private jobPath: string | undefined;

    // True only AFTER the initial restore-from-storage pass completes — gates
    // persistence so we don't overwrite saved state before reading it.
    private filtersRestored = false;

    // ── Filter options + capabilities ──
    readonly filterOptions = signal<ScheduleFilterOptionsDto | null>(null);
    readonly capabilities = signal<ScheduleCapabilitiesDto | null>(null);
    readonly filterModalVisible = signal(false);

    // ── Unified CADT/LADT trees from /api/job-filter-tree ──
    readonly cadtTree = signal<CadtClubNode[]>([]);
    readonly ladtTree = signal<LadtAgegroupNode[]>([]);

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

    // ── Direct team multiselect (typeahead) ──
    /** TeamIds the user has explicitly selected via the typeahead OR row star. */
    readonly directTeamIds = signal<string[]>([]);
    /** Syncfusion field-mapping for ejs-multiselect. */
    readonly directTeamFields = { value: 'teamId', text: 'displayText' };

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

    readonly gameClockVisible = signal(false);
    readonly currentJobId = computed(() => this.jobService.currentJob()?.jobId ?? '');
    readonly hasGameClockGames = signal(false);

    // ══════════════════════════════════════════════════════════════════
    // Computed helpers
    // ══════════════════════════════════════════════════════════════════

    readonly eventName = computed(() => this.jobService.currentJob()?.jobName ?? '');

    /** Flat team list derived from the CADT tree, formatted as "{ClubName}:{TeamName}". */
    readonly directTeamOptions = computed<DirectTeamOption[]>(() => {
        const opts: DirectTeamOption[] = [];
        for (const club of this.cadtTree()) {
            const clubLabel = club.clubName?.trim() ?? '';
            for (const ag of club.agegroups ?? []) {
                for (const div of ag.divisions ?? []) {
                    for (const team of div.teams ?? []) {
                        opts.push({
                            teamId: team.teamId,
                            displayText: clubLabel ? `${clubLabel}:${team.teamName}` : team.teamName,
                        });
                    }
                }
            }
        }
        opts.sort((a, b) => a.displayText.localeCompare(b.displayText));
        return opts;
    });

    /** Pluralized count + label for the title badge: "1,286 games" / "1 game" / "0 games". */
    readonly gameCountLabel = computed(() => {
        const n = this.games().length;
        return `${n.toLocaleString()} ${n === 1 ? 'game' : 'games'}`;
    });

    /** Address-first maps query — geocoded addresses pinpoint better than raw lat/lng. */
    readonly fieldMapUrl = computed<string | null>(() => {
        const f = this.fieldInfo();
        if (!f) return null;
        const parts = [f.address, f.city, [f.state, f.zip].filter(Boolean).join(' ')].filter(Boolean);
        if (parts.length > 0) {
            return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(parts.join(', '))}`;
        }
        if (f.latitude != null && f.longitude != null) {
            return `https://www.google.com/maps/search/?api=1&query=${f.latitude},${f.longitude}`;
        }
        return null;
    });

    readonly hasCadtData = computed(() => this.cadtTree().length > 0);
    readonly hasLadtData = computed(() => this.ladtTree().length > 0);

    /** Map of agegroupName → color hex, sourced from the LADT tree. */
    readonly agegroupColors = computed(() => {
        const map: Record<string, string | null> = {};
        for (const ag of this.ladtTree()) {
            map[ag.agegroupName] = ag.color ?? null;
        }
        return map;
    });

    /** Flat team list for edit-game modal team picker (grouped by division) */
    readonly flatTeamList = computed(() => {
        const clubs = this.cadtTree();
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

    readonly cadtFilterCount = computed(() => this.cadtTeamIds().length > 0 ? this.checkedIds.size : 0);
    readonly ladtFilterCount = computed(() => this.ladtTeamIds().length > 0 ? this.ladtCheckedIds.size : 0);

    readonly hasActiveFilters = computed(() => {
        return this.cadtTeamIds().length > 0
            || this.ladtTeamIds().length > 0
            || this.directTeamIds().length > 0
            || this.selectedGameDay() !== ''
            || this.selectedTime() !== ''
            || this.selectedFieldId() !== ''
            || this.unscoredOnly();
    });

    readonly activeFilterCount = computed(() => {
        let count = 0;
        if (this.cadtTeamIds().length > 0) count++;
        if (this.ladtTeamIds().length > 0) count++;
        if (this.directTeamIds().length > 0) count++;
        if (this.selectedGameDay()) count++;
        if (this.selectedTime()) count++;
        if (this.selectedFieldId()) count++;
        if (this.unscoredOnly()) count++;
        return count;
    });

    /** Chips for direct-team selections (typeahead / row-stars). */
    readonly directTeamChips = computed<DirectTeamOption[]>(() => {
        const ids = this.directTeamIds();
        if (ids.length === 0) return [];
        const optionMap = new Map(this.directTeamOptions().map(o => [o.teamId, o.displayText]));
        return ids.map(id => ({ teamId: id, displayText: optionMap.get(id) ?? id }));
    });

    /**
     * Chips for the non-team filters: CADT/LADT tree picks (highest-level checked
     * node only — clubs, agegroups, divisions, teams as appropriate), Date, Time,
     * Location, Unscored. Direct-team selections render separately via directTeamChips.
     */
    readonly activeFilterChips = computed<FilterChip[]>(() => {
        const chips: FilterChip[] = [];
        const opts = this.filterOptions();

        const cadt = this.cadtTree();
        if (cadt.length > 0 && this.cadtTeamIds().length > 0) {
            this.buildTreeChips(cadt, this.checkedIds, 'cadt', chips);
        }

        const ladt = this.ladtTree();
        if (ladt.length > 0 && this.ladtTeamIds().length > 0) {
            this.buildLadtChips(ladt, chips);
        }

        if (this.selectedGameDay()) {
            chips.push({ category: 'Date', label: this.formatGameDay(this.selectedGameDay()), type: 'gameDay' });
        }
        if (this.selectedTime()) {
            chips.push({ category: 'Time', label: this.formatTime(this.selectedTime()), type: 'time' });
        }
        if (this.selectedFieldId()) {
            const field = opts?.fields?.find(f => f.fieldId === this.selectedFieldId());
            chips.push({ category: 'Location', label: field?.fName ?? this.selectedFieldId(), type: 'field' });
        }
        if (this.unscoredOnly()) {
            chips.push({ category: 'Filter', label: 'Unscored only', type: 'unscored' });
        }

        return chips;
    });

    /** Total chip count across both groups — drives the bar's "N" pill and visibility. */
    readonly totalChipCount = computed(() => this.directTeamChips().length + this.activeFilterChips().length);

    // ══════════════════════════════════════════════════════════════════
    // Lifecycle
    // ══════════════════════════════════════════════════════════════════

    ngOnInit(): void {
        const data = this.route.snapshot.data;
        if (data['publicMode']) {
            // jobPath lives on the parent :jobPath route; default paramsInheritanceStrategy
            // ('emptyOnly') doesn't inherit it onto a non-empty-path child like 'schedule'.
            let r: ActivatedRouteSnapshot | null = this.route.snapshot;
            while (r) {
                const jp = r.paramMap.get('jobPath');
                if (jp) { this.jobPath = jp; break; }
                r = r.parent;
            }
        }

        // Restore persisted filters before kicking off the initial data load so the
        // first /games request honors the saved selection (no extra round-trip).
        this.restoreFiltersFromStorage();

        this.svc.getFilterOptions(this.jobPath).subscribe(opts => {
            this.filterOptions.set(opts);
        });

        this.jobFilterTreeSvc.getForJob(this.jobPath).subscribe(tree => {
            this.cadtTree.set(tree.cadt);
            this.ladtTree.set(tree.ladt);
            // After the tree arrives we can attempt the family-roster seed (one-shot,
            // skipped if a localStorage entry already exists for this tournament).
            this.maybeSeedFromFamilyRoster();
        });

        this.svc.getCapabilities(this.jobPath).subscribe(caps => {
            this.capabilities.set(caps);
        });

        this.loadTabData('games');
        this.probeGameClock();
    }

    // ══════════════════════════════════════════════════════════════════
    // Filter persistence + family-roster seed
    // ══════════════════════════════════════════════════════════════════

    /**
     * Hydrate signals from localStorage when an entry exists for this jobPath.
     * Anything not present in storage stays at its default. Marks `filtersRestored`
     * so subsequent persist() calls are safe.
     */
    private restoreFiltersFromStorage(): void {
        const jobPath = this.jobPath;
        if (!jobPath) {
            this.filtersRestored = true;
            return;
        }
        const saved = this.filtersStore.getFor(jobPath);
        if (saved) {
            this.directTeamIds.set([...saved.teamIds]);
            this.selectedGameDay.set(saved.selectedGameDay ?? '');
            this.selectedTime.set(saved.selectedTime ?? '');
            this.selectedFieldId.set(saved.selectedFieldId ?? '');
            this.unscoredOnly.set(!!saved.unscoredOnly);
        }
        this.filtersRestored = true;
    }

    /**
     * Write the current direct-team selection + simple-filter state to localStorage.
     * No-op until the initial restore has completed (gates first-load overwrites).
     */
    private persistFilters(): void {
        if (!this.filtersRestored) return;
        const jobPath = this.jobPath;
        if (!jobPath) return;
        this.filtersStore.patch(jobPath, {
            teamIds: this.directTeamIds(),
            selectedGameDay: this.selectedGameDay(),
            selectedTime: this.selectedTime(),
            selectedFieldId: this.selectedFieldId(),
            unscoredOnly: this.unscoredOnly(),
        });
    }

    /**
     * One-shot: when no localStorage entry exists yet for this jobPath AND the user
     * is logged in as a Family-class account, seed `directTeamIds` from their
     * players' assignedTeamId values. After the seed runs (whether or not it found
     * teams), `seededFromFamily` flips true and the seed never runs again — the
     * user owns their selection from that point forward.
     */
    private maybeSeedFromFamilyRoster(): void {
        const jobPath = this.jobPath;
        if (!jobPath) return;
        if (!this.auth.isAuthenticated()) return;
        // Already have an entry → user owns it; no seeding.
        if (this.filtersStore.getFor(jobPath) !== null) return;

        this.http.get<FamilyPlayersResponseDto>(
            `${environment.apiUrl}/family/players`,
            { params: { jobPath } },
        ).subscribe({
            next: resp => {
                const seeded = new Set<string>();
                for (const player of resp?.familyPlayers ?? []) {
                    for (const reg of player.priorRegistrations ?? []) {
                        if (reg.active && reg.assignedTeamId) seeded.add(reg.assignedTeamId);
                    }
                }
                const ids = [...seeded];
                this.directTeamIds.set(ids);
                this.filtersStore.patch(jobPath, {
                    teamIds: ids,
                    seededFromFamily: true,
                });
                if (ids.length > 0) this.refreshTab();
            },
            error: () => {
                // Transient (network, 5xx) — don't write the seed flag. Next visit
                // will retry. For non-Family authenticated users the endpoint
                // returns 200 with an empty player list, hitting the success path
                // above; this branch should be rare.
            },
        });
    }

    /**
     * One-shot gate: only show the game-clock FAB if the job has a usable interval
     * clock configured AND there's at least one current-live or upcoming RR/PO game.
     *
     * "Configured" means non-zero half-minutes or quarter-minutes. Without them the
     * round-robin game duration computes to 0, so no game ever falls inside an active
     * window — the clock could only ever count down to the next game's start, never
     * show an in-game interval. The vast majority of jobs leave these at 0, so we hide
     * the clock entirely rather than show a misleading next-game countdown.
     * PO support is provisional pending review.
     */
    private probeGameClock(): void {
        const jobId = this.currentJobId();
        if (!jobId) return;
        this.svc.getGameClockConfig(jobId).subscribe({
            next: (cfg) => {
                const configured = (cfg.halfMinutes ?? 0) > 0 || (cfg.quarterMinutes ?? 0) > 0;
                if (!configured) {
                    this.hasGameClockGames.set(false);
                    return;
                }
                this.svc.getActiveGames(jobId).subscribe({
                    next: (data) => {
                        const has = (data.availableRRGameData?.length ?? 0) > 0
                            || (data.availablePOGameData?.length ?? 0) > 0;
                        this.hasGameClockGames.set(has);
                    },
                    error: () => this.hasGameClockGames.set(false)
                });
            },
            error: () => this.hasGameClockGames.set(false)
        });
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
        this.directTeamIds.set([]);
        this.selectedGameDay.set('');
        this.selectedTime.set('');
        this.selectedFieldId.set('');
        this.unscoredOnly.set(false);
        this.openPanel.set(null);
        this.persistFilters();
        this.refreshTab();
    }

    /**
     * Remove a single chip from the active-filters bar. Tree-derived chips
     * walk descendants/ancestors out of the checked set; simple-filter chips
     * just clear their signal. Persistence runs for filters that are stored
     * (simple filters are; trees aren't).
     */
    removeChip(chip: FilterChip): void {
        switch (chip.type) {
            case 'cadt':
                if (chip.nodeId) {
                    const next = new Set(this.checkedIds);
                    next.delete(chip.nodeId);
                    this.removeTreeDescendants(chip.nodeId, next, this.cadtTree());
                    this.removeTreeAncestors(chip.nodeId, next, this.cadtTree());
                    this.checkedIds = next;
                    this.deriveCadtTeamIds();
                    this.refreshTab();
                }
                break;
            case 'ladt':
                if (chip.nodeId) {
                    const next = new Set(this.ladtCheckedIds);
                    next.delete(chip.nodeId);
                    this.removeLadtDescendants(chip.nodeId, next, this.ladtTree());
                    this.removeLadtAncestors(chip.nodeId, next, this.ladtTree());
                    this.ladtCheckedIds = next;
                    this.deriveLadtTeamIds();
                    this.refreshTab();
                }
                break;
            case 'gameDay':
                this.selectedGameDay.set('');
                this.persistFilters();
                this.refreshTab();
                break;
            case 'time':
                this.selectedTime.set('');
                this.persistFilters();
                this.refreshTab();
                break;
            case 'field':
                this.selectedFieldId.set('');
                this.persistFilters();
                this.refreshTab();
                break;
            case 'unscored':
                this.unscoredOnly.set(false);
                this.persistFilters();
                this.refreshTab();
                break;
        }
    }

    /**
     * Single change handler for the team typeahead. Syncfusion emits an array of
     * the currently-selected values via `$event.value`.
     */
    onDirectTeamChange(event: { value?: string[] | null }): void {
        const next = event.value ?? [];
        // Skip no-op events (Syncfusion can fire change during initial value binding)
        const current = this.directTeamIds();
        if (next.length === current.length && next.every((v, i) => v === current[i])) return;
        this.directTeamIds.set([...next]);
        this.persistFilters();
        this.refreshTab();
    }

    /** Toggle a single team's presence in the direct-selection set (used by row stars). */
    toggleDirectTeam(teamId: string): void {
        if (!teamId) return;
        const current = this.directTeamIds();
        const next = current.includes(teamId)
            ? current.filter(id => id !== teamId)
            : [...current, teamId];
        this.directTeamIds.set(next);
        this.persistFilters();
        this.refreshTab();
    }

    /** Catch-all change handler for the simple-filter selects + checkbox. */
    onSimpleFilterChange(): void {
        this.persistFilters();
        this.refreshTab();
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

        // CADT + LADT → merged teamIds (existing intersection logic)
        const cadtTeams = this.cadtTeamIds();
        const ladtTeams = this.ladtTeamIds();
        const directTeams = this.directTeamIds();

        let treeMerged: string[] | null = null;

        if (cadtTeams.length > 0 && ladtTeams.length > 0) {
            // Both active → intersection
            const ladtSet = new Set(ladtTeams);
            const intersection = cadtTeams.filter(id => ladtSet.has(id));
            treeMerged = intersection.length > 0
                ? intersection
                : ['00000000-0000-0000-0000-000000000000']; // sentinel — no real team matches
        } else if (cadtTeams.length > 0) {
            treeMerged = cadtTeams;
        } else if (ladtTeams.length > 0) {
            treeMerged = ladtTeams;
        }

        // Direct team selections (typeahead / star) UNION with tree-derived teamIds —
        // a parent who explicitly picked a team should always see that team's games,
        // independent of any By-Club / By-Age narrowing.
        if (directTeams.length > 0 && treeMerged) {
            req.teamIds = [...new Set([...treeMerged, ...directTeams])];
        } else if (directTeams.length > 0) {
            req.teamIds = [...directTeams];
        } else if (treeMerged) {
            req.teamIds = treeMerged;
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
        const clubs = this.cadtTree();

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
     * LADT IDs are agegroup-rooted (no club layer): ag:<agId>, div:<divId>, team:<teamId>.
     */
    private deriveLadtTeamIds(): void {
        const teamIdSet = new Set<string>();
        const agegroups = this.ladtTree();

        for (const ag of agegroups) {
            if (this.ladtCheckedIds.has(`ag:${ag.agegroupId}`)) {
                for (const div of ag.divisions ?? []) {
                    for (const team of div.teams ?? []) teamIdSet.add(team.teamId);
                }
                continue;
            }
            for (const div of ag.divisions ?? []) {
                if (this.ladtCheckedIds.has(`div:${div.divId}`)) {
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

    /** Build chips for highest-level checked nodes in a CADT tree. */
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

    /** Build chips for LADT — agegroup-rooted IDs (no club layer). */
    private buildLadtChips(agegroups: LadtAgegroupNode[], chips: FilterChip[]): void {
        for (const ag of agegroups) {
            if (this.ladtCheckedIds.has(`ag:${ag.agegroupId}`)) {
                chips.push({ category: 'Age', label: ag.agegroupName, type: 'ladt', nodeId: `ag:${ag.agegroupId}` });
                continue;
            }
            for (const div of ag.divisions ?? []) {
                if (this.ladtCheckedIds.has(`div:${div.divId}`)) {
                    chips.push({ category: 'Division', label: div.divName, type: 'ladt', nodeId: `div:${div.divId}` });
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

    private removeLadtDescendants(nodeId: string, checked: Set<string>, agegroups: LadtAgegroupNode[]): void {
        for (const ag of agegroups) {
            const agId = `ag:${ag.agegroupId}`;
            if (agId === nodeId) {
                for (const div of ag.divisions ?? []) {
                    checked.delete(`div:${div.divId}`);
                    for (const team of div.teams ?? []) checked.delete(`team:${team.teamId}`);
                }
                return;
            }
            for (const div of ag.divisions ?? []) {
                if (`div:${div.divId}` === nodeId) {
                    for (const team of div.teams ?? []) checked.delete(`team:${team.teamId}`);
                    return;
                }
            }
        }
    }

    private removeLadtAncestors(nodeId: string, checked: Set<string>, agegroups: LadtAgegroupNode[]): void {
        for (const ag of agegroups) {
            const agId = `ag:${ag.agegroupId}`;
            for (const div of ag.divisions ?? []) {
                const divId = `div:${div.divId}`;
                if (divId === nodeId) { checked.delete(agId); return; }
                for (const team of div.teams ?? []) {
                    if (`team:${team.teamId}` === nodeId) {
                        checked.delete(divId); checked.delete(agId);
                        return;
                    }
                }
            }
        }
    }

}
