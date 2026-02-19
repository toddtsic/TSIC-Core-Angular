import { Component, inject, ChangeDetectionStrategy, signal, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { JobConfigService } from '../job-config.service';
import type { UpdateJobConfigSchedulingRequest } from '@core/api';

@Component({
  selector: 'app-scheduling-tab',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './scheduling-tab.component.html',
})
export class SchedulingTabComponent {
  protected readonly svc = inject(JobConfigService);

  eventStartDate = signal<string | null>(null);
  eventEndDate = signal<string | null>(null);
  bScheduleAllowPublicAccess = signal<boolean | null>(null);

  halfMinutes = signal(0);
  halfTimeMinutes = signal(0);
  transitionMinutes = signal(0);
  playoffMinutes = signal(0);
  playoffHalfMinutes = signal<number | undefined>(undefined);
  playoffHalfTimeMinutes = signal<number | undefined>(undefined);
  quarterMinutes = signal<number | undefined>(undefined);
  quarterTimeMinutes = signal<number | undefined>(undefined);
  utcOffsetHours = signal<number | undefined>(undefined);

  constructor() {
    effect(() => {
      const s = this.svc.scheduling();
      if (!s) return;
      this.eventStartDate.set(s.eventStartDate);
      this.eventEndDate.set(s.eventEndDate);
      this.bScheduleAllowPublicAccess.set(s.bScheduleAllowPublicAccess);
      if (s.gameClock) {
        this.halfMinutes.set(s.gameClock.halfMinutes);
        this.halfTimeMinutes.set(s.gameClock.halfTimeMinutes);
        this.transitionMinutes.set(s.gameClock.transitionMinutes);
        this.playoffMinutes.set(s.gameClock.playoffMinutes);
        this.playoffHalfMinutes.set(s.gameClock.playoffHalfMinutes);
        this.playoffHalfTimeMinutes.set(s.gameClock.playoffHalfTimeMinutes);
        this.quarterMinutes.set(s.gameClock.quarterMinutes);
        this.quarterTimeMinutes.set(s.gameClock.quarterTimeMinutes);
        this.utcOffsetHours.set(s.gameClock.utcOffsetHours);
      }
    });
  }

  onFieldChange(): void { this.svc.markDirty('scheduling'); }

  save(): void {
    const req: UpdateJobConfigSchedulingRequest = {
      eventStartDate: this.eventStartDate(),
      eventEndDate: this.eventEndDate(),
      bScheduleAllowPublicAccess: this.bScheduleAllowPublicAccess(),
      gameClock: {
        id: this.svc.scheduling()?.gameClock?.id ?? 0,
        halfMinutes: this.halfMinutes(),
        halfTimeMinutes: this.halfTimeMinutes(),
        transitionMinutes: this.transitionMinutes(),
        playoffMinutes: this.playoffMinutes(),
        playoffHalfMinutes: this.playoffHalfMinutes(),
        playoffHalfTimeMinutes: this.playoffHalfTimeMinutes(),
        quarterMinutes: this.quarterMinutes(),
        quarterTimeMinutes: this.quarterTimeMinutes(),
        utcOffsetHours: this.utcOffsetHours(),
      },
    };
    this.svc.saveScheduling(req);
  }
}
