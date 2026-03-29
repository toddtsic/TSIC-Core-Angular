import { Component, ChangeDetectionStrategy, input, output, signal, effect, computed, HostListener, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import type { RegistrationDetailDto, AccountingRecordDto, FamilyContactDto, UserDemographicsDto, JobOptionDto, SubscriptionDetailDto } from '@core/api';
import { RegistrationSearchService } from '../services/registration-search.service';
import { ToastService } from '@shared-ui/toast.service';
import { AccountingLedgerComponent, CcChargeEvent, CheckOrCorrectionEvent } from '@shared-ui/components/accounting-ledger/accounting-ledger.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { ClubRepPaymentComponent } from '@shared-ui/components/club-rep-payment/club-rep-payment.component';

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

/** Fields guaranteed to appear in the Player Profile card even if metadata doesn't include them */
const PROFILE_GUARANTEED_FIELDS: FieldMetadata[] = [
  { key: 'UniformNo', label: 'Uniform #', type: 'text' }
];

/** Fields excluded from the editable Player Profile card (handled elsewhere) */
const PROFILE_EXCLUDED_KEYS = new Set(['teamid']);

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
  imports: [CommonModule, FormsModule, AccountingLedgerComponent, ConfirmDialogComponent, ClubRepPaymentComponent],
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

  // Profile values (for read-only display + always-include editable fields)
  profileValues = signal<Record<string, any>>({});
  metadataFields = signal<FieldMetadata[]>([]);

  // Contact zone editable state
  isSavingContact = signal<boolean>(false);

  // Family contact (player roles only — editable: email + cellphone; read-only: names)
  familyContact = signal<FamilyContactDto>({});
  hasFamilyLink = signal<boolean>(false);

  // User demographics (player's own — editable: email, cellphone, dob, gender)
  demographics = signal<UserDemographicsDto>({});

  // Family account demographics (player roles only — email, cell, address)
  familyDemographics = signal<UserDemographicsDto>({});

  // Role detection
  isPlayerRole = signal<boolean>(false);
  isClubRep = computed(() => this.detail()?.isClubRep === true);

  // Editable profile fields (excludes team selection, reorders for lacrosse)
  editableProfileFields = computed(() => {
    const fields = this.metadataFields().filter(f => !PROFILE_EXCLUDED_KEYS.has(f.key.toLowerCase()));
    return this.reorderForSport(fields);
  });

  // Profile save state
  isSavingProfile = signal<boolean>(false);

  // Email
  emailSubject = signal<string>('');
  emailBody = signal<string>('');

  // AI compose
  aiPrompt = signal<string>('');
  isDraftingAi = signal<boolean>(false);

  // Change Job modal
  showChangeJobModal = signal<boolean>(false);
  changeJobOptions = signal<JobOptionDto[]>([]);
  selectedNewJobId = signal<string>('');
  isChangingJob = signal<boolean>(false);
  isLoadingJobOptions = signal<boolean>(false);

  // Active toggle
  isTogglingActive = signal<boolean>(false);

  // Delete Registration
  showDeleteConfirm = signal<boolean>(false);
  isDeleting = signal<boolean>(false);
  canDelete = computed(() => {
    const d = this.detail();
    if (!d) return false;
    return !d.accountingRecords || d.accountingRecords.length === 0;
  });

  constructor() {
    effect(() => {
      const d = this.detail();
      if (d) {
        const role = d.roleName?.toLowerCase().trim() ?? '';
        this.isPlayerRole.set(PLAYER_ROLES.has(role));

        this.profileValues.set({ ...d.profileValues });
        this.parseMetadata(d.profileMetadataJson, d.jsonOptions);
        this.normalizeSelectValues();

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

        if (d.familyAccountDemographics) {
          const fDemo = { ...d.familyAccountDemographics };
          fDemo.cellphone = formatPhone(fDemo.cellphone);
          this.familyDemographics.set(fDemo);
        } else {
          this.familyDemographics.set({});
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

  /** Returns true if a profile checkbox value represents "checked" */
  isChecked(val: any): boolean {
    return val === true || val === 'true' || val === 'True' || val === '1';
  }

  /** Sport-aware field label: renames SportAssnId for Lacrosse jobs */
  getFieldLabel(field: FieldMetadata): string {
    const sport = this.detail()?.sportName?.toLowerCase() ?? '';
    if (sport === 'lacrosse') {
      if (field.key.toLowerCase() === 'sportassnid') return 'USA Lacrosse Number';
      if (field.key.toLowerCase() === 'sportassnidexpdate') return 'USA Lacrosse # Expiration';
    }
    return field.label;
  }

  /** For lacrosse: move SportAssnId immediately before SportAssnIdexpDate */
  private reorderForSport(fields: FieldMetadata[]): FieldMetadata[] {
    const sport = this.detail()?.sportName?.toLowerCase() ?? '';
    if (sport !== 'lacrosse') return fields;

    const result = [...fields];
    const assnIdx = result.findIndex(f => f.key.toLowerCase() === 'sportassnid');
    const expIdx = result.findIndex(f => f.key.toLowerCase() === 'sportassnidexpdate');
    if (assnIdx >= 0 && expIdx >= 0 && assnIdx !== expIdx - 1) {
      const [assnField] = result.splice(assnIdx, 1);
      const newExpIdx = result.findIndex(f => f.key.toLowerCase() === 'sportassnidexpdate');
      result.splice(newExpIdx, 0, assnField);
    }
    return result;
  }

  /** Normalize stored profile values to match option casing (e.g. "adult m" → "Adult M") */
  private normalizeSelectValues(): void {
    const fields = this.metadataFields();
    const pv = { ...this.profileValues() };
    let changed = false;
    for (const field of fields) {
      if (field.type !== 'select' || !field.options?.length) continue;
      const stored = pv[field.key];
      if (!stored) continue;
      const match = field.options.find(o => o.toLowerCase() === stored.toLowerCase());
      if (match && match !== stored) {
        pv[field.key] = match;
        changed = true;
      }
    }
    if (changed) this.profileValues.set(pv);
  }

  /** Save profile fields independently from contact info */
  saveProfileInfo(): void {
    const d = this.detail();
    if (!d) return;

    this.isSavingProfile.set(true);
    this.searchService.updateProfile(d.registrationId, {
      registrationId: d.registrationId,
      profileValues: this.profileValues()
    }).subscribe({
      next: () => {
        this.isSavingProfile.set(false);
        this.toast.show('Player profile saved', 'success', 3000);
        this.saved.emit();
      },
      error: (err) => {
        this.isSavingProfile.set(false);
        this.toast.show('Failed to save: ' + (err?.error?.message || 'Unknown error'), 'danger', 4000);
      }
    });
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

  updateFamilyDemographicsField(field: keyof UserDemographicsDto, value: string | null): void {
    this.familyDemographics.set({ ...this.familyDemographics(), [field]: value || null });
  }

  updateFamilyField(field: keyof FamilyContactDto, value: string | null): void {
    this.familyContact.set({ ...this.familyContact(), [field]: value || null });
  }

  updateProfileValue(key: string, value: string | null): void {
    const current = this.profileValues();
    this.profileValues.set({ ...current, [key]: value || null });
  }

  // ── Metadata Parsing ──

  parseMetadata(metadataJson: string | null | undefined, jsonOptions?: string | null): void {
    this.metadataFields.set(this.parseMetadataFields(metadataJson, jsonOptions));
  }

  private parseMetadataFields(metadataJson: string | null | undefined, jsonOptions?: string | null): FieldMetadata[] {
    if (!metadataJson) return [];
    try {
      const raw = JSON.parse(metadataJson);
      const optionSets = this.parseOptionSets(jsonOptions);
      const fields: FieldMetadata[] = [];

      const items = Array.isArray(raw) ? raw : (raw.fields && Array.isArray(raw.fields) ? raw.fields : null);

      if (items) {
        for (const f of items) {
          if (f.visibility === 'hidden' || f.computed) continue;

          const key = f.dbColumn || f.key || f.name || '';
          const label = f.displayName || f.label || f.key || key;
          const inputType = (f.inputType || f.type || 'TEXT').toUpperCase();

          if (isWaiverField(key, label, inputType)) continue;

          const options = this.resolveFieldOptions(f, key, label, optionSets);

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

          const options = this.resolveFieldOptions(value, key, label, optionSets);
          let legacyType = this.mapFieldType(inputType);
          if (legacyType === 'select' && (!options || options.length === 0)) legacyType = 'text';

          fields.push({
            key,
            label,
            type: legacyType,
            options,
            required: value.required || false
          });
        }
      }

      fields.sort((a, b) => (a.order ?? 9999) - (b.order ?? 9999));

      // Ensure guaranteed profile fields exist even if metadata doesn't include them
      const existingKeys = new Set(fields.map(f => f.key.toLowerCase()));
      for (const guaranteed of PROFILE_GUARANTEED_FIELDS) {
        if (!existingKeys.has(guaranteed.key.toLowerCase())) {
          fields.unshift(guaranteed);
        }
      }

      return fields;
    } catch (error) {
      console.error('Failed to parse profile metadata:', error);
      return [];
    }
  }

  /** Parse Job.JsonOptions into a case-insensitive lookup of option set arrays */
  private parseOptionSets(jsonOptions: string | null | undefined): Record<string, any[]> | null {
    if (!jsonOptions) return null;
    try {
      const parsed = JSON.parse(jsonOptions);
      if (typeof parsed !== 'object' || parsed === null) return null;
      // Build case-insensitive lookup
      const sets: Record<string, any[]> = {};
      for (const [k, v] of Object.entries(parsed)) {
        if (Array.isArray(v)) sets[k.toLowerCase()] = v;
      }
      return sets;
    } catch {
      return null;
    }
  }

  /** Resolve options for a field: inline options → dataSource → fuzzy key match */
  private resolveFieldOptions(f: any, key: string, label: string, optionSets: Record<string, any[]> | null): string[] | undefined {
    // 1. Direct inline options
    if (f.options && Array.isArray(f.options) && f.options.length > 0) {
      return f.options.map((o: any) => typeof o === 'string' ? o : (o.value || o.Value || o.label || o.Text || ''));
    }
    if (f.choices && Array.isArray(f.choices) && f.choices.length > 0) {
      return f.choices;
    }
    if (!optionSets) return undefined;

    // 2. Explicit dataSource reference
    const dsKey = (f.dataSource || f.optionsSource || '').trim().toLowerCase();
    if (dsKey && optionSets[dsKey]) {
      return optionSets[dsKey].map((v: any) => String(v?.value ?? v?.Value ?? v?.Text ?? v));
    }

    // 3. Fuzzy match: field name as key
    const keyLower = key.toLowerCase();
    if (optionSets[keyLower]) {
      return optionSets[keyLower].map((v: any) => String(v?.value ?? v?.Value ?? v?.Text ?? v));
    }

    // 4. Fuzzy match: common naming patterns (List_X, ListSizes_X)
    const fuzzyKeys = [`list_${keyLower}`, `listsizes_${keyLower}`];
    for (const fk of fuzzyKeys) {
      if (optionSets[fk]) {
        return optionSets[fk].map((v: any) => String(v?.value ?? v?.Value ?? v?.Text ?? v));
      }
    }

    // 5. Label-based fuzzy match for sizing fields
    const lLabel = label.toLowerCase();
    for (const sizeWord of ['kilt', 'jersey', 'shorts', 'tshirt', 't-shirt', 'gloves', 'sweatshirt', 'sweatpants', 'reversible', 'shoes']) {
      if (lLabel.includes(sizeWord) || keyLower.includes(sizeWord)) {
        const match = Object.keys(optionSets).find(k => k.includes(sizeWord));
        if (match) return optionSets[match].map((v: any) => String(v?.value ?? v?.Value ?? v?.Text ?? v));
      }
    }

    return undefined;
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

    // Save player/registrant demographics — strip phone to digits for storage
    const demoToSave = { ...this.demographics(), cellphone: stripPhoneToDigits(this.demographics().cellphone) };
    calls['demographics'] = this.searchService.updateDemographics(d.registrationId, {
      registrationId: d.registrationId,
      demographics: demoToSave
    });

    // Save family account demographics if player with family link
    if (this.isPlayerRole() && this.hasFamilyLink()) {
      const familyDemoToSave = { ...this.familyDemographics(), cellphone: stripPhoneToDigits(this.familyDemographics().cellphone) };
      calls['familyDemographics'] = this.searchService.updateFamilyAccountDemographics(d.registrationId, {
        registrationId: d.registrationId,
        demographics: familyDemoToSave
      });
    }

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

  // ── Accounting (delegated to shared AccountingLedgerComponent) ──

  onRefundClick(record: AccountingRecordDto): void { this.refundRequested.emit(record); }

  onCcCharge(event: CcChargeEvent): void {
    const d = this.detail();
    if (!d) return;

    this.searchService.chargeCc(d.registrationId, {
      registrationId: d.registrationId,
      creditCard: event.creditCard,
      amount: event.amount
    }).subscribe({
      next: (response) => {
        if (response.success) {
          this.toast.show(`CC charge successful: $${event.amount.toFixed(2)}`, 'success', 3000);
          this.saved.emit();
        } else {
          this.toast.show(`CC charge failed: ${response.error || 'Unknown error'}`, 'danger', 5000);
        }
      },
      error: (err) => { this.toast.show(`CC charge failed: ${err.error?.message || 'Unknown error'}`, 'danger', 5000); }
    });
  }

  onCheckSubmitted(event: CheckOrCorrectionEvent): void {
    const d = this.detail();
    if (!d) return;

    this.searchService.recordPayment(d.registrationId, {
      registrationId: d.registrationId,
      amount: event.amount,
      paymentType: event.paymentType,
      checkNo: event.checkNo,
      comment: event.comment
    }).subscribe({
      next: (response) => {
        if (response.success) {
          this.toast.show(`${event.paymentType} recorded: $${event.amount.toFixed(2)}`, 'success', 3000);
          this.saved.emit();
        } else {
          this.toast.show(`Failed: ${response.error || 'Unknown error'}`, 'danger', 5000);
        }
      },
      error: (err) => { this.toast.show(`Failed: ${err.error?.message || 'Unknown error'}`, 'danger', 5000); }
    });
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

  draftWithAi(): void {
    const prompt = this.aiPrompt().trim();
    if (!prompt) { this.toast.show('Describe the email you want to send', 'warning'); return; }

    this.isDraftingAi.set(true);
    this.searchService.aiComposeEmail(prompt).subscribe({
      next: (response) => {
        this.emailSubject.set(response.subject);
        this.emailBody.set(response.body);
        this.isDraftingAi.set(false);
      },
      error: (err) => {
        this.isDraftingAi.set(false);
        this.toast.show(`AI draft failed: ${err.error?.message || 'Unknown error'}`, 'danger');
      }
    });
  }

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

  toggleActive(): void {
    const d = this.detail();
    if (!d) return;

    const newValue = !d.active;
    this.isTogglingActive.set(true);
    this.searchService.setActive(d.registrationId, newValue).subscribe({
      next: () => {
        (d as Record<string, unknown>)['active'] = newValue;
        this.isTogglingActive.set(false);
        this.toast.show(newValue ? 'Registration activated' : 'Registration deactivated', 'success', 3000);
        this.saved.emit();
      },
      error: (err) => {
        this.isTogglingActive.set(false);
        this.toast.show('Failed to update: ' + (err?.error?.message || 'Unknown error'), 'danger', 4000);
      }
    });
  }

  confirmDelete(): void { this.showDeleteConfirm.set(true); }

  cancelDelete(): void { this.showDeleteConfirm.set(false); }

  showDeleteBlockedReason(): void {
    const d = this.detail();
    const count = d?.accountingRecords?.length ?? 0;
    this.toast.show(
      `Cannot delete — this registration has ${count} accounting record${count !== 1 ? 's' : ''}. You do have the option to make the registrant inactive.`,
      'warning',
      5000
    );
  }

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
