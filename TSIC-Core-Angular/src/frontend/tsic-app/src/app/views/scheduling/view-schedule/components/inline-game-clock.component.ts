import {
    ChangeDetectionStrategy,
    Component,
    EventEmitter,
    OnDestroy,
    OnInit,
    Output,
    inject,
    input,
    signal
} from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { Subject, Subscription, interval, takeUntil } from 'rxjs';
import type { GameClockAvailableGameTimesDto, GameClockConfigDto } from '@core/api';
import { ViewScheduleService } from '../services/view-schedule.service';

type InlineStatus = 'hidden' | 'loading' | 'active' | 'completed';

interface InlineActiveGame {
    gameStart: Date;
    isRoundRobin: boolean;
    activeIntervalLabel: string;
    activeIntervalEnd: Date;
    remainingDays: number;
    remainingHours: number;
    remainingMinutes: number;
    remainingSeconds: number;
}

@Component({
    selector: 'app-inline-game-clock',
    standalone: true,
    imports: [DatePipe, DecimalPipe],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @if (status() !== 'hidden') {
            <button type="button" class="inline-clock" [class.completed]="status() === 'completed'"
                    (click)="onClick()" aria-label="Open game clock">
                @if (status() === 'active' && activeGame(); as ag) {
                    <div class="clock-left">
                        <div class="state-chip" [attr.data-state]="stateKey(ag.activeIntervalLabel)">
                            <span class="state-dot"></span>
                            <span class="state-text">{{ stateLabel(ag.activeIntervalLabel) }}</span>
                        </div>
                        <div class="slot-label">
                            {{ ag.gameStart | date: 'EEE M/d h:mm a' }} Game
                        </div>
                    </div>
                    <div class="clock-right">
                        @if (ag.remainingDays > 0) {
                            <div class="time-unit">
                                <div class="time-value">{{ ag.remainingDays }}</div>
                                <div class="time-label">DAYS</div>
                            </div>
                            <span class="sep">:</span>
                        }
                        <div class="time-unit">
                            <div class="time-value">{{ ag.remainingHours | number: '2.0-0' }}</div>
                            <div class="time-label">HRS</div>
                        </div>
                        <span class="sep">:</span>
                        <div class="time-unit">
                            <div class="time-value">{{ ag.remainingMinutes | number: '2.0-0' }}</div>
                            <div class="time-label">MIN</div>
                        </div>
                        <span class="sep">:</span>
                        <div class="time-unit seconds" [class.tick]="tickFlash()">
                            <div class="time-value">{{ ag.remainingSeconds | number: '2.0-0' }}</div>
                            <div class="time-label">SEC</div>
                        </div>
                    </div>
                } @else if (status() === 'completed') {
                    <div class="state-chip" data-state="completed">
                        <span class="state-dot"></span>
                        <span class="state-text">COMPLETED</span>
                    </div>
                } @else if (status() === 'loading') {
                    <div class="state-chip" data-state="upcoming">
                        <span class="state-dot"></span>
                        <span class="state-text">LOADING</span>
                    </div>
                }
            </button>
        }
    `,
    styles: [`
        :host {
            display: flex;
            justify-content: center;
            flex: 1;
            min-width: 0;
        }

        .inline-clock {
            display: inline-flex;
            align-items: center;
            gap: var(--space-4);
            padding: var(--space-2) var(--space-3);
            background: transparent;
            border: none;
            border-radius: var(--bs-border-radius);
            cursor: pointer;
            font: inherit;
            color: inherit;
            transition: background 150ms ease-out;
        }
        .inline-clock:hover {
            background: color-mix(in srgb, var(--bs-body-color) 5%, transparent);
        }
        .inline-clock:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
        }
        .inline-clock.completed {
            padding: var(--space-1) var(--space-2);
        }

        .clock-left {
            display: flex;
            flex-direction: column;
            align-items: flex-start;
            gap: 2px;
        }
        .clock-right {
            display: flex;
            align-items: flex-start;
            gap: var(--space-2);
        }

        .state-chip {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
            padding: 2px 10px;
            background: color-mix(in srgb, var(--bs-body-color) 8%, transparent);
            border-radius: 999px;
            font-size: 0.65rem;
            font-weight: 700;
            letter-spacing: 0.1em;
        }
        .state-dot {
            width: 7px;
            height: 7px;
            border-radius: 50%;
            background: currentColor;
        }
        .state-chip[data-state="upcoming"]   { color: var(--bs-secondary-color); }
        .state-chip[data-state="live"]       { color: var(--bs-danger); }
        .state-chip[data-state="halftime"]   { color: var(--bs-warning); }
        .state-chip[data-state="transition"] { color: var(--bs-secondary-color); }
        .state-chip[data-state="completed"]  { color: var(--bs-secondary-color); }

        @media (prefers-reduced-motion: no-preference) {
            .state-chip[data-state="live"] .state-dot {
                animation: pulse-dot-inline 1.4s ease-in-out infinite;
            }
            .state-chip[data-state="upcoming"] .state-dot {
                animation: pulse-dot-soft 1.8s ease-in-out infinite;
            }
        }
        @keyframes pulse-dot-inline {
            0%, 100% { opacity: 1; transform: scale(1); }
            50%      { opacity: 0.4; transform: scale(1.3); }
        }
        @keyframes pulse-dot-soft {
            0%, 100% { opacity: 1; }
            50%      { opacity: 0.55; }
        }

        .slot-label {
            font-size: 0.75rem;
            color: var(--bs-secondary-color);
            font-weight: 500;
            white-space: nowrap;
        }

        .time-unit {
            display: flex;
            flex-direction: column;
            align-items: center;
            gap: 1px;
        }
        .sep {
            font-size: 1rem;
            color: var(--bs-secondary-color);
            font-weight: 700;
            line-height: 1.5rem;
            align-self: flex-start;
        }
        .time-value {
            font-family: var(--font-mono, ui-monospace, 'Cascadia Mono', Consolas, monospace);
            font-variant-numeric: tabular-nums;
            font-size: 1.5rem;
            font-weight: 700;
            line-height: 1;
            color: var(--bs-body-color);
            background: color-mix(in srgb, var(--bs-primary) 8%, var(--bs-body-bg));
            border: 1px solid color-mix(in srgb, var(--bs-primary) 25%, transparent);
            border-radius: var(--bs-border-radius);
            padding: 2px 8px;
            box-shadow: 0 1px 2px rgba(0,0,0,0.06);
            min-width: 2.2ex;
            text-align: center;
        }
        .time-label {
            font-size: 0.6rem;
            font-weight: 700;
            color: #ca8a04;
            letter-spacing: 0.05em;
        }

        .time-unit.seconds .time-value {
            transition: transform 150ms ease-out;
        }
        .time-unit.seconds.tick .time-value {
            transform: scale(1.08);
        }

        @media (prefers-reduced-motion: reduce) {
            .time-unit.seconds .time-value,
            .time-unit.seconds.tick .time-value {
                transition: none !important;
                transform: none !important;
            }
            .state-chip[data-state="live"] .state-dot { animation: none !important; }
        }

        @media (max-width: 768px) {
            :host { display: none !important; }
        }
    `]
})
export class InlineGameClockComponent implements OnInit, OnDestroy {
    jobId = input.required<string>();

    @Output() expand = new EventEmitter<void>();

    private readonly scheduleService = inject(ViewScheduleService);

    private readonly config = signal<GameClockConfigDto | null>(null);

    readonly status = signal<InlineStatus>('loading');
    readonly activeGame = signal<InlineActiveGame | null>(null);
    readonly tickFlash = signal(false);

    private readonly unsubscribe$ = new Subject<void>();
    private countdownSub: Subscription | null = null;

    ngOnInit(): void {
        const id = this.jobId();
        if (id) this.bootstrap(id);
    }

    ngOnDestroy(): void {
        this.unsubscribe$.next();
        this.unsubscribe$.complete();
        this.countdownSub?.unsubscribe();
    }

    onClick(): void {
        this.expand.emit();
    }

    stateKey(label: string): 'upcoming' | 'live' | 'halftime' | 'transition' {
        if (label === 'START') return 'upcoming';
        if (label === 'HALF TIME' || label === 'FIRST QUARTER TIME' || label === 'THIRD QUARTER TIME') return 'halftime';
        if (label === 'TRANSITION') return 'transition';
        return 'live';
    }

    stateLabel(label: string): string {
        const key = this.stateKey(label);
        switch (key) {
            case 'upcoming':   return 'UPCOMING';
            case 'live':       return 'LIVE';
            case 'halftime':   return 'HALFTIME';
            case 'transition': return 'BREAK';
        }
    }

    // ─── Internal ─────────────────────────────────────────────────────────────

    private async bootstrap(jobId: string): Promise<void> {
        try {
            if (!this.config()) {
                const c = await this.loadConfig(jobId);
                this.config.set(c);
            }
            await this.refresh(jobId);
        } catch (err) {
            console.error('Inline game clock bootstrap failed', err);
            this.status.set('hidden');
        }
    }

    private loadConfig(jobId: string): Promise<GameClockConfigDto> {
        return new Promise((resolve, reject) => {
            this.scheduleService.getGameClockConfig(jobId).subscribe({
                next: (c) => resolve(c),
                error: (e) => reject(e)
            });
        });
    }

    private loadActive(jobId: string): Promise<GameClockAvailableGameTimesDto> {
        return new Promise((resolve, reject) => {
            this.scheduleService.getActiveGames(jobId).subscribe({
                next: (r) => resolve(r),
                error: (e) => reject(e)
            });
        });
    }

    private async refresh(jobId: string): Promise<void> {
        const data = await this.loadActive(jobId);
        const rrGames = (data.availableRRGameData ?? [])
            .map(d => ({
                gameStart: new Date(d.gameStart),
                isRoundRobin: d.isRoundRobin,
                durationMinutes: d.durationMinutes
            }))
            .sort((a, b) => a.gameStart.getTime() - b.gameStart.getTime());

        if (rrGames.length === 0) {
            this.stopCountdown();
            this.status.set('completed');
            this.activeGame.set(null);
            return;
        }

        this.computeActive(rrGames[0].gameStart, rrGames[0].isRoundRobin);
    }

    private computeActive(gameStart: Date, isRoundRobin: boolean): void {
        const intervals = this.config();
        if (!intervals) {
            this.status.set('loading');
            return;
        }

        const now = this.getNow();
        const phase = this.calculateInterval(gameStart, now, intervals, isRoundRobin);
        if (!phase) {
            this.stopCountdown();
            this.status.set('completed');
            this.activeGame.set(null);
            return;
        }

        const ag: InlineActiveGame = {
            gameStart,
            isRoundRobin,
            activeIntervalLabel: phase.label,
            activeIntervalEnd: phase.end,
            remainingDays: 0,
            remainingHours: 0,
            remainingMinutes: 0,
            remainingSeconds: 0
        };
        this.tickRemaining(ag, now);
        this.activeGame.set(ag);
        this.status.set('active');
        this.startCountdown();
    }

    private tickRemaining(ag: InlineActiveGame, now: Date): void {
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
            const ag = this.activeGame();
            if (!ag) return;
            const now = this.getNow();
            const ms = ag.activeIntervalEnd.getTime() - now.getTime();
            if (ms <= 0) {
                const id = this.jobId();
                if (id) this.refresh(id);
                return;
            }
            const updated: InlineActiveGame = { ...ag };
            this.tickRemaining(updated, now);
            this.activeGame.set(updated);
            this.tickFlash.set(true);
            setTimeout(() => this.tickFlash.set(false), 150);
        });
    }

    private stopCountdown(): void {
        this.countdownSub?.unsubscribe();
        this.countdownSub = null;
    }

    private getNow(): Date {
        const now = new Date();
        const eventOffset = this.config()?.utcoffsetHours;
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
