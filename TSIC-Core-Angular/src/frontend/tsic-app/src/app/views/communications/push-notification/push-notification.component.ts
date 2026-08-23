import { Component, ChangeDetectionStrategy, inject, signal, computed, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { GridAllModule, SortSettingsModel } from '@syncfusion/ej2-angular-grids';
import { PushNotificationService } from './services/push-notification.service';
import type { PushNotificationHistoryDto, PushNotificationReadinessDto } from '../../../core/api';

/** A single unmet delivery condition, rendered as an alert above the send form. */
export interface PushReadinessWarning {
  level: 'danger' | 'warning';
  icon: string;
  text: string;
}

/** reference.JobTypes — showcase runs neither mobile app, so it gets its own explanation. */
const SHOWCASE_JOB_TYPE = 6;

@Component({
  selector: 'app-push-notification',
  standalone: true,
  imports: [FormsModule, GridAllModule],
  templateUrl: './push-notification.component.html',
  styleUrl: './push-notification.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PushNotificationComponent implements OnInit {
  private readonly pushService = inject(PushNotificationService);

  // UI state
  isLoading = signal(false);
  isSending = signal(false);
  errorMessage = signal<string | null>(null);
  successMessage = signal<string | null>(null);

  // Data
  readiness = signal<PushNotificationReadinessDto | null>(null);
  pushText = signal('');
  history = signal<PushNotificationHistoryDto[]>([]);

  // Computed
  /**
   * The one app this job feeds. A job is never both: TSIC-Events and TSIC-Teams are separate
   * Firebase projects, and a token minted by one is rejected by the other. The backend resolves
   * it from job type plus the TSIC-Teams switch; this screen only reports the result.
   */
  audience = computed(() => this.readiness()?.audience ?? 'None');
  audienceLabel = computed(() => {
    switch (this.audience()) {
      case 'Events': return 'TSIC-Events';
      case 'Teams': return 'TSIC-Teams';
      default: return 'No mobile app';
    }
  });

  /** Devices in the resolved audience's pool — who a send would actually reach. */
  deviceCount = computed(() => this.readiness()?.deviceCount ?? 0);

  hasAudience = computed(() => this.audience() !== 'None');
  canSend = computed(() =>
    this.pushText().trim().length > 0 && !this.isSending() && this.hasAudience());

  /**
   * Unmet delivery conditions. The screen is deliberately always reachable — the nav
   * used to hide it on a flag that had nothing to do with mobile, which made "why is
   * the menu item missing" unanswerable from the UI. Everything that would stop a send
   * from landing is stated here instead.
   */
  warnings = computed<PushReadinessWarning[]>(() => {
    const r = this.readiness();
    if (!r) return [];

    const list: PushReadinessWarning[] = [];

    // No audience at all — nothing else matters, so this is the only thing said.
    if (r.audience === 'None') {
      list.push({
        level: 'danger',
        icon: 'bi-x-octagon-fill',
        text: r.jobTypeId === SHOWCASE_JOB_TYPE
          ? 'Showcase events run neither mobile app, so there is nobody to push to.'
          : 'This event feeds no mobile app. Turn on Enable TSIC Teams under ' +
            'Job Settings → Mobile/Store to give it an audience.'
      });
      return list;
    }

    if (!r.senderConfigured) {
      list.push({
        level: 'danger',
        icon: 'bi-key-fill',
        text: `No Firebase sender is configured for ${this.audienceLabel()}, so a send would ` +
              'fail. The other app\'s credential cannot deliver to these devices.'
      });
    }

    if (r.deviceCount === 0) {
      list.push({
        level: 'warning',
        icon: 'bi-phone-slash',
        text: `No ${this.audienceLabel()} devices have registered for this event yet. ` +
              'A send will reach nobody.'
      });
    }

    // Registered devices outlive the switch that hid the event from the app.
    if (r.audience === 'Events' && !r.eventsEnabled && r.deviceCount > 0) {
      list.push({
        level: 'warning',
        icon: 'bi-eye-slash',
        text: `This event is hidden from the TSIC-Events app, but ${r.deviceCount} ` +
              'device(s) are still registered from before it was hidden. They will receive this push.'
      });
    }

    return list;
  });

  // Grid settings
  sortSettings: SortSettingsModel = { columns: [{ field: 'sentWhen', direction: 'Descending' }] };

  ngOnInit(): void {
    this.loadReadiness();
    this.loadHistory();
  }

  private loadReadiness(): void {
    this.pushService.getReadiness().subscribe({
      next: (data) => this.readiness.set(data),
      error: () => { /* Readiness is advisory — a failure must not block the screen */ }
    });
  }

  private loadHistory(): void {
    this.isLoading.set(true);
    this.pushService.getHistory().subscribe({
      next: (data) => {
        this.history.set(data);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.errorMessage.set(err.error?.message || 'Failed to load notification history.');
        this.isLoading.set(false);
      }
    });
  }

  sendPush(): void {
    const text = this.pushText().trim();
    if (!text) return;

    this.isSending.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    this.pushService.sendPush(text).subscribe({
      next: (response) => {
        this.successMessage.set(response.message);
        this.pushText.set('');
        this.isSending.set(false);
        this.loadReadiness();
        this.loadHistory();
      },
      error: (err) => {
        this.errorMessage.set(err.error?.message || 'Failed to send push notification.');
        this.isSending.set(false);
      }
    });
  }
}
