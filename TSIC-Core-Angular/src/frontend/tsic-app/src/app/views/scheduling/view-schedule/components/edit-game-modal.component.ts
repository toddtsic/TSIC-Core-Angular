import {
    ChangeDetectionStrategy, Component, computed, effect, EventEmitter,
    input, Output, signal
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import type { ViewGameDto, EditGameRequest, GameStatusOptionDto } from '@core/api';

export interface TeamOption {
    teamId: string;
    teamName: string;
    divName: string;
}

@Component({
    selector: 'app-edit-game-modal',
    standalone: true,
    imports: [FormsModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @if (visible()) {
            <div class="detail-backdrop" (click)="close.emit()"></div>
        }
        <div class="detail-panel" [class.open]="visible()">
            <div class="panel-header">
                <div class="header-top-row">
                    <h3 class="panel-title">{{ isBracketMode() ? 'Score' : 'Edit Game #' + game()?.gid }}</h3>
                    <button class="btn-close" (click)="close.emit()" aria-label="Close">&times;</button>
                </div>
            </div>

            <div class="panel-body">
                @if (game(); as g) {
                    @if (isBracketMode()) {
                        <div class="panel-card">
                            <h4 class="panel-card-title">Score</h4>
                            <div class="score-row">
                                <span class="team-label">{{ t1Name() }}</span>
                                <input type="number" class="form-input score-box"
                                       min="0" max="99"
                                       [ngModel]="t1Score()"
                                       (ngModelChange)="t1Score.set($event)"
                                       (keydown.enter)="onSave()" />
                            </div>
                            <div class="score-row">
                                <span class="team-label">{{ t2Name() }}</span>
                                <input type="number" class="form-input score-box"
                                       min="0" max="99"
                                       [ngModel]="t2Score()"
                                       (ngModelChange)="t2Score.set($event)"
                                       (keydown.enter)="onSave()" />
                            </div>
                        </div>
                    } @else {
                        <div class="panel-card">
                            <h4 class="panel-card-title">Team 1</h4>

                            <div class="form-group">
                                <label class="form-label">Team</label>
                                <select class="form-input"
                                        [ngModel]="t1Id()"
                                        (ngModelChange)="onTeamChange(1, $event)">
                                    <option [ngValue]="null">(no team)</option>
                                    @for (group of groupedTeams(); track group.divName) {
                                        <optgroup [label]="group.divName">
                                            @for (t of group.teams; track t.teamId) {
                                                <option [ngValue]="t.teamId">{{ t.teamName }}</option>
                                            }
                                        </optgroup>
                                    }
                                </select>
                            </div>

                            <div class="form-row">
                                <div class="form-group form-group-half">
                                    <label class="form-label">Score</label>
                                    <input type="number" class="form-input"
                                           min="0" max="99"
                                           [ngModel]="t1Score()"
                                           (ngModelChange)="t1Score.set($event)" />
                                </div>
                                <div class="form-group form-group-half">
                                    <label class="form-label">Annotation</label>
                                    <input type="text" class="form-input"
                                           [ngModel]="t1Ann()"
                                           (ngModelChange)="t1Ann.set($event)" />
                                </div>
                            </div>
                        </div>

                        <div class="panel-card">
                            <h4 class="panel-card-title">Team 2</h4>

                            <div class="form-group">
                                <label class="form-label">Team</label>
                                <select class="form-input"
                                        [ngModel]="t2Id()"
                                        (ngModelChange)="onTeamChange(2, $event)">
                                    <option [ngValue]="null">(no team)</option>
                                    @for (group of groupedTeams(); track group.divName) {
                                        <optgroup [label]="group.divName">
                                            @for (t of group.teams; track t.teamId) {
                                                <option [ngValue]="t.teamId">{{ t.teamName }}</option>
                                            }
                                        </optgroup>
                                    }
                                </select>
                            </div>

                            <div class="form-row">
                                <div class="form-group form-group-half">
                                    <label class="form-label">Score</label>
                                    <input type="number" class="form-input"
                                           min="0" max="99"
                                           [ngModel]="t2Score()"
                                           (ngModelChange)="t2Score.set($event)" />
                                </div>
                                <div class="form-group form-group-half">
                                    <label class="form-label">Annotation</label>
                                    <input type="text" class="form-input"
                                           [ngModel]="t2Ann()"
                                           (ngModelChange)="t2Ann.set($event)" />
                                </div>
                            </div>
                        </div>

                        <div class="panel-card">
                            <h4 class="panel-card-title">Status</h4>
                            <div class="form-group">
                                <select class="form-input status-select"
                                        [ngModel]="gStatusCode()"
                                        (ngModelChange)="gStatusCode.set(+$event)">
                                    @for (opt of statusOptions(); track opt.code) {
                                        <option [value]="opt.code">{{ capitalize(opt.text) }}</option>
                                    }
                                </select>
                            </div>
                        </div>
                    }
                }
            </div>

            <div class="panel-footer">
                <button class="btn btn-outline-secondary btn-sm" (click)="close.emit()">Cancel</button>
                <button class="btn btn-primary btn-sm" (click)="onSave()">Save</button>
            </div>
        </div>
    `,
    styles: [`
        /* Framed card inside panel body — matches search/registrations .contact-zone */
        .panel-card {
            padding: var(--space-4);
            border: 1px solid var(--bs-primary);
            border-radius: var(--radius-lg);
            background: var(--surface-elevated-bg);
            margin-bottom: var(--space-4);
            display: flex;
            flex-direction: column;
            gap: var(--space-3);
        }

        .panel-card:last-of-type {
            margin-bottom: 0;
        }

        .panel-card-title {
            margin: 0;
            font-size: var(--font-size-sm);
            font-weight: var(--font-weight-semibold);
            color: var(--bs-primary);
            text-transform: uppercase;
            letter-spacing: 0.05em;
        }

        .form-group {
            display: flex;
            flex-direction: column;
            gap: var(--space-1);
        }

        .form-row {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: var(--space-3);
        }

        .form-group-half {
            min-width: 0;
        }

        .form-label {
            font-size: var(--font-size-xs);
            font-weight: var(--font-weight-medium);
            color: var(--text-muted, var(--bs-secondary-color));
        }

        .form-input {
            width: 100%;
            padding: var(--space-1) var(--space-2);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--bs-border-radius);
            background: var(--bs-body-bg);
            color: var(--bs-body-color);
            font-size: var(--font-size-sm);
            outline: none;
            box-sizing: border-box;
        }

        .form-input:focus {
            border-color: var(--bs-primary);
            box-shadow: 0 0 0 2px color-mix(in srgb, var(--bs-primary) 25%, transparent);
        }

        select.form-input {
            cursor: pointer;
        }

        /* ── Compact bracket score layout ── */
        .score-row {
            display: flex;
            align-items: center;
            gap: var(--space-3);
            padding: var(--space-2) 0;
        }

        .score-row + .score-row {
            border-top: 1px solid var(--bs-border-color);
        }

        .team-label {
            flex: 1;
            font-size: var(--font-size-sm);
            font-weight: 600;
            color: var(--bs-body-color);
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .score-box {
            width: 64px;
            flex: none;
            text-align: center;
            font-size: var(--font-size-base);
            font-weight: 700;
            font-variant-numeric: tabular-nums;
        }
    `]
})
export class EditGameModalComponent {
    game = input<ViewGameDto | null>(null);
    visible = input<boolean>(false);
    teams = input<TeamOption[]>([]);
    statusOptions = input<GameStatusOptionDto[]>([]);

    @Output() close = new EventEmitter<void>();
    @Output() save = new EventEmitter<EditGameRequest>();

    /** Bracket mode: mock game from brackets has empty gDate */
    readonly isBracketMode = computed(() => !this.game()?.gDate);

    /** Teams grouped by division for <optgroup> rendering */
    readonly groupedTeams = computed(() => {
        const flat = this.teams();
        const map = new Map<string, TeamOption[]>();
        for (const t of flat) {
            let arr = map.get(t.divName);
            if (!arr) { arr = []; map.set(t.divName, arr); }
            arr.push(t);
        }
        return [...map.entries()].map(([divName, teams]) => ({ divName, teams }));
    });

    // Local form state signals
    readonly t1Id = signal<string | null>(null);
    readonly t2Id = signal<string | null>(null);
    readonly t1Name = signal('');
    readonly t2Name = signal('');
    readonly t1Score = signal<number | undefined>(undefined);
    readonly t2Score = signal<number | undefined>(undefined);
    readonly t1Ann = signal('');
    readonly t2Ann = signal('');
    readonly gStatusCode = signal<number>(1);

    constructor() {
        // Populate form signals when visibility changes (game becomes available)
        effect(() => {
            const isVisible = this.visible();
            const g = this.game();
            if (isVisible && g) {
                this.t1Id.set(g.t1Id ?? null);
                this.t2Id.set(g.t2Id ?? null);
                this.t1Name.set(g.t1Name ?? '');
                this.t2Name.set(g.t2Name ?? '');
                this.t1Score.set(g.t1Score ?? undefined);
                this.t2Score.set(g.t2Score ?? undefined);
                this.t1Ann.set(g.t1Ann ?? '');
                this.t2Ann.set(g.t2Ann ?? '');
                this.gStatusCode.set(g.gStatusCode ?? 1);
            }
        });
    }

    /** When a team is selected from dropdown, update both ID and name */
    onTeamChange(slot: 1 | 2, teamId: string | null): void {
        const name = teamId
            ? this.teams().find(t => t.teamId === teamId)?.teamName ?? ''
            : '';
        if (slot === 1) {
            this.t1Id.set(teamId);
            this.t1Name.set(name);
        } else {
            this.t2Id.set(teamId);
            this.t2Name.set(name);
        }
    }

    capitalize(s: string): string {
        return s ? s.charAt(0).toUpperCase() + s.slice(1) : s;
    }

    onSave(): void {
        const g = this.game();
        if (!g) return;

        const request: EditGameRequest = {
            gid: g.gid,
            t1Id: this.t1Id(),
            t2Id: this.t2Id(),
            t1Name: this.t1Name() || null,
            t2Name: this.t2Name() || null,
            t1Score: this.t1Score(),
            t2Score: this.t2Score(),
            t1Ann: this.t1Ann() || null,
            t2Ann: this.t2Ann() || null,
            gStatusCode: this.gStatusCode()
        };

        this.save.emit(request);
    }
}
