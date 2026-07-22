import { ChangeDetectionStrategy, Component, computed, inject, OnInit, output, signal, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { ClubService } from '@infrastructure/services/club.service';
import { Roles } from '@infrastructure/constants/roles.constants';
import { TeamWizardStateService } from '../state/team-wizard-state.service';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { LoginComponent } from '@views/auth/login/login.component';
import { ClubRepRegisterFormComponent } from './club-rep-register-form.component';
import { PhonePipe } from '@infrastructure/pipes/phone.pipe';
import type { ClubRepClubDto, ClubRepProfileDto } from '@core/api';

export interface LoginStepResult {
    availableClubs: ClubRepClubDto[];
    clubName: string | null;
}

type LoginView = 'sign-in' | 'create' | 'account-summary';

/**
 * Team wizard's "Club & Rep Info" step — single home for everything club-rep
 * account related: sign-in, create-account (inline), and the authenticated
 * review (read-first display of club + rep, each editable inline in place).
 * Every authenticated arrival lands on the review; advancing to the next step
 * is an explicit Continue (see team.component.ts next/advancePastLogin).
 */
@Component({
    selector: 'app-trw-login-step',
    standalone: true,
    imports: [FormsModule, LoginComponent, ClubRepRegisterFormComponent, PhonePipe],
    styles: [`
      :host { display: block; }

      .account-step {
        max-width: 460px;
        margin: 0 auto;
        padding-top: var(--space-8);
      }
      /* Create/edit views need the full wizard width (matches Waivers/Teams cards) */
      .account-step--wide {
        max-width: 720px;
      }

      .or-divider {
        display: flex;
        align-items: center;
        gap: var(--space-3);
        margin: var(--space-4) 0;
        color: var(--brand-text-muted);
        font-size: var(--font-size-sm);
      }
      .or-divider::before,
      .or-divider::after {
        content: '';
        flex: 1;
        height: 1px;
        background: var(--border-color);
      }

      .create-cta {
        text-align: center;
      }
      .create-cta p {
        color: var(--brand-text-muted);
        font-size: var(--font-size-sm);
        margin-bottom: var(--space-3);
      }

      .back-link {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
        background: none;
        border: none;
        padding: 0 0 var(--space-3);
        color: var(--bs-primary);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-medium);
        cursor: pointer;
      }
      .back-link:hover { text-decoration: underline; }
      .back-link:focus-visible {
        outline: none;
        box-shadow: var(--shadow-focus);
        border-radius: var(--radius-sm);
      }

      /* Card-header treatment for create/edit views — matches Waivers step pattern */
      .create-header h5,
      .edit-header h5 {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        font-weight: var(--font-weight-bold);
      }
      .create-header h5 i,
      .edit-header h5 i { color: var(--bs-primary); }
      .new-pill {
        display: inline-block;
        padding: 2px var(--space-2);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-bold);
        color: white;
        background: var(--bs-primary);
        border-radius: var(--radius-full);
        letter-spacing: 0.04em;
      }

      /* ── Read-first display rows ────────────────────────── */
      .readout-row {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--space-3);
      }
      .readout-value {
        font-size: var(--font-size-base);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        min-width: 0;
        overflow-wrap: anywhere;
      }

      /* Definition-list readout for the rep profile (label / value pairs) */
      .profile-readout {
        margin: 0;
        display: grid;
        gap: var(--space-2);
      }
      .profile-readout > div {
        display: grid;
        grid-template-columns: 88px 1fr;
        gap: var(--space-3);
        align-items: baseline;
      }
      .profile-readout dt {
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text-muted);
        text-transform: uppercase;
        letter-spacing: 0.04em;
      }
      .profile-readout dd {
        margin: 0;
        font-size: var(--font-size-sm);
        color: var(--brand-text);
        overflow-wrap: anywhere;
      }

      .readout-actions {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--space-2);
        margin-top: var(--space-3);
        padding-top: var(--space-3);
        border-top: 1px solid var(--border-color);
      }

      /* "Edit" affordance — quiet, text-button style */
      .inline-edit-btn {
        background: none;
        border: none;
        padding: var(--space-1) var(--space-2);
        color: var(--bs-primary);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-medium);
        cursor: pointer;
        border-radius: var(--radius-sm);
      }
      .inline-edit-btn:hover { text-decoration: underline; }
      .inline-edit-btn:focus-visible { outline: none; box-shadow: var(--shadow-focus); }

      .btn-cancel-inline {
        color: var(--brand-text-muted);
        text-decoration: none;
        font-size: var(--font-size-sm);
        padding-left: 0;
        margin-top: var(--space-2);
      }

      /* ── Club info card ─────────────────────────────────── */
      .club-edit-row {
        display: flex;
        gap: var(--space-2);
        align-items: stretch;
      }
      .club-edit-row .field-input { flex: 1; }
      .btn-save-club {
        flex: 0 0 auto;
        display: inline-flex;
        align-items: center;
        justify-content: center;
        min-width: 44px;
      }
      .club-locked-hint {
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
        white-space: nowrap;
      }
      .club-locked-hint i { color: var(--brand-text-muted); }

      .btn-signout {
        color: var(--brand-text-muted);
        text-decoration: none;
        font-size: var(--font-size-sm);
      }
      .btn-signout:hover { color: var(--bs-danger); }

      .summary-loading {
        text-align: center;
        padding: var(--space-3);
        color: var(--brand-text-muted);
        font-size: var(--font-size-sm);
      }

      @media (max-width: 575.98px) {
        .account-step { padding-top: var(--space-4); }
        .or-divider { margin: var(--space-2) 0; font-size: var(--font-size-xs); }
        .create-cta p { font-size: var(--font-size-xs); margin-bottom: var(--space-2); }
      }
    `],
    template: `
    @switch (view()) {

      @case ('sign-in') {
        <div class="card shadow border-0 card-rounded">
          <div class="card-body account-step">
            @if (error()) {
              <div class="alert alert-danger mb-3">{{ error() }}</div>
            }

            <div class="welcome-hero">
              <h5 class="welcome-title"><i class="bi bi-trophy-fill welcome-icon"></i> Let's Register Your Teams!</h5>
              <p class="wizard-tip">Sign in with your Club Rep credentials to register teams for this event.</p>
            </div>

            <app-login
              [theme]="''"
              [embedded]="true"
              [headerText]="'Club Rep Sign In'"
              [subHeaderText]="'Enter your username and password'"
              [returnUrl]="returnUrl()"
              (loginSuccess)="continueWithLogin()" />

            <div class="or-divider">or</div>

            <div class="create-cta">
              <p>Don't have a club rep account yet?</p>
              <button type="button"
                      class="btn btn-outline-primary fw-semibold w-100"
                      (click)="showCreateAccount()">
                <i class="bi bi-shield-plus me-2"></i>Create NEW Club Rep Account
              </button>
            </div>
          </div>
        </div>
      }

      @case ('create') {
        <div class="account-step account-step--wide">
          <button type="button" class="back-link" (click)="goHome()">
            <i class="bi bi-arrow-left"></i> Back to home
          </button>
          <div class="card shadow border-0 card-rounded">
            <div class="card-header card-header-subtle border-0 py-3 create-header">
              <h5 class="mb-0">
                <i class="bi bi-shield-plus"></i>
                Create Club Rep Account
                <span class="new-pill">NEW</span>
              </h5>
            </div>
            <div class="card-body bg-neutral-0">
              <app-club-rep-register-form
                mode="create"
                (registered)="onRegistered()" />
            </div>
          </div>
        </div>
      }

      @case ('account-summary') {
        <div class="account-step account-step--wide">
          <button type="button" class="back-link" (click)="goHome()">
            <i class="bi bi-arrow-left"></i> Back to home
          </button>

          <!-- ═══ CLUB INFO (read-first) ═══ -->
          @if (registeringClub(); as club) {
            <div class="card shadow border-0 card-rounded mb-3">
              <div class="card-header card-header-subtle border-0 py-3 edit-header">
                <h5 class="mb-0"><i class="bi bi-people-fill"></i> Club</h5>
              </div>
              <div class="card-body">
                @if (editingClub()) {
                  <label class="field-label">Club Name <span class="text-muted small">— editable until your first team is registered</span></label>
                  <div class="club-edit-row">
                    <input class="field-input" [value]="clubNameDraft()"
                           (input)="clubNameDraft.set($any($event.target).value)"
                           [disabled]="savingClub()"
                           placeholder="Club name" />
                    <button type="button" class="btn btn-primary btn-save-club"
                            [disabled]="savingClub() || !clubNameDirty()"
                            (click)="saveClubName()">
                      @if (savingClub()) {
                        <span class="spinner-border spinner-border-sm"></span>
                      } @else {
                        <i class="bi bi-check-lg"></i>
                      }
                    </button>
                  </div>
                  @if (clubError()) {
                    <div class="field-error mt-1">{{ clubError() }}</div>
                  }
                  <button type="button" class="btn btn-link btn-cancel-inline" (click)="cancelEditClub()">Cancel</button>
                } @else {
                  <div class="readout-row">
                    <span class="readout-value">{{ club }}</span>
                    @if (clubEditable()) {
                      <button type="button" class="inline-edit-btn" (click)="startEditClub()">
                        <i class="bi bi-pencil me-1"></i>Edit
                      </button>
                    } @else {
                      <span class="club-locked-hint">
                        <i class="bi bi-lock-fill me-1"></i>Club Name Locked — club teams are registered in this or other events
                      </span>
                    }
                  </div>
                }
              </div>
            </div>
          }

          <!-- ═══ CLUB REP PROFILE (read-first, inline edit) ═══ -->
          <div class="card shadow border-0 card-rounded">
            <div class="card-header card-header-subtle border-0 py-3 edit-header">
              <h5 class="mb-0"><i class="bi bi-person-vcard"></i> Club Rep</h5>
            </div>
            <div class="card-body bg-neutral-0">
              @if (loadingProfile()) {
                <div class="summary-loading">
                  <span class="spinner-border spinner-border-sm me-2"></span>Loading your profile...
                </div>
              } @else if (editingProfile()) {
                <app-club-rep-register-form
                  mode="edit"
                  [existing]="profileForEdit()"
                  (saved)="onProfileSaved()" />
                <button type="button" class="btn btn-link btn-cancel-inline" (click)="cancelEditProfile()">Cancel</button>
              } @else {
                @if (profileForEdit(); as p) {
                  <dl class="profile-readout">
                    <div><dt>Name</dt><dd>{{ p.firstName }} {{ p.lastName }}</dd></div>
                    <div><dt>Email</dt><dd>{{ p.email || '—' }}</dd></div>
                    <div><dt>Phone</dt><dd>{{ p.cellphone | phone }}</dd></div>
                    <div><dt>Address</dt><dd>{{ p.streetAddress }}, {{ p.city }} {{ p.state }} {{ p.postalCode }}</dd></div>
                  </dl>
                }
                <div class="readout-actions">
                  <button type="button" class="inline-edit-btn" (click)="startEditProfile()">
                    <i class="bi bi-pencil me-1"></i>Edit Profile
                  </button>
                  <button type="button" class="btn btn-link btn-signout" (click)="goHome()">
                    <i class="bi bi-box-arrow-right me-1"></i>Sign Out
                  </button>
                </div>
              }
            </div>
          </div>
        </div>
      }

    }
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamLoginStepComponent implements OnInit {
    /** Roles that are valid for team registration */
    private static readonly ALLOWED_ROLES: ReadonlySet<string> = new Set([Roles.ClubRep]);

    readonly loginSuccess = output<LoginStepResult>();
    readonly registrationSuccess = output<LoginStepResult>();

    readonly auth = inject(AuthService);
    private readonly clubService = inject(ClubService);
    private readonly state = inject(TeamWizardStateService);
    private readonly teamReg = inject(TeamRegistrationService);
    private readonly router = inject(Router);
    private readonly route = inject(ActivatedRoute);
    private readonly destroyRef = inject(DestroyRef);

    readonly error = signal<string | null>(null);
    readonly profileForEdit = signal<ClubRepProfileDto | null>(null);
    readonly loadingProfile = signal(false);

    /**
     * The visible view is DERIVED from auth state, not set imperatively, so it
     * self-corrects: an authenticated rep sees the "Club & Rep Info" review
     * (account-summary, which now hosts the inline edit form); a logged-out one
     * (incl. a failed token mint that logs us back out) falls back to the sign-in
     * gate automatically. `viewOverride` only carries the create sub-view.
     */
    private readonly viewOverride = signal<'create' | null>(null);
    readonly view = computed<LoginView>(() => {
        if (this.viewOverride() === 'create') return 'create';
        return this.auth.currentUser() ? 'account-summary' : 'sign-in';
    });

    /** Club this rep is registering — shown on the review screen for confirmation. */
    readonly registeringClub = this.state.clubRep.selectedClub;

    // Review is read-first: these flip an individual card into its inline edit form,
    // in place, without ever leaving the "Club & Rep Info" screen. A returning rep
    // rarely edits, so the default is a calm read-only display + a one-click Edit.
    readonly editingProfile = signal(false);
    readonly editingClub = signal(false);

    // ── Club name inline edit (allowed only before the club's first team) ──────
    readonly clubNameDraft = signal('');
    readonly savingClub = signal(false);
    readonly clubError = signal<string | null>(null);

    /**
     * Club name is editable only while the selected club has no registered teams
     * (IsInUse=false). That is exactly the data-safe window: with no teams, no
     * Registrations.club_name copies exist to diverge from the renamed Clubs row.
     * Once a team is registered the name locks (rename becomes an admin concern).
     */
    readonly clubEditable = computed(() => {
        const name = this.state.clubRep.selectedClub();
        if (!name) return false;
        const club = this.state.clubRep.availableClubs().find(c => c.clubName === name);
        return !!club && !club.isInUse;
    });

    /** Save is enabled only for a non-empty, actually-changed name. */
    readonly clubNameDirty = computed(() => {
        const draft = this.clubNameDraft().trim();
        return draft.length > 0 && draft !== (this.registeringClub() ?? '');
    });

    /** Open the inline create-account sub-view (from the sign-in gate). */
    showCreateAccount(): void { this.viewOverride.set('create'); }

    // ── Inline edit toggles (in-place; no view change) ────────────────────────
    startEditProfile(): void { this.editingProfile.set(true); }
    cancelEditProfile(): void { this.editingProfile.set(false); }
    startEditClub(): void { this.seedClubDraft(); this.editingClub.set(true); }
    cancelEditClub(): void { this.editingClub.set(false); this.clubError.set(null); }

    /** Seed the club-name draft from the selected club when landing on the review. */
    private seedClubDraft(): void {
        this.clubNameDraft.set(this.state.clubRep.selectedClub() ?? '');
        this.clubError.set(null);
    }

    /** Persist a club rename; updates wizard state so the new name flows everywhere. */
    saveClubName(): void {
        const current = this.registeringClub();
        const next = this.clubNameDraft().trim();
        if (!current || !next || next === current) return;

        this.savingClub.set(true);
        this.clubError.set(null);
        this.clubService.renameClub({ currentClubName: current, newClubName: next })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (resp) => {
                    this.savingClub.set(false);
                    if (resp.success && resp.newClubName) {
                        const renamed = resp.newClubName;
                        this.state.clubRep.setSelectedClub(renamed);
                        this.state.clubRep.setAvailableClubs(
                            this.state.clubRep.availableClubs().map(c =>
                                c.clubName === current ? { ...c, clubName: renamed } : c));
                        this.clubNameDraft.set(renamed);
                        this.editingClub.set(false);
                    } else {
                        this.clubError.set(resp.message ?? 'Could not rename club.');
                    }
                },
                error: (err: unknown) => {
                    this.savingClub.set(false);
                    const e = err as { error?: { message?: string } };
                    this.clubError.set(e?.error?.message ?? 'Could not rename club.');
                },
            });
    }

    ngOnInit(): void {
        if (this.auth.isAuthenticated()) {
            const user = this.auth.currentUser();
            const role = user?.role;
            if (role && !TeamLoginStepComponent.ALLOWED_ROLES.has(role)) {
                this.auth.logoutLocal();
                return;
            }
            // Two ways an authenticated club rep arrives on this tab — both land on the
            // "Club & Rep Info" review screen (account-summary):
            //   • Full wizard session (Phase-2 token: regId + matching jobPath) — they
            //     stepped back here, or deep-linked in; show the review directly.
            //   • Phase-1 token only — returning from the ToS page mid-login, before a
            //     Phase-2 token was minted (the login redirected to ToS BEFORE
            //     continueWithLogin() ran). Resume the login to mint the Phase-2 token;
            //     continueWithLogin() then flips to the same review screen on success.
            const hasFullSession = !!user?.regId && user.jobPath === this.state.jobPath();
            if (hasFullSession) {
                // view computes to account-summary (authenticated); just load the profile.
                this.seedClubDraft();
                this.loadProfile();
            } else {
                this.continueWithLogin();
            }
        }
    }

    returnUrl(): string {
        const jobPath = this.state.jobPath();
        if (!jobPath) return '/tsic/role-selection';
        // Preserve ?invite=<regId> so a ToS bounce-back re-enters through the invite
        // guard with the token intact — invite-only events would otherwise reject the
        // returning club rep.
        const invite = this.route.snapshot.queryParamMap.get('invite');
        const base = `/${jobPath}/registration/team`;
        return invite ? `${base}?invite=${encodeURIComponent(invite)}` : base;
    }

    /** Called by inline create form after ToS accepted — user is already authenticated. */
    onRegistered(): void {
        this.continueWithLogin();
    }

    onProfileSaved(): void {
        // Collapse back to the read-only display and refetch so it shows fresh values.
        this.editingProfile.set(false);
        this.loadProfile();
    }

    /**
     * Exit the wizard back to the public job landing page.
     * Always clears the session — "Back to home" from the Login tab signals
     * end-of-session intent, and Sign Out routes through here too so we don't
     * leave a half-cleared state behind on either path.
     */
    goHome(): void {
        this.auth.logoutLocal();
        this.viewOverride.set(null);
        this.profileForEdit.set(null);
        this.error.set(null);
        const jobPath = this.state.jobPath();
        if (jobPath) this.router.navigate(['/', jobPath]);
    }

    private loadProfile(onSuccess?: () => void): void {
        this.loadingProfile.set(true);
        this.clubService.getSelfProfile()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (profile) => {
                    this.profileForEdit.set(profile);
                    this.loadingProfile.set(false);
                    onSuccess?.();
                },
                error: () => {
                    this.loadingProfile.set(false);
                    // Stay on summary; display falls back to username.
                    onSuccess?.();
                },
            });
    }

    continueWithLogin(): void {
        this.error.set(null);

        // Guard: reject non-ClubRep logins (check role if present in token)
        const user = this.auth.currentUser();
        if (user?.role && !TeamLoginStepComponent.ALLOWED_ROLES.has(user.role)) {
            this.auth.logoutLocal();
            this.error.set('This is not a club rep account. Please sign in with a club rep account or create one below.');
            return;
        }

        this.teamReg.getMyClubs()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (clubs: ClubRepClubDto[]) => {
                    if (!clubs.length) {
                        this.auth.logoutLocal();
                        this.error.set('No clubs found for this account. This is not a club rep account, or your club has not been set up yet.');
                        return;
                    }
                    const clubName = clubs.length === 1 ? clubs[0].clubName : null;
                    this.loginSuccess.emit({ availableClubs: clubs, clubName });
                    // Land on the "Club & Rep Info" review screen for every authenticated
                    // arrival (fresh sign-in, ToS-return, create-account). `view` already
                    // computes to account-summary now that currentUser() is set; clear any
                    // create-sub-view override and load the rep's profile. The parent mints
                    // the Phase-2 token in the background; getSelfProfile() works on the
                    // Phase-1 token, so the identity renders immediately and the wizard
                    // Continue lights up once hasWizardSession() flips true.
                    this.viewOverride.set(null);
                    this.seedClubDraft();
                    this.loadProfile();
                },
                error: (err: unknown) => {
                    const httpErr = err as { status?: number };
                    console.error('[TeamLogin] Failed to load clubs', err);
                    this.auth.logoutLocal();
                    if (httpErr?.status === 403) {
                        this.error.set('This is not a club rep account. Please sign in with a club rep account or create one below.');
                    } else {
                        this.error.set('Failed to load your clubs. Please try again.');
                    }
                },
            });
    }
}
