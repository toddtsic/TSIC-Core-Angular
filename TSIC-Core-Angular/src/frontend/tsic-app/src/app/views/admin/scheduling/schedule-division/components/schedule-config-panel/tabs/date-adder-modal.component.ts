import {
  ChangeDetectionStrategy, Component, computed, input, output, signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';

interface DateEntry {
  isoDate: string;
  label: string;
}

@Component({
  selector: 'app-date-adder-modal',
  standalone: true,
  imports: [CommonModule, FormsModule, TsicDialogComponent],
  templateUrl: './date-adder-modal.component.html',
  styleUrl: './date-adder-modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DateAdderModalComponent {
  /** Existing dates already in the matrix (for duplicate prevention). */
  readonly existingDates = input<string[]>([]);

  /** Emits the list of newly added dates when user clicks Done. */
  readonly datesAdded = output<string[]>();
  readonly cancelled = output<void>();

  /** Dates added during this modal session. */
  readonly pendingDates = signal<string[]>([]);

  /** Currently selected date (for +1/+7 chaining). */
  readonly selectedDate = signal<string | null>(null);

  /** Manual date picker value. */
  newDate = '';

  /** All dates shown in the list: existing + pending. */
  readonly allDates = computed((): DateEntry[] => {
    const existing = this.existingDates().map(iso => ({
      isoDate: iso,
      label: this.formatLabel(iso),
    }));
    const pending = this.pendingDates().map(iso => ({
      isoDate: iso,
      label: this.formatLabel(iso),
    }));
    return [...existing, ...pending].sort((a, b) => a.isoDate.localeCompare(b.isoDate));
  });

  readonly pendingCount = computed(() => this.pendingDates().length);

  // ── Actions ──

  addManualDate(): void {
    if (!this.newDate) return;
    if (this.isDuplicate(this.newDate)) {
      this.newDate = '';
      return;
    }
    this.pendingDates.set([...this.pendingDates(), this.newDate]);
    this.selectedDate.set(this.newDate);
    this.newDate = '';
  }

  addRelative(days: number): void {
    const base = this.selectedDate();
    if (!base) return;
    const d = new Date(base + 'T12:00:00');
    d.setDate(d.getDate() + days);
    const iso = d.toISOString().substring(0, 10);
    if (this.isDuplicate(iso)) return;
    this.pendingDates.set([...this.pendingDates(), iso]);
    this.selectedDate.set(iso);
  }

  selectDate(isoDate: string): void {
    this.selectedDate.set(isoDate);
  }

  removePending(isoDate: string): void {
    this.pendingDates.set(this.pendingDates().filter(d => d !== isoDate));
    if (this.selectedDate() === isoDate) {
      this.selectedDate.set(null);
    }
  }

  isPending(isoDate: string): boolean {
    return this.pendingDates().includes(isoDate);
  }

  onDone(): void {
    this.datesAdded.emit(this.pendingDates());
  }

  // ── Helpers ──

  private isDuplicate(iso: string): boolean {
    return this.existingDates().includes(iso) || this.pendingDates().includes(iso);
  }

  private formatLabel(isoDate: string): string {
    const d = new Date(isoDate + 'T12:00:00');
    const dow = d.toLocaleDateString('en-US', { weekday: 'short' });
    const parts = isoDate.split('-');
    return `${dow} ${parts[1]}/${parts[2]}/${parts[0]}`;
  }
}
