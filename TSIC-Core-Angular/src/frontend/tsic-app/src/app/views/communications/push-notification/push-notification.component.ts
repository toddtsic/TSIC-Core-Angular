import { Component, ChangeDetectionStrategy, inject, signal, computed, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { GridAllModule, SortSettingsModel } from '@syncfusion/ej2-angular-grids';
import { PushNotificationService } from './services/push-notification.service';
import type { PushNotificationHistoryDto } from '../../../core/api';

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
  deviceCount = signal(0);
  pushText = signal('');
  history = signal<PushNotificationHistoryDto[]>([]);

  // Computed
  canSend = computed(() => this.pushText().trim().length > 0 && !this.isSending());

  // Grid settings
  sortSettings: SortSettingsModel = { columns: [{ field: 'sentWhen', direction: 'Descending' }] };

  ngOnInit(): void {
    this.loadDeviceCount();
    this.loadHistory();
  }

  private loadDeviceCount(): void {
    this.pushService.getDeviceCount().subscribe({
      next: (data) => this.deviceCount.set(data.deviceCount),
      error: () => { /* Device count is non-critical — silently ignore */ }
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
        this.loadDeviceCount();
        this.loadHistory();
      },
      error: (err) => {
        this.errorMessage.set(err.error?.message || 'Failed to send push notification.');
        this.isSending.set(false);
      }
    });
  }
}
