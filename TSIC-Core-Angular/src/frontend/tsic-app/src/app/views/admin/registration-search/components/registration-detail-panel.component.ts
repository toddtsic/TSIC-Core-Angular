import { Component, ChangeDetectionStrategy, input, output, signal, effect, computed, HostListener, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import type { RegistrationDetailDto, AccountingRecordDto, FamilyContactDto, UserDemographicsDto, JobOptionDto, SubscriptionDetailDto } from '@core/api';
import { RegistrationSearchService } from '../services/registration-search.service';
import { ToastService } from '@shared-ui/toast.service';
import { AddPaymentModalComponent } from './add-payment-modal.component';

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

/** Fields that appear as editable inputs in the contact zone for players */
const ALWAYS_INCLUDE_FIELDS: FieldMetadata[] = [
  { key: 'UniformNo', label: 'Uniform #', type: 'text' }
];

/** Keys of always-include fields (lowercase) for filtering from read-only display */
const ALWAYS_INCLUDE_KEYS = new Set(ALWAYS_INCLUDE_FIELDS.map(f => f.key.toLowerCase()));

/** Non-player profile field display labels */
const NON_PLAYER_FIELD_LABELS: Record<string, string> = {
  'ClubName': 'Club Name',
  'SpecialRequests': 'Special Requests',
  'SportYearsExp': 'Years of Experience',
  'SportAssnId': 'Sport Association #',
  'SportAssnIdexpDate': 'Assn # Expiration'
};

/** Formats a 10-digit phone string as xxx-xxx-xxxx. Mirrors backend StringExtensions.FormatPhone. */
function formatPhone(value: string | null | undefined): string | null {
  if (!value) return value ?? null;
  const digits = value.replace(/\D/g, '');
  if (digits.length === 10) return `${digits.slice(0, 3)}-${digits.slice(3, 6)}-${digits.slice(6)}`;
  return value;
}

/** Strips a phone string to digits only for storage. */
function stripPhoneToDigits(value: string | null | undefined): string | null {
  if (!value) return value ?? null;
  return value.replace(/\D/g, '') || null;
}

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
  imports: [CommonModule, FormsModule, AddPaymentModalComponent],
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

  /** Expose for template binding */
  readonly ALWAYS_INCLUDE_FIELDS = ALWAYS_INCLUDE_FIELDS;

  // Tab state
  activeTab = signal<TabType>('details');

  // Profile values (for read-only display + always-include editable fields)
  profileValues = signal<Record<string, any>>({});
  metadataFields = signal<FieldMetadata[]>([]);

  // Contact zone editable state
  isSavingContact = signal<boolean>(false);

  // Family contact (player roles only — editable: email + cellphone; read-only: names)
  familyContact = signal<FamilyContactDto>({});
  hasFamilyLink = signal<boolean>(false);

  // User demographics (editable: email, cellphone)
  demographics = signal<UserDemographicsDto>({});

  // Role detection
  isPlayerRole = signal<boolean>(false);

  // Read-only metadata fields (excludes always-include keys like UniformNo)
  readOnlyMetadataFields = computed(() => {
    return this.metadataFields().filter(f => !ALWAYS_INCLUDE_KEYS.has(f.key.toLowerCase()));
  });

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
        const role = d.roleName?.toLowerCase().trim() ?? '';
        this.isPlayerRole.set(PLAYER_ROLES.has(role));

        this.profileValues.set({ ...d.profileValues });
        this.parseMetadata(d.profileMetadataJson);

        this.hasFamilyLink.set(!!d.familyContact);
        if (d.familyContact) {
          this.familyContact.set({
            ...d.familyContact,
            momCellphone: formatPhone(d.familyContact.momCellphone),
            dadCellphone: formatPhone(d.familyContact.dadCellphone)
          });
        } else {
          this.familyContact.set({});
        }

        if (d.userDemographics) {
          const demo = { ...d.userDemographics };
          if (demo.dateOfBirth) demo.dateOfBirth = demo.dateOfBirth.substring(0, 10);
          demo.cellphone = formatPhone(demo.cellphone);
          this.demographics.set(demo);
        } else {
          this.demographics.set({});
        }

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

  setActiveTab(tab: TabType): void {
    this.activeTab.set(tab);
    if (tab === 'accounting' && this.detail()?.hasSubscription && !this.subscription()) {
      this.loadSubscription();
    }
  }

  // ── Template helpers ──

  hasValue(val: any): boolean {
    return val !== null && val !== undefined && val !== '';
  }

  formatAddress(): string | null {
    const d = this.demographics();
    const parts = [d.streetAddress, d.city, d.state, d.postalCode].filter(p => p);
    return parts.length > 0 ? parts.join(', ') : null;
  }

  formatCheckboxValue(val: any): string {
    if (val === true || val === 'true' || val === 'True' || val === '1') return 'Yes';
    if (val === false || val === 'false' || val === 'False' || val === '0') return 'No';
    return String(val);
  }

  familyName(prefix: 'mom' | 'dad'): string | null {
    const fc = this.familyContact();
    const first = prefix === 'mom' ? fc.momFirstName : fc.dadFirstName;
    const last = prefix === 'mom' ? fc.momLastName : fc.dadLastName;
    const parts = [first, last].filter(p => p);
    return parts.length > 0 ? parts.join(' ') : null;
  }

  nonPlayerFields(): { label: string; value: string }[] {
    const pv = this.profileValues();
    const result: { label: string; value: string }[] = [];
    for (const [key, label] of Object.entries(NON_PLAYER_FIELD_LABELS)) {
      const val = pv[key];
      if (this.hasValue(val)) {
        result.push({ label, value: String(val) });
      }
    }
    return result;
  }

  // ── Contact zone field helpers ──

  updateDemographicsField(field: keyof UserDemographicsDto, value: string | null): void {
    this.demographics.set({ ...this.demographics(), [field]: value || null });
  }

  updateFamilyField(field: keyof FamilyContactDto, value: string | null): void {
    this.familyContact.set({ ...this.familyContact(), [field]: value || null });
  }

  updateProfileValue(key: string, value: string | null): void {
    const current = this.profileValues();
    this.profileValues.set({ ...current, [key]: value || null });
  }

  // ── Metadata Parsing ──

  parseMetadata(metadataJson: string | null | undefined): void {
    this.metadataFields.set(this.parseMetadataFields(metadataJson));
  }

  private parseMetadataFields(metadataJson: string | null | undefined): FieldMetadata[] {
    if (!metadataJson) return [];
    try {
      const raw = JSON.parse(metadataJson);
      const fields: FieldMetadata[] = [];

      const items = Array.isArray(raw) ? raw : (raw.fields && Array.isArray(raw.fields) ? raw.fields : null);

      if (items) {
        for (const f of items) {
          if (f.visibility === 'hidden' || f.computed) continue;

          const key = f.dbColumn || f.key || f.name || '';
          const label = f.displayName || f.label || f.key || key;
          const inputType = (f.inputType || f.type || 'TEXT').toUpperCase();

          if (isWaiverField(key, label, inputType)) continue;

          let options: string[] | undefined;
          if (f.options && Array.isArray(f.options)) {
            options = f.options.map((o: any) => typeof o === 'string' ? o : (o.value || o.label || ''));
          } else if (f.choices && Array.isArray(f.choices)) {
            options = f.choices;
          }

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

      fields.sort((a, b) => (a.order ?? 9999) - (b.order ?? 9999));

      // Ensure always-include fields exist (for the editable contact zone)
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

  // ── Save Contact Info (unified save for all editable fields) ──

  saveContactInfo(): void {
    const d = this.detail();
    if (!d) return;

    this.isSavingContact.set(true);
    const calls: Record<string, any> = {};

    // Always save demographics (email + cellphone) — strip phone to digits for storage
    const demoToSave = { ...this.demographics(), cellphone: stripPhoneToDigits(this.demographics().cellphone) };
    calls['demographics'] = this.searchService.updateDemographics(d.registrationId, {
      registrationId: d.registrationId,
      demographics: demoToSave
    });

    // Save family contact if player with family link — strip phones to digits for storage
    if (this.isPlayerRole() && this.hasFamilyLink()) {
      const familyToSave = {
        ...this.familyContact(),
        momCellphone: stripPhoneToDigits(this.familyContact().momCellphone),
        dadCellphone: stripPhoneToDigits(this.familyContact().dadCellphone)
      };
      calls['family'] = this.searchService.updateFamilyContact(d.registrationId, {
        registrationId: d.registrationId,
        familyContact: familyToSave
      });
    }

    // Save profile if player (always-include fields like UniformNo)
    if (this.isPlayerRole()) {
      calls['profile'] = this.searchService.updateProfile(d.registrationId, {
        registrationId: d.registrationId,
        profileValues: this.profileValues()
      });
    }

    forkJoin(calls).subscribe({
      next: () => {
        this.isSavingContact.set(false);
        this.toast.show('Contact info saved', 'success', 3000);
        this.saved.emit();
      },
      error: (err) => {
        this.isSavingContact.set(false);
        this.toast.show('Failed to save: ' + (err?.error?.message || 'Unknown error'), 'danger', 4000);
      }
    });
  }

  // ── Accounting ──

  showPaymentModal = signal<boolean>(false);
  editingAId = signal<number | null>(null);
  editComment = signal<string>('');
  editCheckNo = signal<string>('');
  isSavingEdit = signal<boolean>(false);

  onRefundClick(record: AccountingRecordDto): void { this.refundRequested.emit(record); }

  openPaymentModal(): void { this.showPaymentModal.set(true); }

  onPaymentRecorded(): void {
    this.showPaymentModal.set(false);
    this.saved.emit();
  }

  startEditRecord(record: AccountingRecordDto): void {
    this.editingAId.set(record.aId);
    this.editComment.set(record.comment || '');
    this.editCheckNo.set(record.checkNo || '');
  }

  cancelEditRecord(): void {
    this.editingAId.set(null);
  }

  saveEditRecord(): void {
    const aId = this.editingAId();
    if (aId == null) return;

    this.isSavingEdit.set(true);
    this.searchService.editAccountingRecord(aId, {
      comment: this.editComment() || null,
      checkNo: this.editCheckNo() || null
    }).subscribe({
      next: () => {
        this.isSavingEdit.set(false);
        this.editingAId.set(null);
        this.toast.show('Record updated', 'success', 3000);
        this.saved.emit();
      },
      error: (err) => {
        this.isSavingEdit.set(false);
        this.toast.show('Failed to update: ' + (err?.error?.message || 'Unknown error'), 'danger', 4000);
      }
    });
  }

  /** Check/Correction/Cash records are editable (not CC records). */
  isEditable(record: AccountingRecordDto): boolean {
    const method = (record.paymentMethod || '').toLowerCase();
    return method.includes('check') || method.includes('correction') || method.includes('cash');
  }

  getFinancialSummary() {
    const d = this.detail();
    return { fees: d?.feeTotal || 0, paid: d?.paidTotal || 0, owed: d?.owedTotal || 0 };
  }

  // ── Subscription ──

  subscription = signal<SubscriptionDetailDto | null>(null);
  isLoadingSubscription = signal<boolean>(false);
  isCancellingSubscription = signal<boolean>(false);
  showCancelSubConfirm = signal<boolean>(false);

  loadSubscription(): void {
    const d = this.detail();
    if (!d || !d.hasSubscription) return;

    this.isLoadingSubscription.set(true);
    this.searchService.getSubscription(d.registrationId).subscribe({
      next: (sub) => {
        this.subscription.set(sub);
        this.isLoadingSubscription.set(false);
      },
      error: () => {
        this.subscription.set(null);
        this.isLoadingSubscription.set(false);
      }
    });
  }

  confirmCancelSubscription(): void {
    this.showCancelSubConfirm.set(true);
  }

  dismissCancelSubscription(): void {
    this.showCancelSubConfirm.set(false);
  }

  cancelSubscription(): void {
    const d = this.detail();
    if (!d) return;

    this.showCancelSubConfirm.set(false);
    this.isCancellingSubscription.set(true);
    this.searchService.cancelSubscription(d.registrationId).subscribe({
      next: () => {
        this.isCancellingSubscription.set(false);
        this.toast.show('Subscription cancelled', 'success', 3000);
        this.loadSubscription();
        this.saved.emit();
      },
      error: (err) => {
        this.isCancellingSubscription.set(false);
        this.toast.show('Failed to cancel: ' + (err?.error?.message || 'Unknown error'), 'danger', 4000);
      }
    });
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
