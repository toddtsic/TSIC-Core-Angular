import { Component, ChangeDetectionStrategy, input, output, signal, effect, HostListener, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { RegistrationDetailDto, AccountingRecordDto } from '@core/api';
import { RegistrationSearchService } from '../services/registration-search.service';
import { ToastService } from '@shared-ui/toast.service';

type TabType = 'details' | 'accounting' | 'email';

interface FieldMetadata {
  key: string;
  label: string;
  type: 'text' | 'select' | 'date' | 'checkbox' | 'textarea';
  options?: string[];
  required?: boolean;
}

@Component({
  selector: 'app-registration-detail-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './registration-detail-panel.component.html',
  styleUrl: './registration-detail-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RegistrationDetailPanelComponent {
  detail = input<RegistrationDetailDto | null>(null);
  isOpen = input<boolean>(false);

  closed = output<void>();
  saved = output<void>();
  refundRequested = output<AccountingRecordDto>();

  private searchService = inject(RegistrationSearchService);
  private toast = inject(ToastService);

  activeTab = signal<TabType>('details');
  isSaving = signal<boolean>(false);
  profileValues = signal<Record<string, any>>({});
  metadataFields = signal<FieldMetadata[]>([]);
  emailSubject = signal<string>('');
  emailBody = signal<string>('');

  constructor() {
    effect(() => {
      const currentDetail = this.detail();
      if (currentDetail) {
        this.profileValues.set({ ...currentDetail.profileValues });
        this.parseMetadata(currentDetail.profileMetadataJson);
        this.emailSubject.set('');
        this.emailBody.set('');
      }
    });
  }

  @HostListener('document:keydown.escape')
  onEscapeKey(): void {
    if (this.isOpen()) { this.close(); }
  }

  close(): void { this.closed.emit(); }

  setActiveTab(tab: TabType): void { this.activeTab.set(tab); }

  parseMetadata(metadataJson: string | null | undefined): void {
    if (!metadataJson) { this.metadataFields.set([]); return; }
    try {
      const metadata = JSON.parse(metadataJson);
      const fields: FieldMetadata[] = [];
      if (Array.isArray(metadata)) {
        metadata.forEach((field: any) => {
          fields.push({
            key: field.key || field.name || '',
            label: field.label || field.displayName || field.key || '',
            type: this.mapFieldType(field.type || 'text'),
            options: field.options || field.choices || undefined,
            required: field.required || false
          });
        });
      } else if (typeof metadata === 'object') {
        Object.entries(metadata).forEach(([key, value]: [string, any]) => {
          fields.push({
            key, label: value.label || key,
            type: this.mapFieldType(value.type || 'text'),
            options: value.options || value.choices || undefined,
            required: value.required || false
          });
        });
      }
      this.metadataFields.set(fields);
    } catch (error) {
      console.error('Failed to parse profile metadata:', error);
      this.metadataFields.set([]);
    }
  }

  private mapFieldType(type: string): FieldMetadata['type'] {
    const typeMap: Record<string, FieldMetadata['type']> = {
      'text': 'text', 'string': 'text', 'select': 'select', 'dropdown': 'select',
      'date': 'date', 'datetime': 'date', 'checkbox': 'checkbox', 'boolean': 'checkbox',
      'textarea': 'textarea', 'multiline': 'textarea'
    };
    return typeMap[type.toLowerCase()] || 'text';
  }

  saveProfile(): void {
    const currentDetail = this.detail();
    if (!currentDetail) return;
    this.isSaving.set(true);
    this.searchService.updateProfile(currentDetail.registrationId, {
      registrationId: currentDetail.registrationId,
      profileValues: this.profileValues()
    }).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.toast.show('Profile updated successfully', 'success', 3000);
        this.saved.emit();
      },
      error: (err) => {
        this.isSaving.set(false);
        this.toast.show('Failed to update profile: ' + (err?.error?.message || 'Unknown error'), 'danger', 4000);
      }
    });
  }

  onRefundClick(record: AccountingRecordDto): void { this.refundRequested.emit(record); }

  insertEmailToken(token: string): void { this.emailBody.set(this.emailBody() + token); }

  previewEmail(): void {
    const currentDetail = this.detail();
    if (!currentDetail || !this.emailSubject() || !this.emailBody()) return;
    this.searchService.previewEmail({
      registrationIds: [currentDetail.registrationId],
      subject: this.emailSubject(),
      bodyTemplate: this.emailBody()
    }).subscribe({
      next: (response) => {
        if (response.previews?.length > 0) {
          this.toast.show('Preview generated - check console', 'success', 3000);
          console.log('Email preview:', response.previews[0]);
        }
      },
      error: (err) => {
        this.toast.show('Preview failed: ' + (err?.error?.message || 'Unknown error'), 'danger', 4000);
      }
    });
  }

  sendEmail(): void {
    const currentDetail = this.detail();
    if (!currentDetail || !this.emailSubject() || !this.emailBody()) {
      this.toast.show('Please provide both subject and body', 'warning');
      return;
    }
    this.searchService.sendBatchEmail({
      registrationIds: [currentDetail.registrationId],
      subject: this.emailSubject(),
      bodyTemplate: this.emailBody()
    }).subscribe({
      next: () => {
        this.toast.show('Email sent successfully', 'success', 3000);
        this.emailSubject.set('');
        this.emailBody.set('');
      },
      error: (err) => {
        this.toast.show('Failed to send email: ' + (err?.error?.message || 'Unknown error'), 'danger', 4000);
      }
    });
  }

  getFinancialSummary() {
    const d = this.detail();
    return { fees: d?.feeTotal || 0, paid: d?.paidTotal || 0, owed: d?.owedTotal || 0 };
  }
}
