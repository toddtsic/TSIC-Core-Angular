import { Component, ChangeDetectionStrategy, input, output, signal, linkedSignal, computed, HostListener, inject, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import type { RegistrationDetailDto, AccountingRecordDto, FamilyContactDto, UserDemographicsDto, JobOptionDto } from '@core/api';
import { RegistrationSearchService } from '../services/registration-search.service';
import { RoleIds } from '@infrastructure/constants/roles.constants';
import { ToastService } from '@shared-ui/toast.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { AccountingLedgerComponent, CcChargeEvent, CheckOrCorrectionEvent, RefundEvent } from '@shared-ui/components/accounting-ledger/accounting-ledger.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { ClubRepPaymentComponent } from '@shared-ui/components/club-rep-payment/club-rep-payment.component';
import { FamilyPaymentComponent } from '@shared-ui/components/family-payment/family-payment.component';
import { ResizablePanelDirective } from '@shared-ui/directives/resizable-panel.directive';
import { environment } from '@environments/environment';

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

/** Coerce stored select values to match their option casing (e.g. "adult m" → "Adult M"). Pure —
 *  returns a NEW object, never mutates the input. */
function normalizeSelectValues(raw: Record<string, any>, fields: FieldMetadata[]): Record<string, any> {
  const pv = { ...raw };
  for (const field of fields) {
    if (field.type !== 'select' || !field.options?.length) continue;
    const stored = pv[field.key];
    if (!stored) continue;
    const match = field.options.find(o => o.toLowerCase() === String(stored).toLowerCase());
    if (match && match !== stored) pv[field.key] = match;
  }
  return pv;
}

/** Seed transforms — pure functions of the detail DTO. Used both to initialize the editable
 *  linkedSignals when a new registrant loads AND to compute the dirty-tracking baselines, so the
 *  baseline and the initial editable value are guaranteed identical. */
function seedFamilyContact(d: RegistrationDetailDto | null): FamilyContactDto {
  const fc = d?.familyContact;
  if (!fc) return {};
  return { ...fc, momCellphone: formatPhone(fc.momCellphone), dadCellphone: formatPhone(fc.dadCellphone) };
}
function seedDemographics(d: RegistrationDetailDto | null): UserDemographicsDto {
  const demo = d?.userDemographics;
  if (!demo) return {};
  const out = { ...demo };
  if (out.dateOfBirth) out.dateOfBirth = out.dateOfBirth.substring(0, 10);
  out.cellphone = formatPhone(out.cellphone);
  return out;
}
function seedFamilyDemographics(d: RegistrationDetailDto | null): UserDemographicsDto {
  const fDemo = d?.familyAccountDemographics;
  if (!fDemo) return {};
  const out = { ...fDemo };
  out.cellphone = formatPhone(out.cellphone);
  return out;
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
  imports: [CommonModule, FormsModule, AccountingLedgerComponent, ConfirmDialogComponent, ClubRepPaymentComponent, FamilyPaymentComponent, ResizablePanelDirective],
  templateUrl: './registration-detail-panel.component.html',
  styleUrl: './registration-detail-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RegistrationDetailPanelComponent implements OnChanges {
  detail = input<RegistrationDetailDto | null>(null);
  isOpen = input<boolean>(false);

  // Role-filter context from the parent search — mirrors batch-email-modal.
  // Used to gate the Club Rep delete: only available when the search is constrained to Club Rep only.
  activeRoleIds = input<string[]>([]);

  closed = output<void>();
  saved = output<void>();
  // refundRequested removed — refunds now handled inside accounting-ledger modal

  private searchService = inject(RegistrationSearchService);
  private toast = inject(ToastService);
  private auth = inject(AuthService);


  // Tab state — Accounting is the default/first tab; resets to it only when a DIFFERENT registrant
  // opens. Keyed on registrationId (not the whole detail object) so an in-place refresh of the SAME
  // registrant — e.g. after "Live update" / a profile save re-fetches the detail — preserves the tab
  // the user is on instead of yanking them back to Accounting.
  activeTab = linkedSignal({ source: () => this.detail()?.registrationId, computation: () => 'accounting' as TabType });

  // Parsed profile metadata (fields, with option sets resolved). Pure derivation of the detail input —
  // recomputes only when a new registrant loads.
  metadataFields = computed<FieldMetadata[]>(() =>
    this.parseMetadataFields(this.detail()?.profileMetadataJson, this.detail()?.jsonOptions));

  // Editable profile values, reseeded from the detail input whenever a NEW registrant loads (source =
  // detail) and normalized to option casing. Between loads the admin's edits — and the USLax "Live
  // update" expiry write — persist via .set(); a reseed fires ONLY when detail() itself changes, so a
  // write can never be clobbered by a stale reseed. (This replaced a constructor effect() that
  // transitively read profileValues and thus re-ran on its own writes, silently wiping them.)
  profileValues = linkedSignal({
    source: () => this.detail(),
    computation: (d) => normalizeSelectValues({ ...(d?.profileValues ?? {}) }, this.metadataFields())
  });

  // Contact zone editable state
  isSavingContact = signal<boolean>(false);

  // Family contact (player roles only — editable: email + cellphone; read-only: names)
  familyContact = linkedSignal({ source: () => this.detail(), computation: (d) => seedFamilyContact(d) });
  hasFamilyLink = computed(() => !!this.detail()?.familyContact);

  // User demographics (player's own — editable: email, cellphone, dob, gender)
  demographics = linkedSignal({ source: () => this.detail(), computation: (d) => seedDemographics(d) });

  // Family account demographics (player roles only — email, cell, address)
  familyDemographics = linkedSignal({ source: () => this.detail(), computation: (d) => seedFamilyDemographics(d) });

  // Role detection
  isPlayerRole = computed(() => PLAYER_ROLES.has(this.detail()?.roleName?.toLowerCase().trim() ?? ''));
  isClubRep = computed(() => this.detail()?.isClubRep === true);

  /** Player registered under a family account → show the combined family-accounting view. */
  isFamilyPlayer = computed(() => this.isPlayerRole() && !!this.detail()?.familyUserId);

  // Editable profile fields (excludes team selection, reorders for lacrosse)
  editableProfileFields = computed(() => {
    let fields = this.metadataFields().filter(f => !PROFILE_EXCLUDED_KEYS.has(f.key.toLowerCase()));
    // The USLax expiry is server-authoritative — never a field the registrant fills — so a USLax template
    // carries the number but not its expiry. Guarantee a read-only expiry slot whenever this is a Lacrosse
    // reg with a number on file, so "Live update" (revalidateUsLax) has a bound element to repaint; without
    // it, the fetched date is written to profileValues with nothing on screen bound to it. Renders read-only
    // via isDerivedReadOnlyField; the value comes from profileValues (populated server-side, null when unset).
    if (this.canRevalidateUsLax() && !fields.some(f => f.key.toLowerCase() === 'sportassnidexpdate')) {
      fields = [...fields, { key: 'SportAssnIdexpDate', label: 'USA Lacrosse # Expiration', type: 'date' }];
    }
    return this.reorderForSport(fields);
  });

  /** Show the metadata-driven profile card for ANY role whose template has fields — players and
   *  adults (coach/Staff/Referee/Recruiter) alike. Template-less roles (Club Rep) fall back to the
   *  legacy read-only list. The backend ships the role-appropriate metadata, so this stays role-agnostic. */
  readonly showProfileCard = computed(() => this.editableProfileFields().length > 0);

  /** Card heading — player vs generic adult/registration profile. */
  readonly profileCardTitle = computed(() => this.isPlayerRole() ? 'Player Profile' : 'Registration Profile');

  /** Coach/Staff decoded team-request note (read-only), surfaced by the backend from the codified blob. */
  readonly coachRequestNote = computed(() => this.detail()?.coachRequestNote ?? null);

  // Profile save state
  isSavingProfile = signal<boolean>(false);

  // USA Lacrosse live re-validation (records SportAssnIdexpDate server-side)
  revalidating = signal<boolean>(false);

  /** This is a Lacrosse job (USLax membership applies). */
  readonly isLacrosse = computed(() => this.detail()?.sportName?.toLowerCase() === 'lacrosse');
  /** Show the "Live update" link only for a Lacrosse job with a number on file. */
  readonly canRevalidateUsLax = computed(() => this.isLacrosse() && !!this.profileValues()['SportAssnId']);

  // Email draft — cleared whenever a new registrant loads (a transient draft, never carried across).
  emailSubject = linkedSignal({ source: () => this.detail(), computation: () => '' });
  emailBody = linkedSignal({ source: () => this.detail(), computation: () => '' });

  // ── Unsaved-changes tracking ──
  // Snapshots of the editable zones, taken when detail loads and after each save. Compared
  // against current state to drive the per-section Save affordance + the discard-on-close
  // guard. Email is intentionally excluded (a transient draft, cleared on send/load).
  // Reseeded from the detail input on load, and re-set after each successful save. Computed PURELY from
  // detail (never the live editable signals) so editing a field moves the live serialization but NOT the
  // baseline — otherwise the dirty comparison would always match and the zone would read as never-dirty.
  private snapshotContact = linkedSignal({ source: () => this.detail(), computation: (d) => this.contactBaseline(d) });
  private snapshotProfile = linkedSignal({ source: () => this.detail(), computation: (d) => this.profileBaseline(d) });
  showDiscardConfirm = signal<boolean>(false);

  private serializeContact(): string {
    const player = this.isPlayerRole() && this.hasFamilyLink();
    return JSON.stringify({
      demo: this.demographics(),
      famContact: player ? this.familyContact() : null,
      famDemo: player ? this.familyDemographics() : null
    });
  }
  private serializeProfile(): string {
    return JSON.stringify(this.profileValues());
  }

  /** Dirty-tracking baseline for the Contact zone — mirrors serializeContact() applied to the freshly
   *  SEEDED values, but built purely from the detail input so it never moves when the user edits. */
  private contactBaseline(d: RegistrationDetailDto | null): string {
    const role = d?.roleName?.toLowerCase().trim() ?? '';
    const player = PLAYER_ROLES.has(role) && !!d?.familyContact;
    return JSON.stringify({
      demo: seedDemographics(d),
      famContact: player ? seedFamilyContact(d) : null,
      famDemo: player ? seedFamilyDemographics(d) : null
    });
  }
  /** Dirty-tracking baseline for the Profile zone — mirrors serializeProfile() of the seeded values. */
  private profileBaseline(d: RegistrationDetailDto | null): string {
    return JSON.stringify(normalizeSelectValues({ ...(d?.profileValues ?? {}) }, this.metadataFields()));
  }

  /** Contact Info zone has unsaved edits (player/family demographics + family contact). */
  readonly isContactDirty = computed(() => this.serializeContact() !== this.snapshotContact());
  /** Player Profile zone has unsaved edits (metadata-driven profile values). */
  readonly isProfileDirty = computed(() => this.serializeProfile() !== this.snapshotProfile());
  /** Any savable zone is dirty — gates the close guard. */
  readonly isDirty = computed(() => this.isContactDirty() || this.isProfileDirty());

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

  /** True when this registration's role is Club Rep (by role name, not the active-team flag). */
  isClubRepRole = computed(() => this.detail()?.roleName === 'Club Rep');

  /** True when the active search is constrained to exactly the plain Club Rep role. Deliberately does
   *  NOT accept the "ACTIVE, NOT WAITLISTED" sentinel: that filter returns only reps who own an active
   *  team, and a club rep is deletable only with zero teams (see canDelete) — so exposing the delete
   *  control there would surface a control that can never fire. Matched on the stable role GUID. */
  clubRepOnlySearch = computed(() => {
    const ids = this.activeRoleIds();
    return ids.length === 1 && ids[0].toLowerCase() === RoleIds.ClubRep.toLowerCase();
  });

  /**
   * Whether the delete control is shown at all. For a Club Rep it only surfaces for a Superuser
   * whose search is scoped to Club Rep only; all other roles keep the existing always-shown control.
   */
  showDeleteControl = computed(() => {
    const d = this.detail();
    if (!d) return false;
    if (this.isClubRepRole()) return this.auth.isSuperuser() && this.clubRepOnlySearch();
    return true;
  });

  canDelete = computed(() => {
    const d = this.detail();
    if (!d) return false;
    const noAccounting = !d.accountingRecords || d.accountingRecords.length === 0;
    if (this.isClubRepRole()) {
      return noAccounting && (d.clubRepTeamCount ?? 0) === 0;
    }
    return noAccounting;
  });

  /**
   * The ONE genuine side effect tied to a new registrant loading: a Production-only live Authorize.Net
   * refresh of the subscription card. Everything the old constructor effect() used to do imperatively —
   * seeding profileValues / familyContact / demographics / email draft / the ARB snapshot / dirty-tracking
   * baselines — is now declarative (computed + linkedSignal seeded from detail), which is why writing the
   * USLax expiry can no longer be clobbered by a self-triggering reseed. Only the HTTP call, which is a
   * true side effect, remains here — and it reads only detail(), writing signals it never reads back.
   *
   * Off-Production the subscription lives in an account this host can't reach, so the stored snapshot
   * (seeded synchronously into `subscription`) is the honest source and there's no failing live call.
   */
  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['detail']) return;
    const d = this.detail();
    if (d && this.isProdEnv && d.hasSubscription) this.loadSubscription();
  }

  @HostListener('document:keydown.escape')
  onEscapeKey(): void {
    if (this.isOpen()) { this.close(); }
  }

  close(): void {
    // Don't let an accidental X / backdrop / Esc silently throw away edits in either zone.
    if (this.isDirty()) {
      this.showDiscardConfirm.set(true);
      return;
    }
    this.closed.emit();
  }

  confirmDiscard(): void {
    this.showDiscardConfirm.set(false);
    this.closed.emit();
  }

  cancelDiscard(): void {
    this.showDiscardConfirm.set(false);
  }

  setActiveTab(tab: TabType): void {
    this.activeTab.set(tab);
    // Live refresh is Production-only; off-Production the stored snapshot (seeded on load) stands.
    if (tab === 'accounting' && this.isProdEnv && this.detail()?.hasSubscription && !this.subscriptionIsLive()) {
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

  /** The editable USA Lacrosse # field that carries the "Live update" link (Lacrosse jobs only). */
  isUsLaxNumberField(field: FieldMetadata): boolean {
    return this.canRevalidateUsLax() && field.key.toLowerCase() === 'sportassnid';
  }

  /** USA Lacrosse expiry is USLax-authoritative: server-derived and refreshed ONLY via the revalidate
   *  ("Live update") path. Render it read-only in the card — an admin can never type an unverified date.
   *  The backend enforces the same lock, so this is the matching UI, not the guarantee. */
  isDerivedReadOnlyField(field: FieldMetadata): boolean {
    return field.key.toLowerCase() === 'sportassnidexpdate';
  }

  /**
   * Live-refresh the registrant's USA Lacrosse membership. The backend re-pings USA Lacrosse and
   * records the returned expiry onto this registration's SportAssnIdexpDate. We mirror the new date
   * locally (and re-snapshot so the profile zone doesn't read as dirty — the server already saved it).
   *
   * Deliberately does NOT emit `saved`: that fires the parent's full refresh (re-run the grid search +
   * re-fetch the entire detail), which flashes the panel and yanks the tab back to Accounting. The
   * expiry isn't a grid column and we've already reflected it locally, so a parent refresh buys nothing
   * here — the in-place update below IS the smooth update.
   */
  revalidateUsLax(): void {
    const d = this.detail();
    if (!d || this.revalidating()) return;

    this.revalidating.set(true);
    this.searchService.revalidateUsLax(d.registrationId).subscribe({
      next: (res) => {
        this.revalidating.set(false);
        if (res.found) {
          if (res.expDate) {
            this.applyExpiryDate(res.expDate);
            const exp = new Date(res.expDate + 'T00:00:00').toLocaleDateString();
            this.toast.show(`${res.memStatus ?? 'Active'} · expires ${exp}`, 'success', 4000, 'USA Lacrosse');
          } else {
            // Membership is valid but USA Lacrosse returned no parseable expiration — the server didn't
            // record a date, so don't imply we did. Surface it plainly rather than a "success · n/a".
            this.toast.show(`${res.memStatus ?? 'Active'} — USA Lacrosse returned no expiration date`, 'warning', 5000, 'USA Lacrosse');
          }
        } else {
          this.toast.show(res.message || 'Membership not found.', 'warning', 5000, 'USA Lacrosse');
        }
      },
      error: (err) => {
        this.revalidating.set(false);
        this.toast.show(err?.error?.message || 'Could not re-validate — try again.', 'danger', 4000, 'USA Lacrosse');
      }
    });
  }

  /**
   * Mirror the freshly-recorded USLax expiry into the profile card in place. The read-only expiry input
   * binds to profileValues()[field.key], and that key is whatever the job's metadata declares for the
   * column — casing can differ from the entity name. Write under the field's ACTUAL key (matched
   * case-insensitively, falling back to the canonical column name) so a hardcoded-casing mismatch can't
   * silently drop the value. Re-snapshot afterward so the zone doesn't read as dirty — the server saved it.
   */
  private applyExpiryDate(expDate: string): void {
    const expiryField = this.metadataFields().find(f => f.key.toLowerCase() === 'sportassnidexpdate');
    const key = expiryField?.key ?? 'SportAssnIdexpDate';
    this.profileValues.set({ ...this.profileValues(), [key]: expDate });
    this.snapshotProfile.set(this.serializeProfile());
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
        this.snapshotProfile.set(this.serializeProfile());   // saved → zone is clean again
        this.toast.show('Player profile saved', 'success', 3000, 'Profile Updated');
        this.saved.emit();
      },
      error: (err) => {
        this.isSavingProfile.set(false);
        this.toast.show(err?.error?.message || 'Unknown error', 'danger', 0, 'Save Failed');
      }
    });
  }

  nonPlayerFields(): { key: string; label: string; value: string }[] {
    const pv = this.profileValues();
    const result: { key: string; label: string; value: string }[] = [];
    for (const [key, label] of Object.entries(NON_PLAYER_FIELD_LABELS)) {
      const val = pv[key];
      if (this.hasValue(val)) {
        result.push({ key, label, value: String(val) });
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

  private parseMetadataFields(metadataJson: string | null | undefined, jsonOptions?: string | null): FieldMetadata[] {
    if (!metadataJson) return [];
    try {
      const raw = JSON.parse(metadataJson);
      const optionSets = this.parseOptionSets(jsonOptions);
      const fields: FieldMetadata[] = [];

      const items = Array.isArray(raw) ? raw : (raw.fields && Array.isArray(raw.fields) ? raw.fields : null);

      if (items) {
        for (const f of items) {
          if (f.visibility === 'hidden') continue;

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
        this.snapshotContact.set(this.serializeContact());   // saved → zone is clean again
        this.toast.show('Contact info saved', 'success', 3000);
        this.saved.emit();
      },
      error: (err) => {
        this.isSavingContact.set(false);
        this.toast.show(err?.error?.message || 'Unknown error', 'danger', 0, 'Save Failed');
      }
    });
  }

  // ── Accounting (delegated to shared AccountingLedgerComponent) ──

  onRefundSubmitted(event: RefundEvent): void {
    this.searchService.processRefund({
      accountingRecordId: event.accountingRecordId,
      refundAmount: event.refundAmount,
      reason: 'Admin refund'
    }).subscribe({
      next: (result) => {
        if (result.success) {
          this.toast.show(`$${event.refundAmount.toFixed(2)} refunded`, 'success', 4000, 'CC Refund');
          this.saved.emit();
        } else {
          this.toast.show(result.message ?? 'Unknown error', 'danger', 0, 'CC Refund Failed');
        }
      },
      error: (err) => {
        this.toast.show(err?.error?.message || 'Unknown error', 'danger', 0, 'CC Refund Failed');
      }
    });
  }

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
          this.toast.show(`$${event.amount.toFixed(2)} charged successfully`, 'success', 3000, 'CC Charge');
          this.saved.emit();
        } else {
          this.toast.show(response.error || 'Unknown error', 'danger', 0, 'CC Charge Failed');
        }
      },
      error: (err) => { this.toast.show(err.error?.message || 'Unknown error', 'danger', 0, 'CC Charge Failed'); }
    });
  }

  onCheckSubmitted(event: CheckOrCorrectionEvent): void {
    const d = this.detail();
    if (!d) return;
    const label = event.paymentType === 'Check' ? 'Check Payment' : 'Correction';

    this.searchService.recordPayment(d.registrationId, {
      registrationId: d.registrationId,
      amount: event.amount,
      paymentType: event.paymentType,
      checkNo: event.checkNo,
      comment: event.comment
    }).subscribe({
      next: (response) => {
        if (response.success) {
          this.toast.show(`$${event.amount.toFixed(2)} recorded`, 'success', 3000, label);
          this.saved.emit();
        } else {
          this.toast.show(response.error || 'Unknown error', 'danger', 0, `${label} Failed`);
        }
      },
      error: (err) => { this.toast.show(err.error?.message || 'Unknown error', 'danger', 0, `${label} Failed`); }
    });
  }

  // ── Subscription ──

  // Only Production talks to live Authorize.Net; every other host is sandboxed and the
  // subscription lives in an account it can't reach, so we lean on the stored snapshot.
  private readonly isProdEnv = environment.envName === 'production';

  // Seeded synchronously from the stored ARB snapshot (Registrations.AdnSubscription* columns) so the
  // card shows in EVERY environment with no gateway call. loadSubscription() (Production only) later
  // .set()s the live Authorize.Net record; that override persists until the next registrant loads.
  subscription = linkedSignal({ source: () => this.detail(), computation: (d) => d?.storedSubscription ?? null });

  // Payment progress for the header ARB badge (x of y occurrences). Derived from paid ÷ per-occurrence,
  // but ONLY shown when the paid total is a CLEAN multiple of the occurrence amount (uniform monthly
  // ARB) — never infer a count off a deposit / partial / mixed balance, which would misstate money.
  arbProgress = computed<{ paid: number; total: number } | null>(() => {
    const sub = this.subscription();
    const d = this.detail();
    if (!sub || !d) return null;
    const total = sub.totalOccurrences;
    const per = sub.perOccurrenceAmount;
    const paidTotal = d.paidTotal ?? 0;
    if (!total || !per || per <= 0) return null;
    const paid = Math.round(paidTotal / per);
    if (paid < 0 || paid > total || Math.abs(paid * per - paidTotal) > 0.005) return null;
    return { paid, total };
  });
  // True only once a LIVE Authorize.Net read has succeeded (Production). While false, the card
  // is showing the stored snapshot — which is display-only, so destructive actions stay hidden.
  subscriptionIsLive = linkedSignal({ source: () => this.detail(), computation: () => false });
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
        this.subscriptionIsLive.set(true);
        this.isLoadingSubscription.set(false);
      },
      error: () => {
        // Live refresh failed (e.g. ADN outage). Keep the stored snapshot on screen rather than
        // wiping it to "no subscription", and mark it as not-live so actions stay gated.
        this.subscriptionIsLive.set(false);
        this.isLoadingSubscription.set(false);
        this.toast.show('Live subscription status unavailable; showing the stored record.', 'warning', 5000);
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
        this.toast.show(err?.error?.message || 'Unknown error', 'danger', 0, 'Cancel Subscription Failed');
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
        this.toast.show(err.error?.message || 'Unknown error', 'danger', 0, 'AI Draft Failed');
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
        this.toast.show(err?.error?.message || 'Unknown error', 'danger', 0, 'Email Failed');
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
        this.toast.show(err?.error?.message || 'Unknown error', 'danger', 0, 'Load Failed');
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
        this.toast.show(err?.error?.message || 'Unknown error', 'danger', 0, 'Job Change Failed');
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
        this.toast.show(err?.error?.message || 'Unknown error', 'danger', 0, 'Update Failed');
      }
    });
  }

  confirmDelete(): void { this.showDeleteConfirm.set(true); }

  cancelDelete(): void { this.showDeleteConfirm.set(false); }

  showDeleteBlockedReason(): void {
    const d = this.detail();
    if (!d) return;

    const teamCount = d.clubRepTeamCount ?? 0;
    if (this.isClubRepRole() && teamCount > 0) {
      this.toast.show(
        `Cannot delete — this club rep has ${teamCount} team${teamCount !== 1 ? 's' : ''} attached. Reassign or remove the team${teamCount !== 1 ? 's' : ''} first.`,
        'warning',
        5000
      );
      return;
    }

    const count = d.accountingRecords?.length ?? 0;
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
        this.toast.show(err?.error?.message || 'Unknown error', 'danger', 0, 'Delete Failed');
      }
    });
  }
}
