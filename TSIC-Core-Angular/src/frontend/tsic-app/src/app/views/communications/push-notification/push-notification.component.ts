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
  /** The pool this screen actually sends to: devices registered against the job (TSIC-Events). */
  deviceCount = computed(() => this.readiness()?.eventsDeviceCount ?? 0);
  canSend = computed(() => this.pushText().trim().length > 0 && !this.isSending());

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

    if (!r.eventsEnabled && !r.teamsEnabled) {
      list.push({
        level: 'danger',
        icon: 'bi-x-octagon-fill',
        text: 'Neither mobile app is enabled for this event. Turn on TSIC-Events Enabled or ' +
              'Enable TSIC Teams under Job Settings → Mobile/Store before sending.'
      });
    }

    if (r.eventsEnabled && r.eventsDeviceCount === 0) {
      list.push({
        level: 'warning',
        icon: 'bi-phone-slash',
        text: 'TSIC-Events is enabled but no devices have registered for this event yet. ' +
              'A send will reach nobody.'
      });
    }

    if (!r.eventsEnabled && r.eventsDeviceCount > 0) {
      list.push({
        level: 'warning',
        icon: 'bi-eye-slash',
        text: `This event is hidden from the TSIC-Events app, but ${r.eventsDeviceCount} ` +
              'device(s) are still registered from before it was hidden. They will receive this push.'
      });
    }

    // TSIC-Teams is a separate audience with a separate device pool and a separate
    // Firebase project. This screen sends only to the TSIC-Events pool.
    if (r.teamsEnabled) {
      list.push({
        level: r.eventsEnabled ? 'warning' : 'danger',
        icon: 'bi-people-fill',
        text: `TSIC Teams is enabled for this event (${r.teamsDeviceCount} device(s) subscribed ` +
              'to a team), but this screen broadcasts to the TSIC-Events pool only. ' +
              'TSIC-Teams users will not receive it.'
      });
    }

    if (r.teamsEnabled && !r.teamsSenderConfigured) {
      list.push({
        level: 'warning',
        icon: 'bi-key-fill',
        text: 'No Firebase sender is configured for TSIC-Teams, so TSIC-Teams devices cannot ' +
              'be reached from this server at all.'
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
