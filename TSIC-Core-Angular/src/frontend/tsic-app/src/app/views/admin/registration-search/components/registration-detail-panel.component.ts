import { Component, ChangeDetectionStrategy, input, output, signal, effect, HostListener, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { RegistrationDetailDto, AccountingRecordDto, FamilyContactDto, UserDemographicsDto, JobOptionDto } from '@core/api';
import { RegistrationSearchService } from '../services/registration-search.service';
import { ToastService } from '@shared-ui/toast.service';

type TabType = 'details' | 'accounting' | 'email';

interface FieldMetadata {
  key: string;
  label: string;
  type: 'text' | 'select' | 'date' | 'checkbox' | 'textarea' | 'number';
  options?: string[];
  required?: boolean;
  order?: number;
}

/** Roles that use the player profile metadata form + family contact section */
const PLAYER_ROLES = new Set(['player', 'goalie', 'goalkeeper', 'athlete']);

/** Fields that always appear in the player profile section (extend this list as needed) */
const ALWAYS_INCLUDE_FIELDS: FieldMetadata[] = [
  { key: 'UniformNo', label: 'Uniform Number', type: 'text' }
];

/** Waiver field detection — mirrors backend ProfileMetadataService.IsWaiverField */
function isWaiverField(key: string, label: string, inputType: string): boolean {
  const lname = key.toLowerCase();
  const ldisp = label.toLowerCase();
  const isCheckbox = inputType.toLowerCase().includes('checkbox');
  const looksLikeWaiver = isCheckbox && (
    ldisp.startsWith('i agree') ||
    ldisp.includes('waiver') || ldisp.includes('release') ||
    (ldisp.includes('code') && ldisp.includes('conduct')) ||
    ldisp.includes('refund') || (ldisp.includes('terms') && ldisp.includes('conditions'))
  );
  return looksLikeWaiver || lname.includes('waiver') || lname.includes('codeofconduct') || lname.includes('refund');
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

  // Tab state
  activeTab = signal<TabType>('details');

  // Profile fields (metadata-driven for players, fixed for non-players)
  profileValues = signal<Record<string, any>>({});
  metadataFields = signal<FieldMetadata[]>([]);
  isSaving = signal<boolean>(false);

  // Family contact (player roles only)
  familyContact = signal<FamilyContactDto>({});
  isSavingFamily = signal<boolean>(false);
  hasFamilyLink = signal<boolean>(false);

  // User demographics (all roles)
  demographics = signal<UserDemographicsDto>({});
  isSavingDemographics = signal<boolean>(false);

  // Role detection
  isPlayerRole = signal<boolean>(false);

  // Email
  emailSubject = signal<string>('');
  emailBody = signal<string>('');

  // Change Job modal
  showChangeJobModal = signal<boolean>(false);
  changeJobOptions = signal<JobOptionDto[]>([]);
  selectedNewJobId = signal<string>('');
  isChangingJob = signal<boolean>(false);
  isLoadingJobOptions = signal<boolean>(false);

  // Delete Registration
  showDeleteConfirm = signal<boolean>(false);
  isDeleting = signal<boolean>(false);

  constructor() {
    effect(() => {
      const d = this.detail();
      if (d) {
        // Role detection: only check role name — metadata is per-job, not per-role
        const role = d.roleName?.toLowerCase().trim() ?? '';
        this.isPlayerRole.set(PLAYER_ROLES.has(role));

        // Profile values
        this.profileValues.set({ ...d.profileValues });
        this.parseMetadata(d.profileMetadataJson);

        // Family contact
        this.hasFamilyLink.set(!!d.familyContact);
        this.familyContact.set(d.familyContact ? { ...d.familyContact } : {});

        // Demographics — normalize dateOfBirth to yyyy-MM-dd for input[type=date]
        if (d.userDemographics) {
          const demo = { ...d.userDemographics };
          if (demo.dateOfBirth) demo.dateOfBirth = demo.dateOfBirth.substring(0, 10);
          this.demographics.set(demo);
        } else {
          this.demographics.set({});
        }

        // Reset email
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

  // ── Metadata Parsing ──

  parseMetadata(metadataJson: string | null | undefined): void {
    this.metadataFields.set(this.parseMetadataFields(metadataJson));
  }

  private parseMetadataFields(metadataJson: string | null | undefined): FieldMetadata[] {
    if (!metadataJson) return [];
    try {
      const raw = JSON.parse(metadataJson);
      const fields: FieldMetadata[] = [];

      // Handle {fields: [...]} wrapper format (ProfileMetadata class)
      const items = Array.isArray(raw) ? raw : (raw.fields && Array.isArray(raw.fields) ? raw.fields : null);

      if (items) {
        for (const f of items) {
          // Skip hidden/computed fields
          if (f.visibility === 'hidden' || f.computed) continue;

          const key = f.dbColumn || f.key || f.name || '';
          const label = f.displayName || f.label || f.key || key;
          const inputType = (f.inputType || f.type || 'TEXT').toUpperCase();

          // Skip waiver fields (always signed, not useful to display)
          if (isWaiverField(key, label, inputType)) continue;

          // Extract options
          let options: string[] | undefined;
          if (f.options && Array.isArray(f.options)) {
            options = f.options.map((o: any) => typeof o === 'string' ? o : (o.value || o.label || ''));
          } else if (f.choices && Array.isArray(f.choices)) {
            options = f.choices;
          }

          // If metadata says SELECT but provides no options, fall back to text input
          let mappedType = this.mapFieldType(inputType);
          if (mappedType === 'select' && (!options || options.length === 0)) mappedType = 'text';

          fields.push({
            key,
            label,
            type: mappedType,
            options,
            required: f.validation?.required || f.required || false,
            order: typeof f.order === 'number' ? f.order : 9999
          });
        }
      } else if (typeof raw === 'object') {
        // Legacy object format: { fieldName: { label, type, options } }
        for (const [key, value] of Object.entries(raw) as [string, any][]) {
          const label = value.label || key;
          const inputType = value.type || 'text';
          if (isWaiverField(key, label, inputType)) continue;

          const legacyOptions = value.options || value.choices || undefined;
          let legacyType = this.mapFieldType(inputType);
          if (legacyType === 'select' && (!legacyOptions || legacyOptions.length === 0)) legacyType = 'text';

          fields.push({
            key,
            label,
            type: legacyType,
            options: legacyOptions,
            required: value.required || false
          });
        }
      }

      // Sort by metadata order so related fields (e.g. USLax# + expiry) stay together
      fields.sort((a, b) => (a.order ?? 9999) - (b.order ?? 9999));

      // Merge always-include fields that aren't already present from metadata
      const existingKeys = new Set(fields.map(f => f.key.toLowerCase()));
      for (const alwaysField of ALWAYS_INCLUDE_FIELDS) {
        if (!existingKeys.has(alwaysField.key.toLowerCase())) {
          fields.unshift(alwaysField);
        }
      }

      return fields;
    } catch (error) {
      console.error('Failed to parse profile metadata:', error);
      return [];
    }
  }

  private mapFieldType(type: string): FieldMetadata['type'] {
    const t = type.toLowerCase();
    const typeMap: Record<string, FieldMetadata['type']> = {
      'text': 'text', 'string': 'text', 'email': 'text', 'tel': 'text', 'phone': 'text', 'file': 'text',
      'select': 'select', 'dropdown': 'select', 'multiselect': 'select',
      'date': 'date', 'datetime': 'date',
      'checkbox': 'checkbox', 'boolean': 'checkbox',
      'textarea': 'textarea', 'multiline': 'textarea',
      'number': 'number', 'int': 'number'
    };
    return typeMap[t] || 'text';
  }

  // ── Save Profile (Registration entity columns) ──

  saveProfile(): void {
    const d = this.detail();
    if (!d) return;
    this.isSaving.set(true);
    this.searchService.updateProfile(d.registrationId, {
      registrationId: d.registrationId,
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

  // ── Save Family Contact ──

  saveFamilyContact(): void {
    const d = this.detail();
    if (!d) return;
    this.isSavingFamily.set(true);
    this.searchService.updateFamilyContact(d.registrationId, {
      registrationId: d.registrationId,
      familyContact: this.familyContact()
    }).subscribe({
      next: () => {
        this.isSavingFamily.set(false);
        this.toast.show('Family contact updated successfully', 'success', 3000);
        this.saved.emit();
      },
      error: (err) => {
        this.isSavingFamily.set(false);
        this.toast.show('Failed to update family contact: ' + (err?.error?.message || 'Unknown error'), 'danger', 4000);
      }
    });
  }

  // ── Save Demographics ──

  saveDemographics(): void {
    const d = this.detail();
    if (!d) return;
    this.isSavingDemographics.set(true);
    this.searchService.updateDemographics(d.registrationId, {
      registrationId: d.registrationId,
      demographics: this.demographics()
    }).subscribe({
      next: () => {
        this.isSavingDemographics.set(false);
        this.toast.show('Demographics updated successfully', 'success', 3000);
        this.saved.emit();
      },
      error: (err) => {
        this.isSavingDemographics.set(false);
        this.toast.show('Failed to update demographics: ' + (err?.error?.message || 'Unknown error'), 'danger', 4000);
      }
    });
  }

  // ── Demographics helpers ──

  updateDemographicsField(field: keyof UserDemographicsDto, value: string | null): void {
    this.demographics.set({ ...this.demographics(), [field]: value || null });
  }

  // ── Family contact helpers ──

  updateFamilyField(field: keyof FamilyContactDto, value: string | null): void {
    this.familyContact.set({ ...this.familyContact(), [field]: value || null });
  }

  // ── Accounting ──

  onRefundClick(record: AccountingRecordDto): void { this.refundRequested.emit(record); }

  getFinancialSummary() {
    const d = this.detail();
    return { fees: d?.feeTotal || 0, paid: d?.paidTotal || 0, owed: d?.owedTotal || 0 };
  }

  // ── Email ──

  insertEmailToken(token: string): void { this.emailBody.set(this.emailBody() + token); }

  sendEmail(): void {
    const d = this.detail();
    if (!d || !this.emailSubject() || !this.emailBody()) {
      this.toast.show('Please provide both subject and body', 'warning');
      return;
    }
    this.searchService.sendBatchEmail({
      registrationIds: [d.registrationId],
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

  // ── Change Job ──

  openChangeJobModal(): void {
    this.isLoadingJobOptions.set(true);
    this.selectedNewJobId.set('');
    this.searchService.getChangeJobOptions().subscribe({
      next: (options) => {
        this.changeJobOptions.set(options);
        this.isLoadingJobOptions.set(false);
        this.showChangeJobModal.set(true);
      },
      error: (err) => {
        this.isLoadingJobOptions.set(false);
        this.toast.show('Failed to load job options: ' + (err?.error?.message || 'Unknown error'), 'danger', 4000);
      }
    });
  }

  cancelChangeJob(): void {
    this.showChangeJobModal.set(false);
    this.selectedNewJobId.set('');
  }

  submitChangeJob(): void {
    const d = this.detail();
    const newJobId = this.selectedNewJobId();
    if (!d || !newJobId) return;

    this.isChangingJob.set(true);
    this.searchService.changeJob(d.registrationId, { newJobId }).subscribe({
      next: (result) => {
        this.isChangingJob.set(false);
        this.showChangeJobModal.set(false);
        this.selectedNewJobId.set('');
        this.toast.show(result.message || 'Job changed successfully', 'success', 4000);
        this.saved.emit();
      },
      error: (err) => {
        this.isChangingJob.set(false);
        this.toast.show('Failed to change job: ' + (err?.error?.message || 'Unknown error'), 'danger', 4000);
      }
    });
  }

  // ── Delete Registration ──

  confirmDelete(): void { this.showDeleteConfirm.set(true); }

  cancelDelete(): void { this.showDeleteConfirm.set(false); }

  executeDelete(): void {
    const d = this.detail();
    if (!d) return;

    this.isDeleting.set(true);
    this.searchService.deleteRegistration(d.registrationId).subscribe({
      next: (result) => {
        this.isDeleting.set(false);
        this.showDeleteConfirm.set(false);
        this.toast.show(result.message || 'Registration deleted successfully', 'success', 4000);
        this.closed.emit();
        this.saved.emit();
      },
      error: (err) => {
        this.isDeleting.set(false);
        this.toast.show(err?.error?.message || 'Failed to delete registration', 'danger', 5000);
      }
    });
  }
}
