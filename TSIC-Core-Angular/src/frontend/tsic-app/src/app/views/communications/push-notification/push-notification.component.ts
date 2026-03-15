import { Component, ChangeDetectionStrategy, inject, signal, computed, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PushNotificationService } from './services/push-notification.service';
import type { PushNotificationHistoryDto } from '../../../core/api';

@Component({
  selector: 'app-push-notification',
  standalone: true,
  imports: [FormsModule],
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

  // Sort state
  sortColumn = signal<string>('sentWhen');
  sortDirection = signal<'asc' | 'desc'>('desc');

  sortedHistory = computed(() => {
    const list = [...this.history()];
    const col = this.sortColumn();
    const dir = this.sortDirection();

    list.sort((a, b) => {
      let valA: string | number;
      let valB: string | number;

      switch (col) {
        case 'sentBy': valA = a.sentBy; valB = b.sentBy; break;
        case 'sentWhen': valA = a.sentWhen; valB = b.sentWhen; break;
        case 'deviceCount': valA = a.deviceCount; valB = b.deviceCount; break;
        case 'pushText': valA = a.pushText; valB = b.pushText; break;
        default: valA = a.sentWhen; valB = b.sentWhen;
      }

      if (valA < valB) return dir === 'asc' ? -1 : 1;
      if (valA > valB) return dir === 'asc' ? 1 : -1;
      return 0;
    });

    return list;
  });

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

  toggleSort(column: string): void {
    if (this.sortColumn() === column) {
      this.sortDirection.set(this.sortDirection() === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortColumn.set(column);
      this.sortDirection.set('asc');
    }
  }

  sortIcon(column: string): string {
    if (this.sortColumn() !== column) return '';
    return this.sortDirection() === 'asc' ? ' \u25B2' : ' \u25BC';
  }

  formatDate(iso: string): string {
    const d = new Date(iso);
    return d.toLocaleString('en-US', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false
    });
  }
}
