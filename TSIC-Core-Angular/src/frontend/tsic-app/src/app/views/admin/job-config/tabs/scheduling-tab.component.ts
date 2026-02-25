import { Component, inject, ChangeDetectionStrategy, computed, linkedSignal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { JobConfigService } from '../job-config.service';
import { toDateOnly } from '../shared/rte-config';
import type { UpdateJobConfigSchedulingRequest } from '@core/api';

@Component({
  selector: 'app-scheduling-tab',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './scheduling-tab.component.html',
})
export class SchedulingTabComponent implements OnInit {
  protected readonly svc = inject(JobConfigService);

  eventStartDate = linkedSignal(() => toDateOnly(this.svc.scheduling()?.eventStartDate) ?? null);
  eventEndDate = linkedSignal(() => toDateOnly(this.svc.scheduling()?.eventEndDate) ?? null);
  bScheduleAllowPublicAccess = linkedSignal(() => this.svc.scheduling()?.bScheduleAllowPublicAccess ?? null);

  halfMinutes = linkedSignal(() => this.svc.scheduling()?.gameClock?.halfMinutes ?? 0);
  halfTimeMinutes = linkedSignal(() => this.svc.scheduling()?.gameClock?.halfTimeMinutes ?? 0);
  transitionMinutes = linkedSignal(() => this.svc.scheduling()?.gameClock?.transitionMinutes ?? 0);
  playoffMinutes = linkedSignal(() => this.svc.scheduling()?.gameClock?.playoffMinutes ?? 0);
  playoffHalfMinutes = linkedSignal(() => this.svc.scheduling()?.gameClock?.playoffHalfMinutes);
  playoffHalfTimeMinutes = linkedSignal(() => this.svc.scheduling()?.gameClock?.playoffHalfTimeMinutes);
  quarterMinutes = linkedSignal(() => this.svc.scheduling()?.gameClock?.quarterMinutes);
  quarterTimeMinutes = linkedSignal(() => this.svc.scheduling()?.gameClock?.quarterTimeMinutes);
  utcOffsetHours = linkedSignal(() => this.svc.scheduling()?.gameClock?.utcOffsetHours);

  private readonly cleanSnapshot = computed(() => {
    const s = this.svc.scheduling();
    if (!s) return '';
    return JSON.stringify({
      eventStartDate: toDateOnly(s.eventStartDate) ?? null,
      eventEndDate: toDateOnly(s.eventEndDate) ?? null,
      bScheduleAllowPublicAccess: s.bScheduleAllowPublicAccess,
      gameClock: {
        id: s.gameClock?.id ?? 0,
        halfMinutes: s.gameClock?.halfMinutes ?? 0,
        halfTimeMinutes: s.gameClock?.halfTimeMinutes ?? 0,
        transitionMinutes: s.gameClock?.transitionMinutes ?? 0,
        playoffMinutes: s.gameClock?.playoffMinutes ?? 0,
        playoffHalfMinutes: s.gameClock?.playoffHalfMinutes,
        playoffHalfTimeMinutes: s.gameClock?.playoffHalfTimeMinutes,
        quarterMinutes: s.gameClock?.quarterMinutes,
        quarterTimeMinutes: s.gameClock?.quarterTimeMinutes,
        utcOffsetHours: s.gameClock?.utcOffsetHours,
      },
    } satisfies UpdateJobConfigSchedulingRequest);
  });

  ngOnInit(): void {
    this.svc.saveHandler.set(() => this.save());
  }

  onFieldChange(): void {
    if (JSON.stringify(this.buildPayload()) === this.cleanSnapshot()) {
      this.svc.markClean('scheduling');
    } else {
      this.svc.markDirty('scheduling');
    }
  }

  save(): void {
    this.svc.saveScheduling(this.buildPayload());
  }

  private buildPayload(): UpdateJobConfigSchedulingRequest {
    return {
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
  }
}
