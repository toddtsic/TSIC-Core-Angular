import {
  Component, OnInit, signal, computed, inject, ChangeDetectionStrategy
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  ChangePasswordService,
  ResetTarget,
  NO_EMAIL_SENTINEL,
  displayEmail,
  dobLabel,
  childKey
} from './services/change-password.service';
import { MergePanelComponent } from './components/merge-panel.component';
import { ToastService } from '@shared-ui/toast.service';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { PhonePipe } from '@infrastructure/pipes/phone.pipe';
import type {
  ChangePasswordSearchResultDto,
  ChangePasswordRoleOptionDto,
  MergeCandidateDto,
  MergeCandidatesResponse,
  ResetContextDto
} from '@core/api';

// Mirrors MaxAccounts in ChangePasswordRepository — the server caps the search at this many login
// accounts, so hitting it exactly is how we know the results were cut.
const MAX_ACCOUNTS = 50;

const MIN_PASSWORD_LENGTH = 6;

/**
 * SuperUser account repair. THREE missions, and the screen is built around them:
 *
 *   1. Change a password.
 *   2. Merge duplicate logins — a household, or an adult IN THE SAME ROLE.
 *   3. See where a person is registered.
 *
 * Mission 3 is not a third screen. It is the EVIDENCE the other two consume, and it is the table:
 * one row per REGISTRATION, exactly as legacy's grid was, because "which club, which event" is how
 * you find the person a caller is describing. Ann uses this tool; anything legacy showed her is here.
 *
 * The two things that will bite anyone who edits this file:
 *
 *   A PLAYER HAS NO USABLE LOGIN. The row is about Maya; the password is her mother's. Legacy shipped
 *   a "new player password" field that reset a credential nobody can sign in with. There is no
 *   player-password affordance here — absent, not disabled — and the button says FAMILY, so the label
 *   itself names what changes.
 *
 *   A MERGE IS NOT A RENAME. It re-points one login's registrations onto another, irreversibly, across
 *   customers. So the dialog shows the identity block on both sides (that IS the security key, shown),
 *   the children aligned, and every registration that will move. One retirement per act, never a list.
 */
@Component({
  selector: 'app-change-password',
  standalone: true,
  imports: [CommonModule, FormsModule, TsicDialogComponent, MergePanelComponent, PhonePipe],
  templateUrl: './change-password.component.html',
  styleUrl: './change-password.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ChangePasswordComponent implements OnInit {
  private readonly api = inject(ChangePasswordService);
  private readonly toast = inject(ToastService);

  readonly minPasswordLength = MIN_PASSWORD_LENGTH;

  // ── Mission 3: the search, and the table it fills ────────────────────────────

  readonly roleOptions = signal<ChangePasswordRoleOptionDto[]>([]);

  // Legacy's nine criteria, unchanged. Generous and fuzzy on purpose: for a player, name / email /
  // phone each fan out across the CHILD, MOM and DAD — you search for the kid by whichever parent
  // called you. The search is an ANCHOR, not a census: it only has to find ONE true row, and the
  // identity key expands from there to every duplicate. Do not make it stricter to compensate.
  readonly roleId = signal('');
  readonly firstName = signal('');
  readonly lastName = signal('');
  readonly customerName = signal('');
  readonly jobName = signal('');
  readonly email = signal('');
  readonly phone = signal('');
  readonly familyUserName = signal('');
  readonly userName = signal('');

  readonly rows = signal<ChangePasswordSearchResultDto[]>([]);
  readonly searching = signal(false);
  readonly searched = signal(false);

  readonly canSearch = computed(() => !!this.roleId() && !this.searching());

  /** Distinct logins in the result set — what the server's cap actually bounds. */
  readonly accountCount = computed(() =>
    new Set(this.rows().map(r => this.loginOf(r))).size);

  readonly truncated = computed(() => this.accountCount() >= MAX_ACCOUNTS);

  // ── Mission 1: change a password ─────────────────────────────────────────────

  readonly resetRow = signal<ChangePasswordSearchResultDto | null>(null);
  readonly resetContext = signal<ResetContextDto | null>(null);
  readonly resetLoading = signal(false);
  readonly newPassword = signal('');
  readonly resetting = signal(false);

  readonly passwordTooShort = computed(() =>
    this.newPassword().length > 0 && this.newPassword().length < MIN_PASSWORD_LENGTH);

  readonly canReset = computed(() =>
    !!this.resetContext()
    && this.newPassword().length >= MIN_PASSWORD_LENGTH
    && !this.resetting());

  // ── Mission 2: merge ─────────────────────────────────────────────────────────

  readonly mergeRow = signal<ChangePasswordSearchResultDto | null>(null);
  readonly mergeData = signal<MergeCandidatesResponse | null>(null);
  readonly mergeLoading = signal(false);
  readonly merging = signal(false);

  readonly keepUserName = signal('');
  readonly retireUserName = signal('');

  readonly mergeAccounts = computed<MergeCandidateDto[]>(() => this.mergeData()?.accounts ?? []);

  readonly keepAccount = computed(() =>
    this.mergeAccounts().find(a => a.userName === this.keepUserName()) ?? null);

  readonly retireAccount = computed(() =>
    this.mergeAccounts().find(a => a.userName === this.retireUserName()) ?? null);

  readonly isFamilyMerge = computed(() => !!this.mergeRow()?.familyUserName);

  readonly canMerge = computed(() => {
    const keep = this.keepAccount();
    const retire = this.retireAccount();
    return !!keep && !!retire && keep.userId !== retire.userId && !this.merging();
  });

  /**
   * The children a merge will FUSE, keyed exactly as the server keys them
   * (`ChangePasswordRepository.BuildChildCollapseAsync`): a child collapses only when BOTH sides hold
   * exactly ONE row for that (name, DOB).
   *
   * Two rows on either side is ambiguous — it is what a deliberate double-registration looks like, a
   * second player row created to get past an event's one-per-player rule — so the server leaves BOTH
   * alone. Marking such a child "merges" here would be a lie about what the button does.
   */
  readonly collapsingChildKeys = computed<Set<string>>(() => {
    const keep = this.keepAccount();
    const retire = this.retireAccount();
    const collapsing = new Set<string>();
    if (!keep || !retire) return collapsing;

    const countBy = (account: MergeCandidateDto) => {
      const counts = new Map<string, number>();
      for (const child of account.children) {
        const key = childKey(child.name, child.dob);
        counts.set(key, (counts.get(key) ?? 0) + 1);
      }
      return counts;
    };

    const onKeep = countBy(keep);
    for (const [key, count] of countBy(retire)) {
      if (count === 1 && onKeep.get(key) === 1) collapsing.add(key);
    }
    return collapsing;
  });

  /** The children named in the consequence sentence — "and merges Maya and Ethan onto mabell2025". */
  readonly collapsingChildNames = computed<string[]>(() => {
    const retire = this.retireAccount();
    if (!retire) return [];
    return retire.children
      .filter(c => this.isCollapsing(c.name, c.dob))
      .map(c => c.name);
  });

  // ── Contacts ─────────────────────────────────────────────────────────────────
  //
  // Not a fourth mission — it is what UNBLOCKS the second one. The merge key is the mother's email,
  // phone and name; a typo in any of them means the duplicate login is never offered, and the tool
  // stares blankly at a household it plainly cannot identify. So the admin gets to fix it.
  //
  // Phone is NOT editable, and that is not an oversight: legacy's grid marked the phones editable but
  // never bound them — they were posted and silently discarded, and it never once worked. Contract §3
  // #13. Bind them for real, or leave them off; do not ship the pretence.

  readonly contactsRow = signal<ChangePasswordSearchResultDto | null>(null);
  readonly savingContacts = signal(false);

  readonly cFamilyEmail = signal('');
  readonly cMomEmail = signal('');
  readonly cDadEmail = signal('');
  readonly cUserEmail = signal('');

  readonly cNoFamilyEmail = signal(false);
  readonly cNoMomEmail = signal(false);
  readonly cNoDadEmail = signal(false);
  readonly cNoUserEmail = signal(false);

  ngOnInit(): void {
    this.api.getRoleOptions().subscribe({
      next: options => {
        this.roleOptions.set(options);
        if (!this.roleId() && options.length) this.roleId.set(options[0].roleId);
      },
      error: () => this.toast.show('Could not load the role list.', 'danger')
    });
  }

  // ── Search ───────────────────────────────────────────────────────────────────

  search(): void {
    if (!this.canSearch()) return;

    this.searching.set(true);

    this.api.search({
      roleId: this.roleId(),
      firstName: this.blank(this.firstName()),
      lastName: this.blank(this.lastName()),
      customerName: this.blank(this.customerName()),
      jobName: this.blank(this.jobName()),
      email: this.blank(this.email()),
      phone: this.blank(this.phone()),
      familyUserName: this.blank(this.familyUserName()),
      userName: this.blank(this.userName())
    }).subscribe({
      next: results => {
        this.rows.set(results);
        this.searched.set(true);
        this.searching.set(false);
      },
      error: err => {
        this.searching.set(false);
        this.toast.show(err?.error?.message ?? 'Search failed.', 'danger');
      }
    });
  }

  clear(): void {
    this.firstName.set('');
    this.lastName.set('');
    this.customerName.set('');
    this.jobName.set('');
    this.email.set('');
    this.phone.set('');
    this.familyUserName.set('');
    this.userName.set('');
    this.rows.set([]);
    this.searched.set(false);
  }

  // ── Change password ──────────────────────────────────────────────────────────

  /**
   * The row is a REGISTRATION. Which ACCOUNT it resolves to is the server's answer, not a cell in the
   * grid — and that resolution is the whole thing the dialog exists to show, so it is fetched, never
   * inferred from the row the cursor happened to be on.
   */
  openReset(row: ChangePasswordSearchResultDto): void {
    this.resetRow.set(row);
    this.resetContext.set(null);
    this.newPassword.set('');
    this.resetLoading.set(true);

    const target = this.isPlayer(row) ? ResetTarget.Family : ResetTarget.User;

    this.api.getResetContext(row.registrationId, target).subscribe({
      next: context => {
        this.resetContext.set(context);
        this.resetLoading.set(false);
      },
      error: err => {
        this.resetLoading.set(false);
        this.closeReset();
        this.toast.show(err?.error?.message ?? 'Could not resolve the account for this row.', 'danger');
      }
    });
  }

  closeReset(): void {
    this.resetRow.set(null);
    this.resetContext.set(null);
    this.newPassword.set('');
  }

  confirmReset(): void {
    const row = this.resetRow();
    const context = this.resetContext();
    if (!row || !context || !this.canReset()) return;

    this.resetting.set(true);

    this.api.resetPassword(row.registrationId, {
      target: context.target,
      newPassword: this.newPassword(),
      // What the UI BELIEVES it is resetting. The server re-resolves the account from the
      // registration's FK and rejects a disagreement — a guard against a stale grid, not the
      // targeting mechanism.
      expectedUserName: context.userName
    }).subscribe({
      next: res => {
        this.resetting.set(false);
        this.closeReset();
        this.toast.show(res.message, 'success');
      },
      error: err => {
        this.resetting.set(false);
        this.toast.show(err?.error?.message ?? 'The password could not be changed.', 'danger');
      }
    });
  }

  copyPassword(): void {
    const password = this.newPassword();
    if (!password) return;

    navigator.clipboard?.writeText(password).then(
      () => this.toast.show('Password copied.', 'info', 2000),
      () => this.toast.show('Could not copy — read it from the field.', 'warning')
    );
  }

  // ── Merge ────────────────────────────────────────────────────────────────────

  openMerge(row: ChangePasswordSearchResultDto): void {
    this.mergeRow.set(row);
    this.mergeData.set(null);
    this.keepUserName.set('');
    this.retireUserName.set('');
    this.mergeLoading.set(true);

    const candidates$ = this.isPlayer(row)
      ? this.api.getFamilyMergeCandidates(row.registrationId)
      : this.api.getUserMergeCandidates(row.registrationId);

    candidates$.subscribe({
      next: data => {
        this.mergeData.set(data);
        this.seedPair(data, this.loginOf(row));
        this.mergeLoading.set(false);
      },
      error: err => {
        this.mergeLoading.set(false);
        this.closeMerge();
        this.toast.show(err?.error?.message ?? 'Could not load merge candidates.', 'danger');
      }
    });
  }

  /**
   * Open on the pair the admin is most likely looking at — the login the row came from, and one other.
   * Both are dropdowns and NOTHING happens until the consequence is read and the button clicked. The
   * point of seeding is that both panels render fully, because comparing them side by side IS the
   * decision.
   */
  private seedPair(data: MergeCandidatesResponse, rowLogin: string): void {
    const accounts = data.accounts;
    if (accounts.length < 2) return;

    const keep = accounts.find(a => a.userName === rowLogin) ?? accounts[0];
    const retire = accounts.find(a => a.userId !== keep.userId);
    if (!retire) return;

    this.keepUserName.set(keep.userName);
    this.retireUserName.set(retire.userName);
  }

  /** The two sides can never be the same account: picking one pushes the other out of the way. */
  onKeepChange(userName: string): void {
    this.keepUserName.set(userName);
    if (this.retireUserName() === userName) {
      const other = this.mergeAccounts().find(a => a.userName !== userName);
      this.retireUserName.set(other?.userName ?? '');
    }
  }

  onRetireChange(userName: string): void {
    this.retireUserName.set(userName);
    if (this.keepUserName() === userName) {
      const other = this.mergeAccounts().find(a => a.userName !== userName);
      this.keepUserName.set(other?.userName ?? '');
    }
  }

  closeMerge(): void {
    this.mergeRow.set(null);
    this.mergeData.set(null);
    this.keepUserName.set('');
    this.retireUserName.set('');
  }

  confirmMerge(): void {
    const row = this.mergeRow();
    const keep = this.keepAccount();
    const retire = this.retireAccount();
    if (!row || !keep || !retire || !this.canMerge()) return;

    this.merging.set(true);

    const request = { keepUserName: keep.userName, retireUserName: retire.userName };

    const merge$ = this.isFamilyMerge()
      ? this.api.mergeFamilyUsername(row.registrationId, request)
      : this.api.mergeUsername(row.registrationId, request);

    merge$.subscribe({
      next: res => {
        this.merging.set(false);
        this.closeMerge();
        this.toast.show(res.message, 'success');
        // The retired login now owns nothing, so the rows on screen have moved. Re-run the search
        // rather than patch the grid — the truth is on the server.
        this.search();
      },
      error: err => {
        this.merging.set(false);
        this.toast.show(err?.error?.message ?? 'The merge failed. Nothing was changed.', 'danger');
      }
    });
  }

  // ── Edit contacts ────────────────────────────────────────────────────────────

  openContacts(row: ChangePasswordSearchResultDto): void {
    this.contactsRow.set(row);

    this.seedEmail(row.familyEmail, this.cFamilyEmail, this.cNoFamilyEmail);
    this.seedEmail(row.momEmail, this.cMomEmail, this.cNoMomEmail);
    this.seedEmail(row.dadEmail, this.cDadEmail, this.cNoDadEmail);
    this.seedEmail(row.email, this.cUserEmail, this.cNoUserEmail);
  }

  closeContacts(): void {
    this.contactsRow.set(null);
  }

  saveContacts(): void {
    const row = this.contactsRow();
    if (!row || this.savingContacts()) return;

    this.savingContacts.set(true);

    // PATCH semantics on the wire: a NULL field means "leave it alone", an EMPTY STRING means "clear
    // the address". Every box is sent, so every box is authoritative — what is on screen is what is
    // stored. Collapsing the two is what made a stale address unremovable (5a121a2c).
    const save$ = this.isPlayer(row)
      ? this.api.updateFamilyEmails(row.registrationId, {
          familyEmail: this.emailToSend(this.cFamilyEmail(), this.cNoFamilyEmail()),
          momEmail: this.emailToSend(this.cMomEmail(), this.cNoMomEmail()),
          dadEmail: this.emailToSend(this.cDadEmail(), this.cNoDadEmail())
        })
      : this.api.updateUserEmail(row.registrationId, {
          email: this.emailToSend(this.cUserEmail(), this.cNoUserEmail())
        });

    save$.subscribe({
      next: res => {
        this.savingContacts.set(false);
        this.closeContacts();
        this.toast.show(res.message, 'success');
        this.search();
      },
      error: err => {
        this.savingContacts.set(false);
        this.toast.show(err?.error?.message ?? 'The contact details could not be saved.', 'danger');
      }
    });
  }

  /**
   * `not@given.com` is a recorded fact — "we asked, there isn't one" — and it is distinct from a blank,
   * which only says we never captured one. The toggle carries the fact; the box carries the address.
   *
   * The marker is written by the toggle and never typed. A marker an admin types is a marker an admin
   * typos, and a typo'd marker is just a live address that bounces.
   */
  private seedEmail(
    stored: string | null | undefined,
    box: { set(v: string): void },
    noEmail: { set(v: boolean): void }
  ): void {
    const isMarker = (stored ?? '').trim().toLowerCase() === NO_EMAIL_SENTINEL;
    noEmail.set(isMarker);
    box.set(isMarker ? '' : (stored ?? ''));
  }

  private emailToSend(value: string, noEmail: boolean): string {
    return noEmail ? NO_EMAIL_SENTINEL : value.trim();
  }

  // ── Row helpers ──────────────────────────────────────────────────────────────

  /**
   * A player row is one that has a FAMILY login. Keyed on the data rather than the role name: it is
   * the presence of that second account that makes the password — and the merge — the household's.
   */
  isPlayer(row: ChangePasswordSearchResultDto): boolean {
    return !!row.familyUserName;
  }

  /** The login this row's actions act on — the family's for a player, their own for an adult. */
  loginOf(row: ChangePasswordSearchResultDto): string {
    return row.familyUserName || row.userName;
  }

  personOf(row: ChangePasswordSearchResultDto): string {
    return `${row.firstName ?? ''} ${row.lastName ?? ''}`.trim() || '(no name)';
  }

  /** `not@given.com` is a flag, not an address — never rendered as one. See the service. */
  showEmail(email: string | null | undefined): string {
    return displayEmail(email);
  }

  /** `2008-03-28`, straight off the wire — no date pipe, no timezone shift. See the service. */
  dob(value: string | null | undefined): string {
    return dobLabel(value);
  }

  isCollapsing(name: string, dob: string | null | undefined): boolean {
    return this.collapsingChildKeys().has(childKey(name, dob));
  }

  trackRow = (_: number, row: ChangePasswordSearchResultDto) => row.registrationId;
  trackReach = (index: number) => index;

  private blank(value: string): string | undefined {
    const trimmed = value.trim();
    return trimmed.length ? trimmed : undefined;
  }
}
