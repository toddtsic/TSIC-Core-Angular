import { AfterViewInit, ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, input, OnInit, output, signal, computed, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators, AbstractControl, ValidationErrors } from '@angular/forms';
import { debounceTime, distinctUntilChanged, filter, switchMap, catchError, tap, map } from 'rxjs/operators';
import { of } from 'rxjs';
import { ClubService } from '@infrastructure/services/club.service';
import { AccountService } from '@infrastructure/services/account.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { TosContentComponent } from '../../shared/components/tos-content.component';
import { FormFieldDataService, type SelectOption } from '@infrastructure/services/form-field-data.service';
import { ToastService } from '@shared-ui/toast.service';
import { USERNAME_PATTERN } from '@shared-ui/validators/username.validators';
import type { ClubRepRegistrationRequest, ClubRepProfileDto, ClubRepProfileUpdateRequest, ClubSearchResult } from '@core/api';

/**
 * Club rep self-registration / profile-edit form.
 * Renders form fields only — the consumer owns the title and card chrome.
 *
 * A club name never blocks sign-up (Todd 2026-10-06). When clubs of the same name exist —
 * typically a new rep taking over from their club's old rep — the rep answers "Is your club
 * one of these?": the picked club's teams are copied into theirs, or "None of these" starts
 * empty. The server silently claims an unclaimed empty club of exactly the typed name.
 */
@Component({
    selector: 'app-club-rep-register-form',
    standalone: true,
    imports: [ReactiveFormsModule, TosContentComponent, DatePipe],
    styles: [`
      :host { display: block; }

      /* Hero-sized variant of .field-input — used for the Club Name input,
         which is the primary action target of the create form. */
      .field-input--hero {
        padding: var(--space-2) var(--space-3);
        font-size: var(--font-size-base);
        line-height: 1.4;
      }

      /* ── Same-name clubs: "Is your club one of these?" (a choice, never a block) ──
         The question is the site's informational callout (.tsic-callout--info); the
         clubs list under it. Row surfaces inherit .card-body (light/dark); the primary
         tint is an rgba overlay that works over either. */
      .club-choice {
        margin: var(--space-2) 0 0;
        padding: 0;
        border: 0;
        min-width: 0;
      }
      .known-q { display: block; font-weight: var(--font-weight-bold); }
      .known-p { display: block; margin-top: var(--space-1); font-weight: var(--font-weight-normal); }
      .club-known-list {
        margin-top: var(--space-2);
        border: 1px solid var(--border-color);
        border-radius: var(--radius-md);
        overflow: hidden;
      }
      .known-club-row {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        margin: 0;
        padding: var(--space-2) var(--space-3);
        border-bottom: 1px solid var(--border-color);
        font-size: var(--font-size-sm);
        color: var(--brand-text);
        cursor: pointer;
      }
      .known-club-row:last-child { border-bottom: none; }
      .known-club-row:hover { background: rgba(var(--bs-primary-rgb), 0.04); }
      .known-club-row.is-chosen {
        background: rgba(var(--bs-primary-rgb), 0.12);
        box-shadow: inset 4px 0 0 var(--bs-primary);
      }
      .known-club-row .form-check-input { flex-shrink: 0; margin: 0; }
      .known-club-row .form-check-input:focus-visible { outline: none; box-shadow: var(--shadow-focus); }

      /* ── Shared ──────────────────────────────────────────── */
      .form-divider { border-color: var(--border-color); opacity: 0.5; }
      .form-section-title {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        margin: 0 0 var(--space-2);
        display: flex;
        align-items: center;
      }
      .form-section-title i { color: var(--bs-primary); }
      .value-prop {
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
        line-height: var(--line-height-normal);
        padding: var(--space-1) 0;
      }
      .value-prop i { color: var(--bs-success); }

      /* ── ToS acceptance row (above Create Account) ─────── */
      .tos-acceptance-row {
        display: flex;
        align-items: flex-start;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        margin-top: var(--space-2);
        background: var(--brand-bg);
        border: 1px solid var(--border-color);
        border-radius: var(--radius-md);
        font-size: var(--font-size-sm);
        color: var(--brand-text);
        line-height: 1.4;
      }
      .tos-acceptance-row input[type="checkbox"] {
        flex-shrink: 0;
        width: 18px;
        height: 18px;
        margin-top: 1px;
        accent-color: var(--bs-primary);
        cursor: pointer;
      }
      .tos-acceptance-row label {
        margin: 0;
        cursor: pointer;
        user-select: none;
      }
      .tos-link-btn {
        background: none;
        border: none;
        padding: 0;
        color: var(--bs-primary);
        font: inherit;
        text-decoration: underline;
        cursor: pointer;
      }
      .tos-link-btn:hover { text-decoration: none; }
      .tos-link-btn:focus-visible {
        outline: none;
        box-shadow: var(--shadow-focus);
        border-radius: var(--radius-sm);
      }

      /* ── Inline collapsible ToS panel ─────────────────── */
      .tos-inline-panel {
        margin-top: var(--space-2);
        border: 1px solid var(--border-color);
        border-radius: var(--radius-md);
        background: var(--brand-surface);
        overflow: hidden;
      }
      .tos-inline-scroll {
        max-height: 320px;
        overflow-y: auto;
        padding: var(--space-3) var(--space-4);
      }
    `],
    template: `
    <form [formGroup]="form" (ngSubmit)="onSubmit()">

              @if (!isEdit()) {
              <!-- ═══ CLUB NAME INPUT (hero of the form) ═══ -->
              <div class="mb-2">
                <label class="field-label">Club Name <span class="req-star">*</span></label>
                <input #clubNameInput class="field-input field-input--hero" formControlName="clubName"
                       placeholder="Start typing your club name..."
                       autocomplete="off"
                       [class.is-invalid]="showError('clubName')" />
                @if (errorText('clubName'); as msg) { <div class="field-error">{{ msg }}</div> }
              </div>

              <!-- Loading -->
              @if (clubSearchLoading()) {
                <div class="text-center py-2">
                  <span class="spinner-border spinner-border-sm text-primary me-1"></span>
                  <span class="small text-muted">Checking for your club...</span>
                </div>
              }

              <!-- ═══ CLUBS ALREADY ON TSIC ═══
                   A choice, never a block (Todd 2026-10-06): a rep taking over or joining a club picks
                   WHICH club is theirs, and that club's saved teams are copied into their own — names
                   aren't unique, so the pick is by club, not by name. "None of these" starts empty.
                   Create Account waits for an answer. No rep names or emails here. -->
              @if (similarMatches().length > 0) {
                <fieldset class="club-choice" aria-labelledby="club-choice-q">
                  <div class="tsic-callout tsic-callout--info tsic-callout--block" role="note">
                    <i class="bi bi-info-circle" aria-hidden="true"></i>
                    <span>
                      <span class="known-q" id="club-choice-q">{{ similarMatches().length === 1 ? 'Is this your club?' : 'Is your club one of these?' }}</span>
                      <span class="known-p">
                        Pick it and its teams come with you &mdash; nothing to retype.
                        Not your club? Pick <b>None of these</b>.
                      </span>
                    </span>
                  </div>
                  <div class="club-known-list">
                  @for (club of similarMatches(); track club.clubId) {
                    <label class="known-club-row" [class.is-chosen]="clubChoice() === club.clubId">
                      <input type="radio" class="form-check-input" name="clubChoice"
                             [checked]="clubChoice() === club.clubId" (change)="chooseClub(club)">
                      <span>
                        <span class="fw-semibold">{{ club.clubName }}</span>
                        @if (club.state) {
                          <span class="text-muted ms-1">({{ club.state }})</span>
                        }
                        <span class="text-muted ms-1">&bull; {{ club.activeTeamCount }} {{ club.activeTeamCount === 1 ? 'team' : 'teams' }}</span>
                        @if (club.lastRegistered) {
                          <span class="text-muted ms-1">&bull; last registered {{ club.lastRegistered | date: 'MMM y' }}</span>
                        }
                      </span>
                    </label>
                  }
                  <label class="known-club-row" [class.is-chosen]="clubChoice() === 'none'">
                    <input type="radio" class="form-check-input" name="clubChoice"
                           [checked]="clubChoice() === 'none'" (change)="clubChoice.set('none')">
                    <span><span class="fw-semibold">None of these</span>
                      <span class="text-muted ms-1">&mdash; we're a new club</span></span>
                  </label>
                  </div>
                </fieldset>
              }
              }

                <hr class="form-divider my-3">
                <h6 class="form-section-title">
                  <i class="bi bi-person-vcard me-2"></i>Club Rep Details
                </h6>

                @if (!isEdit()) {
                  <!-- ═══ CREDENTIALS ═══ -->
                  <div class="row g-2 mb-2">
                    <div class="col-12">
                      <input class="field-input" formControlName="username"
                             placeholder="Username" autocomplete="off"
                             [class.is-required]="!form.controls.username.value?.trim()"
                             [class.is-invalid]="showError('username') || usernameStatus() === 'taken'" />
                      @if (errorText('username'); as msg) {
                        <div class="field-error">{{ msg }}</div>
                      } @else if (usernameStatus() === 'checking') {
                        <div class="small text-muted mt-1"><span class="spinner-border spinner-border-sm me-1"></span>Checking availability…</div>
                      } @else if (usernameStatus() === 'taken') {
                        <div class="field-error">That username is already taken — choose another.</div>
                      }
                    </div>
                    <div class="col-6">
                      <div class="position-relative">
                        <input [type]="showPassword() ? 'text' : 'password'" class="field-input pe-5" formControlName="password"
                               placeholder="Password" autocomplete="new-password"
                               [class.is-required]="!form.controls.password.value"
                               [class.is-invalid]="showError('password')" />
                        <button type="button" class="password-toggle"
                                (click)="showPassword.set(!showPassword())"
                                [attr.aria-label]="showPassword() ? 'Hide password' : 'Show password'" tabindex="-1">
                          <i class="bi" [class.bi-eye]="!showPassword()" [class.bi-eye-slash]="showPassword()"></i>
                        </button>
                      </div>
                      @if (errorText('password'); as msg) { <div class="field-error">{{ msg }}</div> }
                    </div>
                    <div class="col-6">
                      <div class="position-relative">
                        <input [type]="showConfirm() ? 'text' : 'password'" class="field-input pe-5" formControlName="confirmPassword"
                               placeholder="Confirm Password" autocomplete="new-password"
                               [class.is-invalid]="showError('confirmPassword') || showMismatch()" />
                        <button type="button" class="password-toggle"
                                (click)="showConfirm.set(!showConfirm())"
                                [attr.aria-label]="showConfirm() ? 'Hide password' : 'Show password'" tabindex="-1">
                          <i class="bi" [class.bi-eye]="!showConfirm()" [class.bi-eye-slash]="showConfirm()"></i>
                        </button>
                      </div>
                      @if (errorText('confirmPassword'); as msg) {
                        <div class="field-error">{{ msg }}</div>
                      } @else if (showMismatch()) {
                        <div class="field-error">Passwords do not match</div>
                      }
                    </div>
                  </div>

                  <hr class="form-divider my-2">
                }

                <!-- ═══ PERSONAL INFO ═══ -->
                <div class="row g-2 mb-2">
                  <div class="col-6">
                    <input #firstNameInput class="field-input" formControlName="firstName"
                           placeholder="First Name"
                           [class.is-required]="!form.controls.firstName.value?.trim()"
                           [class.is-invalid]="showError('firstName')" />
                    @if (errorText('firstName'); as msg) { <div class="field-error">{{ msg }}</div> }
                  </div>
                  <div class="col-6">
                    <input class="field-input" formControlName="lastName"
                           placeholder="Last Name"
                           [class.is-required]="!form.controls.lastName.value?.trim()"
                           [class.is-invalid]="showError('lastName')" />
                    @if (errorText('lastName'); as msg) { <div class="field-error">{{ msg }}</div> }
                  </div>
                </div>
                @if (!isEdit()) {
                  <div class="row g-2 mb-2">
                    <div class="col-12">
                      <select class="field-select" formControlName="gender"
                              [class.is-required]="!form.controls.gender.value"
                              [class.is-invalid]="showError('gender')">
                        <option value="">Gender</option>
                        <option value="M">Male</option>
                        <option value="F">Female</option>
                      </select>
                      @if (errorText('gender'); as msg) { <div class="field-error">{{ msg }}</div> }
                    </div>
                  </div>
                }
                <div class="row g-2 mb-2">
                  <div class="col-7">
                    <input type="email" class="field-input" formControlName="email"
                           placeholder="Email"
                           [class.is-required]="!form.controls.email.value?.trim()"
                           [class.is-invalid]="showError('email')" />
                    @if (errorText('email'); as msg) { <div class="field-error">{{ msg }}</div> }
                  </div>
                  <div class="col-5">
                    <input type="tel" inputmode="numeric" class="field-input"
                           formControlName="cellphone" (input)="digitsOnly('cellphone', $event)"
                           placeholder="Phone (digits only)"
                           [class.is-required]="!form.controls.cellphone.value?.trim()"
                           [class.is-invalid]="showError('cellphone')" />
                    @if (errorText('cellphone'); as msg) { <div class="field-error">{{ msg }}</div> }
                  </div>
                </div>
                <div class="row g-2 mb-2">
                  <div class="col-12">
                    <input class="field-input" formControlName="streetAddress"
                           autocomplete="address-line1"
                           placeholder="Street Address"
                           [class.is-required]="!form.controls.streetAddress.value?.trim()"
                           [class.is-invalid]="showError('streetAddress')" />
                    @if (errorText('streetAddress'); as msg) { <div class="field-error">{{ msg }}</div> }
                  </div>
                </div>
                <div class="row g-2 mb-2">
                  <div class="col-5">
                    <input class="field-input" formControlName="city"
                           autocomplete="address-level2"
                           placeholder="City"
                           [class.is-required]="!form.controls.city.value?.trim()"
                           [class.is-invalid]="showError('city')" />
                    @if (errorText('city'); as msg) { <div class="field-error">{{ msg }}</div> }
                  </div>
                  <div class="col-4">
                    <select class="field-select" formControlName="state"
                            autocomplete="address-level1"
                            [class.is-required]="!form.controls.state.value"
                            [class.is-invalid]="showError('state')">
                      <option value="">State</option>
                      @for (s of stateOptions(); track s.value) {
                        <option [value]="s.value">{{ s.label }}</option>
                      }
                    </select>
                    @if (errorText('state'); as msg) { <div class="field-error">{{ msg }}</div> }
                  </div>
                  <div class="col-3">
                    <input class="field-input" formControlName="postalCode"
                           autocomplete="postal-code"
                           placeholder="Zip"
                           [class.is-required]="!form.controls.postalCode.value?.trim()"
                           [class.is-invalid]="showError('postalCode')" />
                    @if (errorText('postalCode'); as msg) { <div class="field-error">{{ msg }}</div> }
                  </div>
                </div>

                @if (errorMsg()) {
                  <div class="alert alert-danger py-2 small mb-2">{{ errorMsg() }}</div>
                }

                @if (!isEdit()) {
                  <div class="tos-acceptance-row">
                    <input id="clubRepTosAccept" type="checkbox" formControlName="agreeToTos"
                           [class.is-invalid]="showError('agreeToTos')" />
                    <label for="clubRepTosAccept">
                      I have read and agree to the
                      <button type="button" class="tos-link-btn"
                              [attr.aria-expanded]="tosExpanded()"
                              aria-controls="clubRepTosPanel"
                              (click)="tosExpanded.set(!tosExpanded())">
                        Terms of Service<i class="bi ms-1"
                          [class.bi-chevron-down]="!tosExpanded()"
                          [class.bi-chevron-up]="tosExpanded()"></i>
                      </button>.
                    </label>
                  </div>
                  @if (errorText('agreeToTos'); as msg) { <div class="field-error">{{ msg }}</div> }
                  @if (tosExpanded()) {
                    <div id="clubRepTosPanel" class="tos-inline-panel">
                      <div class="tos-inline-scroll">
                        <app-tos-content />
                      </div>
                    </div>
                  }
                }

                <button type="submit" class="btn btn-primary w-100 fw-semibold mt-3"
                        [disabled]="saving() || (!isEdit() && !canSubmit())">
                  @if (saving()) {
                    <span class="spinner-border spinner-border-sm me-1"></span>
                    @if (isEdit()) { Saving... } @else { Creating... }
                  } @else {
                    @if (isEdit()) {
                      <i class="bi bi-check-lg me-1"></i>Save Changes
                    } @else {
                      <i class="bi bi-person-plus-fill me-1"></i>Create Account
                    }
                  }
                </button>
      </form>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClubRepRegisterFormComponent implements OnInit, AfterViewInit {
    /** 'create' = self-register a new ClubRep (default). 'edit' = update current profile. */
    readonly mode = input<'create' | 'edit'>('create');
    /** Existing profile data to prefill in edit mode. */
    readonly existing = input<ClubRepProfileDto | null>(null);

    readonly registered = output<{ username: string; password: string }>();
    readonly saved = output<void>();

    readonly isEdit = computed(() => this.mode() === 'edit');

    private readonly fb = inject(FormBuilder);
    private readonly clubService = inject(ClubService);
    private readonly account = inject(AccountService);
    private readonly auth = inject(AuthService);
    private readonly fieldData = inject(FormFieldDataService);
    private readonly toast = inject(ToastService);
    private readonly destroyRef = inject(DestroyRef);

    // First input refs — focused on view init to drop the user straight into the form.
    private readonly clubNameInput = viewChild<ElementRef<HTMLInputElement>>('clubNameInput');
    private readonly firstNameInput = viewChild<ElementRef<HTMLInputElement>>('firstNameInput');

    // computed, not a captured array: reference.States arrives after bootstrap, and a plain
    // field would pin this select to the static fallback for the life of the component.
    readonly stateOptions = computed<SelectOption[]>(() => this.fieldData.getOptionsForDataSource('states'));

    // UI state
    readonly submitted = signal(false);
    readonly saving = signal(false);
    readonly errorMsg = signal<string | null>(null);
    readonly showPassword = signal(false);
    readonly showConfirm = signal(false);
    readonly tosExpanded = signal(false);

    /** Live username-availability probe (advisory; /clubreps/register is the real gate). */
    readonly usernameStatus = signal<'idle' | 'checking' | 'available' | 'taken'>('idle');

    // Club search state
    readonly clubSearchResults = signal<ClubSearchResult[]>([]);
    readonly clubSearchLoading = signal(false);

    /** Same-name clubs only (server: ClubNameMatcher.IsSameClubName) — the "Is this your club?" panel. */
    readonly similarMatches = computed(() =>
        // A club with no active teams has nothing to bring — picking it is "None of these". Server order:
        // most recently registered first, untouched copies already folded into their source.
        this.clubSearchResults().filter(c => c.isExactMatch && (c.activeTeamCount ?? 0) > 0)
    );

    /** The rep's answer: a listed club's id, 'none' ("None of these"), or null = not answered. */
    readonly clubChoice = signal<number | 'none' | null>(null);

    /** The picked club while it is still listed — editing the name to another club's drops it. */
    readonly chosenClub = computed(() => {
        const choice = this.clubChoice();
        return typeof choice === 'number' ? this.similarMatches().find(c => c.clubId === choice) ?? null : null;
    });

    /** Answered, or nothing to answer: no same-name club is listed. */
    readonly clubChoiceMade = computed(() =>
        this.similarMatches().length === 0 || this.clubChoice() === 'none' || !!this.chosenClub());

    readonly form = this.fb.group({
        clubName: ['', Validators.required],
        firstName: ['', Validators.required],
        lastName: ['', Validators.required],
        gender: ['', Validators.required],
        email: ['', [Validators.required, Validators.email]],
        cellphone: ['', Validators.required],
        streetAddress: ['', Validators.required],
        city: ['', Validators.required],
        state: ['', Validators.required],
        postalCode: ['', [Validators.required, Validators.pattern(/^\d{5}(-\d{4})?$/)]],
        username: ['', [Validators.required, Validators.minLength(3), Validators.pattern(USERNAME_PATTERN)]],
        password: ['', [Validators.required, Validators.minLength(6)]],
        confirmPassword: ['', Validators.required],
        agreeToTos: [false, Validators.requiredTrue],
    }, {
        // Cross-field: confirmPassword must match password. A group validator, not a
        // computed() — form-control values aren't signals, so a computed froze at false.
        validators: (group: AbstractControl): ValidationErrors | null => {
            const pw = group.get('password')?.value;
            const cpw = group.get('confirmPassword')?.value;
            return pw && cpw && pw !== cpw ? { passwordMismatch: true } : null;
        },
    });

    passwordMismatch(): boolean {
        return !!this.form.errors?.['passwordMismatch'];
    }

    /** Mismatch shows once the confirm field has been typed in or left. */
    showMismatch(): boolean {
        const c = this.form.controls.confirmPassword;
        return this.passwordMismatch() && (c.dirty || c.touched || this.submitted());
    }

    /** A field shows its error once typed in or left — never only after submit, because
     *  Create Account is disabled while the form is invalid and submit can't happen. */
    showError(name: string): boolean {
        const c = this.form.get(name);
        return !!c && c.invalid && (c.dirty || c.touched || this.submitted());
    }

    errorText(name: string): string | null {
        if (!this.showError(name)) return null;
        const errors = this.form.get(name)?.errors ?? {};
        if (name === 'agreeToTos') return 'You must agree to the Terms of Service';
        if (errors['required']) return 'Required';
        if (errors['minlength']) return `Min ${errors['minlength'].requiredLength} characters`;
        if (errors['email']) return 'Invalid email';
        if (errors['pattern']) {
            return name === 'username'
                ? 'Letters, numbers, spaces and - ! . _ @ + / only'
                : 'Must be 12345 or 12345-6789';
        }
        return 'Invalid';
    }

    constructor() {
        // Live search — keep prior results visible while typing to avoid stutter.
        // Only clear when the input drops below the search threshold.
        this.form.controls.clubName.valueChanges.pipe(
            distinctUntilChanged(),
            tap((v) => {
                // A new name is a new question: "Is this your club?" is answered again. chooseClub
                // sets its answer after its own setValue, so that answer stands.
                this.clubChoice.set(null);
                if (!v || v.trim().length < 3) {
                    this.clubSearchResults.set([]);
                    this.clubSearchLoading.set(false);
                }
            }),
            debounceTime(300),
            filter((v): v is string => !!v && v.trim().length >= 3),
            tap(() => this.clubSearchLoading.set(true)),
            switchMap(name => this.clubService.searchClubs(name.trim()).pipe(
                catchError(() => of([] as ClubSearchResult[]))
            )),
            takeUntilDestroyed(this.destroyRef)
        ).subscribe(results => {
            this.clubSearchLoading.set(false);
            this.clubSearchResults.set(results);
        });

        // Live username-availability probe — mirrors the club-name search above so the rep
        // learns a username is taken before hitting Create. Advisory; /clubreps/register is
        // still the authoritative uniqueness gate, so an error/throttle just resets to 'idle'.
        this.form.controls.username.valueChanges.pipe(
            distinctUntilChanged(),
            tap((v) => {
                if (!v || v.trim().length < 3) this.usernameStatus.set('idle');
            }),
            debounceTime(400),
            filter((v): v is string => !!v && v.trim().length >= 3),
            tap(() => this.usernameStatus.set('checking')),
            switchMap(name => this.account.checkUsernameAvailable(name.trim()).pipe(
                map(res => (res.available ? 'available' : 'taken') as 'available' | 'taken'),
                catchError(() => of('idle' as const)),
            )),
            takeUntilDestroyed(this.destroyRef),
        ).subscribe(status => this.usernameStatus.set(status));
    }

    ngOnInit(): void {
        if (!this.isEdit()) return;

        // Edit mode: disable the creation-only fields (they're excluded from form.value
        // and from form.invalid when disabled) and prefill the profile fields.
        this.form.controls.clubName.disable();
        this.form.controls.username.disable();
        this.form.controls.password.disable();
        this.form.controls.confirmPassword.disable();
        this.form.controls.agreeToTos.disable();
        // Identity fields are locked too: free name edits would let one rep "hand off"
        // an account by renaming it instead of creating a proper new club rep account.
        this.form.controls.firstName.disable();
        this.form.controls.lastName.disable();
        // Gender is collected on create only; disable so the edit form excludes it.
        this.form.controls.gender.disable();

        const data = this.existing();
        if (data) {
            this.form.patchValue({
                firstName: data.firstName,
                lastName: data.lastName,
                email: data.email,
                cellphone: data.cellphone,
                streetAddress: data.streetAddress,
                city: data.city,
                state: data.state,
                postalCode: data.postalCode,
            });
        }
    }

    ngAfterViewInit(): void {
        // Drop the user straight into the form: clubName for create, firstName for edit.
        // setTimeout defers past the change-detection tick so the @if branch is in the DOM.
        setTimeout(() => {
            const target = this.isEdit() ? this.firstNameInput() : this.clubNameInput();
            target?.nativeElement.focus();
        });
    }

    /** "This is my club": its saved teams come with the rep, and the club name takes its spelling. */
    chooseClub(club: ClubSearchResult): void {
        const c = this.form.controls.clubName;
        c.setValue(club.clubName);
        c.markAsDirty();
        this.clubChoice.set(club.clubId);
    }

    digitsOnly(controlName: string, event: Event): void {
        const input = event.target as HTMLInputElement;
        const digits = input.value.replace(/\D+/g, '').slice(0, 15);
        input.value = digits;
        this.form.get(controlName)?.setValue(digits);
    }

    /** Submit needs a valid form, matching passwords, ToS accepted, a username not known taken, and —
     *  when same-name clubs are listed — an answer to "Is this your club?". The name itself never blocks. */
    canSubmit(): boolean {
        if (this.usernameStatus() === 'taken') return false;
        return this.form.valid && !this.passwordMismatch() && this.clubChoiceMade();
    }

    onSubmit(): void {
        this.submitted.set(true);
        if (this.isEdit()) {
            if (this.form.invalid) return;
            this.submitEdit();
            return;
        }

        if (this.form.invalid || !this.canSubmit() || this.passwordMismatch()) return;

        this.saving.set(true);
        this.errorMsg.set(null);

        const v = this.form.value;
        const request: ClubRepRegistrationRequest = {
            clubName: v.clubName!.trim(),
            firstName: v.firstName!.trim(),
            lastName: v.lastName!.trim(),
            gender: v.gender!,
            email: v.email!.trim(),
            cellphone: v.cellphone!.trim(),
            streetAddress: v.streetAddress!.trim(),
            city: v.city!.trim(),
            state: v.state!,
            postalCode: v.postalCode!.trim(),
            username: v.username!.trim(),
            password: v.password!,
            acceptedTos: true,
            sourceClubId: this.chosenClub()?.clubId ?? null,
        };

        this.clubService.registerClub(request)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (resp) => {
                    if (resp.success) {
                        // Backend has already stamped bTSICWaiverSigned + timestamp.
                        // Auto-login and emit registered — no separate ToS step.
                        this.autoLoginAndEmit(request.username, request.password);
                    } else {
                        this.saving.set(false);
                        this.errorMsg.set(resp.message || 'Registration failed.');
                    }
                },
                error: (err: unknown) => {
                    this.saving.set(false);
                    const httpErr = err as { error?: { message?: string } };
                    this.errorMsg.set(httpErr?.error?.message || 'Request failed.');
                },
            });
    }

    /** After successful registration: sign the user in and emit `registered`. */
    private autoLoginAndEmit(username: string, password: string): void {
        this.auth.login({ username, password })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.saving.set(false);
                    this.registered.emit({ username, password });
                },
                error: () => {
                    this.saving.set(false);
                    this.errorMsg.set('Account created, but sign-in failed. Please log in manually.');
                },
            });
    }

    private submitEdit(): void {
        this.saving.set(true);
        this.errorMsg.set(null);

        const v = this.form.getRawValue();
        const request: ClubRepProfileUpdateRequest = {
            email: (v.email ?? '').trim(),
            cellphone: (v.cellphone ?? '').trim(),
            streetAddress: (v.streetAddress ?? '').trim(),
            city: (v.city ?? '').trim(),
            state: v.state ?? '',
            postalCode: (v.postalCode ?? '').trim(),
        };

        this.clubService.updateSelfProfile(request)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.saving.set(false);
                    this.toast.show('Profile updated.', 'success', 2500);
                    this.saved.emit();
                },
                error: (err: unknown) => {
                    this.saving.set(false);
                    const httpErr = err as { error?: { message?: string } };
                    this.errorMsg.set(httpErr?.error?.message || 'Update failed.');
                },
            });
    }

}
