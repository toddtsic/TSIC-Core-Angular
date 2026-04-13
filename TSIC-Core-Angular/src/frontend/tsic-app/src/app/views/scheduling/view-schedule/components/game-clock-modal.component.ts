import {
    ChangeDetectionStrategy,
    Component,
    EventEmitter,
    OnDestroy,
    OnInit,
    Output,
    computed,
    inject,
    input,
    signal
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { Subject, Subscription, interval, takeUntil } from 'rxjs';
import type { GameClockAvailableGameTimesDto, GameClockConfigDto, GameClockStartDataDto } from '@core/api';
import { ViewScheduleService } from '../services/view-schedule.service';

type UpdateStatus = 'loading' | 'active-game-updated' | 'multiple-live-games' | 'all-games-played' | 'game-already-played';
type Bucket = 'rr' | 'po';

interface ActiveGameData {
    activeIntervalStart: Date;
    activeIntervalEnd: Date;
    activeIntervalLabel: string;
    remainingDays: number;
    remainingHours: number;
    remainingMinutes: number;
    remainingSeconds: number;
}

interface LiveGame {
    gameStart: Date;
    isRoundRobin: boolean;
    durationMinutes: number;
    gameData: ActiveGameData | null;
}

@Component({
    selector: 'app-game-clock-modal',
    standalone: true,
    imports: [DatePipe],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <div class="modal-backdrop" (click)="close.emit()">
            <div class="modal-card" (click)="$event.stopPropagation()">
                <div class="modal-header">
                    <h3 class="modal-title">Game Clock</h3>
                    <button class="modal-close" (click)="close.emit()" aria-label="Close">&times;</button>
                </div>

                <div class="modal-body">
                    @if (hasBothBuckets()) {
                        <div class="bucket-toggle" role="tablist">
                            <button type="button" role="tab"
                                    [class.active]="selectedBucket() === 'rr'"
                                    (click)="selectBucket('rr')">Round Robin</button>
                            <button type="button" role="tab"
                                    [class.active]="selectedBucket() === 'po'"
                                    (click)="selectBucket('po')">Playoff</button>
                        </div>
                    }

                    @if (status() === 'loading') {
                        <div class="state-card state-neutral">
                            <p>Loading game data&hellip;</p>
                        </div>
                    }

                    @if (status() === 'multiple-live-games') {
                        <div class="state-card state-warning">
                            <div class="state-title">Multiple Live Games</div>
                            <p>Select a game to view the countdown:</p>
                            <ul class="game-list">
                                @for (g of liveGames(); track g.gameStart.getTime()) {
                                    <li>
                                        <button type="button" class="game-item" (click)="selectGame(g)">
                                            <strong>{{ formatGameTime(g.gameStart) }}</strong>
                                            <span class="game-meta">
                                                {{ g.isRoundRobin ? 'Round Robin' : 'Playoff' }}
                                                &middot; {{ g.durationMinutes }} min
                                            </span>
                                        </button>
                                    </li>
                                }
                            </ul>
                        </div>
                    }

                    @if (status() === 'all-games-played' || status() === 'game-already-played') {
                        <div class="state-card state-neutral">
                            <div class="state-title">
                                @if (status() === 'all-games-played') {
                                    All Games Completed
                                } @else {
                                    No Active Games
                                }
                            </div>
                            <p>
                                @if (status() === 'all-games-played') {
                                    All games have been played. Check the standings for final results.
                                } @else {
                                    There are no games currently in progress.
                                }
                            </p>
                        </div>
                    }

                    @if (status() === 'active-game-updated' && activeGame(); as ag) {
                        @if (ag.activeIntervalLabel === 'START') {
                            <div class="pre-start-note">No live games at this time</div>
                        }

                        <div class="countdown-card">
                            <div class="countdown-subtitle">
                                <div>
                                    Time remaining
                                    {{ ag.activeIntervalLabel === 'START' ? ' until the ' : ' in the ' }}
                                </div>
                                <div><span class="highlight">{{ ag.activeIntervalLabel }}</span> of the</div>
                                <div>
                                    <span class="highlight">{{ ag.activeIntervalStart | date: 'M/d/yy hh:mm aa' }}</span>
                                    Game
                                </div>
                            </div>

                            <div class="countdown-row">
                                <div class="time-unit">
                                    <div class="time-value">{{ ag.remainingDays }}</div>
                                    <div class="time-label">D</div>
                                </div>
                                <div class="time-unit">
                                    <div class="time-value">{{ ag.remainingHours }}</div>
                                    <div class="time-label">H</div>
                                </div>
                                <div class="time-unit">
                                    <div class="time-value">{{ ag.remainingMinutes }}</div>
                                    <div class="time-label">M</div>
                                </div>
                                <div class="time-unit">
                                    <div class="time-value">{{ ag.remainingSeconds }}</div>
                                    <div class="time-label">S</div>
                                </div>
                            </div>
                        </div>
                    }
                </div>
            </div>
        </div>
    `,
    styles: [`
        .modal-backdrop {
            position: fixed;
            inset: 0;
            z-index: 1050;
            background: rgba(0, 0, 0, 0.5);
            display: flex;
            align-items: center;
            justify-content: center;
            padding: var(--space-4);
        }
        .modal-card {
            background: var(--bs-body-bg);
            border-radius: var(--bs-border-radius-lg);
            max-width: 520px;
            width: 100%;
            max-height: 90vh;
            overflow-y: auto;
            box-shadow: var(--shadow-lg);
            display: flex;
            flex-direction: column;
        }
        .modal-header {
            display: flex;
            align-items: center;
            justify-content: space-between;
            padding: var(--space-3) var(--space-4);
            border-bottom: 1px solid var(--bs-border-color);
        }
        .modal-title {
            margin: 0;
            font-size: var(--font-size-lg, 1.125rem);
            font-weight: 600;
            color: var(--bs-body-color);
        }
        .modal-close {
            background: none;
            border: none;
            font-size: 1.5rem;
            line-height: 1;
            color: var(--bs-secondary-color);
            cursor: pointer;
            padding: 0 var(--space-1);
        }
        .modal-close:hover { color: var(--bs-body-color); }
        .modal-close:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        .modal-body {
            padding: var(--space-3) var(--space-4);
            overflow-y: auto;
        }

        .bucket-toggle {
            display: flex;
            gap: var(--space-2);
            margin-bottom: var(--space-3);
        }
        .bucket-toggle button {
            flex: 1;
            padding: var(--space-2) var(--space-3);
            border: 1px solid var(--bs-border-color);
            background: var(--bs-body-bg);
            color: var(--bs-body-color);
            border-radius: var(--bs-border-radius);
            cursor: pointer;
            font-weight: 500;
        }
        .bucket-toggle button.active {
            background: var(--bs-primary);
            color: #fff;
            border-color: var(--bs-primary);
        }
        .bucket-toggle button:focus-visible { outline: none; box-shadow: var(--shadow-focus); }

        .state-card {
            padding: var(--space-4);
            border-radius: var(--bs-border-radius);
            text-align: center;
        }
        .state-card p { margin: var(--space-2) 0 0; }
        .state-neutral {
            background: var(--bs-secondary-bg);
            color: var(--bs-body-color);
        }
        .state-warning {
            background: color-mix(in srgb, var(--bs-warning) 20%, transparent);
            color: var(--bs-body-color);
            text-align: left;
        }
        .state-title {
            font-weight: 600;
            font-size: var(--font-size-md, 1rem);
        }

        .game-list {
            list-style: none;
            padding: 0;
            margin: var(--space-3) 0 0;
            display: flex;
            flex-direction: column;
            gap: var(--space-2);
        }
        .game-item {
            width: 100%;
            text-align: left;
            padding: var(--space-2) var(--space-3);
            background: var(--bs-body-bg);
            border: 1px solid var(--bs-border-color);
            border-radius: var(--bs-border-radius);
            cursor: pointer;
            display: flex;
            flex-direction: column;
            gap: 2px;
            color: var(--bs-body-color);
        }
        .game-item:hover { background: var(--bs-tertiary-bg); }
        .game-item:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        .game-meta {
            font-size: var(--font-size-sm, 0.875rem);
            color: var(--bs-secondary-color);
        }

        .pre-start-note {
            text-align: center;
            padding: var(--space-2);
            color: var(--bs-danger);
            font-weight: 600;
            font-size: var(--font-size-sm);
        }

        .countdown-card {
            background: var(--bs-primary);
            color: #fff;
            border-radius: var(--bs-border-radius);
            padding: var(--space-4);
            text-align: center;
        }
        .countdown-subtitle {
            font-size: var(--font-size-sm, 0.875rem);
            line-height: 1.4;
            margin-bottom: var(--space-3);
        }
        .countdown-subtitle .highlight {
            color: #ffd54a;
            font-weight: 700;
            font-size: 1rem;
        }

        .countdown-row {
            display: flex;
            justify-content: center;
            gap: var(--space-3);
            flex-wrap: nowrap;
        }
        .time-unit {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            flex-direction: row-reverse;
        }
        .time-label {
            color: #ffd54a;
            font-size: 0.9rem;
            font-weight: 700;
            text-transform: uppercase;
        }
        .time-value {
            background: #fff;
            color: #111;
            border-radius: var(--bs-border-radius);
            box-shadow: 0 0 5px rgba(0, 0, 0, 0.2);
            min-width: 2.2ex;
            padding: var(--space-1) var(--space-2);
            font-size: 2em;
            font-weight: 600;
            line-height: 1;
        }

        @media (prefers-reduced-motion: reduce) {
            .modal-backdrop, .modal-card { animation: none !important; transition: none !important; }
        }
    `]
})
export class GameClockModalComponent implements OnInit, OnDestroy {
    jobId = input.required<string>();

    @Output() close = new EventEmitter<void>();

    private readonly scheduleService = inject(ViewScheduleService);

    private readonly intervals = signal<GameClockConfigDto | null>(null);
    private readonly rrGames = signal<LiveGame[]>([]);
    private readonly poGames = signal<LiveGame[]>([]);
    readonly selectedBucket = signal<Bucket>('rr');
    private readonly selectedGameStart = signal<Date | undefined>(undefined);
    readonly status = signal<UpdateStatus>('loading');

    readonly hasBothBuckets = computed(() => this.rrGames().length > 0 && this.poGames().length > 0);

    readonly liveGames = computed<LiveGame[]>(() =>
        this.selectedBucket() === 'rr' ? this.rrGames() : this.poGames()
    );

    readonly activeGame = computed<ActiveGameData | null>(() => {
        const games = this.liveGames();
        if (games.length === 1 && games[0].gameData) {
            return games[0].gameData;
        }
        return null;
    });

    private readonly unsubscribe$ = new Subject<void>();
    private countdownSub: Subscription | null = null;

    async ngOnInit(): Promise<void> {
        try {
            const config = await this.loadConfig();
            this.intervals.set(config);
            await this.refresh();
        } catch (err) {
            console.error('Failed to load game clock data', err);
            this.status.set('all-games-played');
        }
    }

    ngOnDestroy(): void {
        this.unsubscribe$.next();
        this.unsubscribe$.complete();
        this.countdownSub?.unsubscribe();
    }

    selectBucket(b: Bucket): void {
        if (this.selectedBucket() === b) return;
        this.selectedBucket.set(b);
        this.selectedGameStart.set(undefined);
        this.refresh();
    }

    async selectGame(g: LiveGame): Promise<void> {
        this.selectedGameStart.set(g.gameStart);
        await this.refresh();
    }

    formatGameTime(d: Date): string {
        return d.toLocaleString('en-US', {
            month: '2-digit', day: '2-digit', year: 'numeric',
            hour: '2-digit', minute: '2-digit', hour12: true
        });
    }

    // ─── Internal ─────────────────────────────────────────────────────────────

    private loadConfig(): Promise<GameClockConfigDto> {
        return new Promise((resolve, reject) => {
            this.scheduleService.getGameClockConfig(this.jobId()).subscribe({
                next: (c) => resolve(c),
                error: (e) => reject(e)
            });
        });
    }

    private loadActive(preferred?: Date): Promise<GameClockAvailableGameTimesDto> {
        return new Promise((resolve, reject) => {
            this.scheduleService.getActiveGames(this.jobId(), preferred).subscribe({
                next: (r) => resolve(r),
                error: (e) => reject(e)
            });
        });
    }

    private async refresh(): Promise<void> {
        const data = await this.loadActive(this.selectedGameStart());
        this.rrGames.set((data.availableRRGameData ?? []).map(this.toLiveGame));
        this.poGames.set((data.availablePOGameData ?? []).map(this.toLiveGame));

        // Auto-select whichever bucket has data if current is empty
        if (this.liveGames().length === 0) {
            const other: Bucket = this.selectedBucket() === 'rr' ? 'po' : 'rr';
            const otherGames = other === 'rr' ? this.rrGames() : this.poGames();
            if (otherGames.length > 0) {
                this.selectedBucket.set(other);
            }
        }

        this.updateActiveGame();
    }

    private toLiveGame(d: GameClockStartDataDto): LiveGame {
        return {
            gameStart: new Date(d.gameStart),
            isRoundRobin: d.isRoundRobin,
            durationMinutes: d.durationMinutes,
            gameData: null
        };
    }

    private updateActiveGame(): void {
        const games = this.liveGames();
        if (games.length === 0) {
            this.stopCountdown();
            this.status.set('all-games-played');
            return;
        }

        if (games.length > 1) {
            this.stopCountdown();
            this.status.set('multiple-live-games');
            return;
        }

        const game = games[0];
        const intervals = this.intervals();
        if (!intervals) {
            this.status.set('loading');
            return;
        }

        const now = this.getNow();
        const gameStart = game.gameStart;
        const phase = this.calculateInterval(gameStart, now, intervals, game.isRoundRobin);

        if (!phase) {
            // Game window fully elapsed. Clear selection and attempt to pull the next game.
            this.stopCountdown();
            if (this.selectedGameStart() !== undefined) {
                this.selectedGameStart.set(undefined);
                this.refresh();
                return;
            }
            this.status.set('game-already-played');
            return;
        }

        const ag: ActiveGameData = {
            activeIntervalStart: gameStart,
            activeIntervalEnd: phase.end,
            activeIntervalLabel: phase.label,
            remainingDays: 0,
            remainingHours: 0,
            remainingMinutes: 0,
            remainingSeconds: 0
        };
        this.tickRemaining(ag, now);

        const updated: LiveGame = { ...game, gameData: ag };
        this.setGameInCurrentBucket(updated);
        this.status.set('active-game-updated');
        this.startCountdown();
    }

    private tickRemaining(ag: ActiveGameData, now: Date): void {
        const ms = ag.activeIntervalEnd.getTime() - now.getTime();
        if (ms <= 0) {
            ag.remainingDays = 0;
            ag.remainingHours = 0;
            ag.remainingMinutes = 0;
            ag.remainingSeconds = 0;
            return;
        }
        let s = Math.floor(ms / 1000);
        ag.remainingSeconds = s % 60;
        let m = Math.floor(s / 60);
        ag.remainingMinutes = m % 60;
        let h = Math.floor(m / 60);
        ag.remainingHours = h % 24;
        ag.remainingDays = Math.floor(h / 24);
    }

    private startCountdown(): void {
        this.countdownSub?.unsubscribe();
        this.countdownSub = interval(1000).pipe(takeUntil(this.unsubscribe$)).subscribe(() => {
            const games = this.liveGames();
            if (games.length !== 1 || !games[0].gameData) return;
            const now = this.getNow();
            const ag = games[0].gameData;
            const ms = ag.activeIntervalEnd.getTime() - now.getTime();
            if (ms <= 0) {
                // Move to next phase
                this.updateActiveGame();
                return;
            }
            const updatedAg: ActiveGameData = { ...ag };
            this.tickRemaining(updatedAg, now);
            const updatedGame: LiveGame = { ...games[0], gameData: updatedAg };
            this.setGameInCurrentBucket(updatedGame);
        });
    }

    private stopCountdown(): void {
        this.countdownSub?.unsubscribe();
        this.countdownSub = null;
    }

    private setGameInCurrentBucket(game: LiveGame): void {
        if (this.selectedBucket() === 'rr') {
            this.rrGames.set([game]);
        } else {
            this.poGames.set([game]);
        }
    }

    private getNow(): Date {
        const now = new Date();
        const eventOffset = this.intervals()?.utcoffsetHours;
        if (eventOffset != null) {
            const mobileOffsetHours = new Date().getTimezoneOffset() / 60;
            if (mobileOffsetHours !== eventOffset) {
                now.setHours(now.getHours() + (mobileOffsetHours - eventOffset));
            }
        }
        return now;
    }

    private calculateInterval(
        gameStart: Date, now: Date, intervals: GameClockConfigDto, isRoundRobin: boolean
    ): { end: Date; label: string } | null {
        const useQuarters = (intervals.quarterMinutes ?? 0) > 0 && (intervals.quarterTimeMinutes ?? 0) > 0;
        return useQuarters
            ? this.calculateQuarterIntervals(gameStart, now, intervals)
            : this.calculateHalfIntervals(gameStart, now, intervals, isRoundRobin);
    }

    private calculateQuarterIntervals(
        gameStart: Date, now: Date, intervals: GameClockConfigDto
    ): { end: Date; label: string } | null {
        const qMin = intervals.quarterMinutes ?? 0;
        const qTimeMin = intervals.quarterTimeMinutes ?? 0;
        const halfTimeMin = intervals.halfTimeMinutes ?? 0;
        const transMin = intervals.transitionMinutes ?? 0;

        const q1End = new Date(gameStart.getTime() + qMin * 60000);
        const q1TimeEnd = new Date(q1End.getTime() + qTimeMin * 60000);
        const q2End = new Date(q1TimeEnd.getTime() + qMin * 60000);
        const halftimeEnd = new Date(q2End.getTime() + halfTimeMin * 60000);
        const q3End = new Date(halftimeEnd.getTime() + qMin * 60000);
        const q3TimeEnd = new Date(q3End.getTime() + qTimeMin * 60000);
        const q4End = new Date(q3TimeEnd.getTime() + qMin * 60000);
        const transitionEnd = new Date(q4End.getTime() + transMin * 60000);

        if (now < gameStart) return { end: gameStart, label: 'START' };
        if (now < q1End) return { end: q1End, label: 'FIRST QUARTER' };
        if (now < q1TimeEnd) return { end: q1TimeEnd, label: 'FIRST QUARTER TIME' };
        if (now < q2End) return { end: q2End, label: 'SECOND QUARTER' };
        if (now < halftimeEnd) return { end: halftimeEnd, label: 'HALF TIME' };
        if (now < q3End) return { end: q3End, label: 'THIRD QUARTER' };
        if (now < q3TimeEnd) return { end: q3TimeEnd, label: 'THIRD QUARTER TIME' };
        if (now < q4End) return { end: q4End, label: 'FOURTH QUARTER' };
        if (now < transitionEnd) return { end: transitionEnd, label: 'TRANSITION' };
        return null;
    }

    private calculateHalfIntervals(
        gameStart: Date, now: Date, intervals: GameClockConfigDto, isRoundRobin: boolean
    ): { end: Date; label: string } | null {
        const halfMin = intervals.halfMinutes ?? 0;
        const halfTimeMin = intervals.halfTimeMinutes ?? 0;
        const transMin = intervals.transitionMinutes ?? 0;
        const hasHalftime = halfTimeMin > 0;

        const h1End = new Date(gameStart.getTime() + halfMin * 60000);
        let halftimeEnd: Date | null = null;
        let h2End: Date | null = null;
        let transitionEnd: Date;

        if (hasHalftime) {
            halftimeEnd = new Date(h1End.getTime() + halfTimeMin * 60000);
            h2End = new Date(halftimeEnd.getTime() + halfMin * 60000);
            transitionEnd = new Date(h2End.getTime() + transMin * 60000);
        } else {
            transitionEnd = new Date(h1End.getTime() + transMin * 60000);
        }

        if (now < gameStart) return { end: gameStart, label: 'START' };
        if (now < h1End && isRoundRobin) {
            return { end: h1End, label: hasHalftime ? 'FIRST HALF' : 'ONLY HALF' };
        }
        if (halftimeEnd && now < halftimeEnd && hasHalftime) {
            return { end: halftimeEnd, label: 'HALF TIME' };
        }
        if (h2End && now < h2End && hasHalftime) {
            return { end: h2End, label: 'SECOND HALF' };
        }
        if (now < transitionEnd) {
            return { end: transitionEnd, label: 'TRANSITION' };
        }
        return null;
    }
}
