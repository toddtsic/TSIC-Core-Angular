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
import { RoleIds } from '@infrastructure/constants/roles.constants';
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
 * The contacts a PLAYER row can edit are the household's — mother and father, on the `Families` row.
 * There is deliberately no family-login field: the family login IS the mother, so the server brings it
 * to parity with her rather than letting an admin type into it separately and drift the two apart.
 *
 * An ADULT is their own account, so `user*` writes straight to their `AspNetUsers` row.
 */
type ContactFieldKey =
  | 'momEmail' | 'momPhone'
  | 'dadEmail' | 'dadPhone'
  | 'userEmail' | 'userPhone';

/**
 * One editable address in the contacts dialog.
 *
 * `optedOut` is NOT "there is no address". It is `not@given.com` — the marker legacy's
 * `EmailOptOutController` stamps on every row carrying a person's address when they ask us to stop
 * mailing them. So it is a DECISION on file, and this tool treats it as one: shown, never written,
 * never silently cleared, and replaceable only on a deliberate click. Contract §1.
 */
interface ContactField {
  key: ContactFieldKey;

  /** Phones have no opt-out — `not@given.com` is an EMAIL marker and there is no phone equivalent. */
  kind: 'email' | 'phone';

  label: string;
  value: string;

  /**
   * What was on file when the dialog opened. An UNTOUCHED field sends `null` — "leave it alone" — and
   * that is load-bearing, not tidiness: saving Mom's email is what mirrors the family LOGIN's address.
   * Without this, Ann fixing the FATHER's phone would re-send Mom's email unchanged, fire the mirror,
   * and silently re-point the password-reset address of the 23,916 households whose login has drifted
   * from `Mom_Email`. An edit does what it says and nothing else.
   */
  original: string;

  optedOut: boolean;
  replacing: boolean;
}

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

  /**
   * At least one of the eight text criteria is typed. The ROLE does not count: it is preselected the
   * moment the role list lands (ngOnInit), so a gate that accepted it would never close.
   *
   * Every criterion is optional on the server — it applies the ones it is given and caps the rest — so a
   * role-only search is not a narrow query, it is the first MaxRows registrations of that role ACROSS
   * EVERY CUSTOMER AND JOB. That is not a result Ann can use: the caller's name is somewhere in it, or
   * it is not, and there is no way to tell which. The search is an anchor. Make it anchor to something.
   */
  readonly hasCriteria = computed(() =>
    [this.firstName(), this.lastName(), this.customerName(), this.jobName(),
     this.email(), this.phone(), this.familyUserName(), this.userName()]
      .some(v => v.trim().length > 0));

  readonly canSearch = computed(() => !!this.roleId() && this.hasCriteria() && !this.searching());

  /**
   * Is the SELECTED role Player? Family username is the one criterion that only a player has — a player
   * owns no login, so the account they sign in with is a parent's. Every other role signs in as
   * themselves and has no family login to be searched by.
   *
   * Keyed on the role GUID, never the display name, and compared case-insensitively: the backend's GUID
   * casing varies (see roles.constants.ts). The role list this tool serves is real role IDs only — no
   * synthetic filter sentinels — so a straight equality check is the whole test.
   */
  readonly isPlayerRole = computed(() => this.roleId().toUpperCase() === RoleIds.Player);

  /** Distinct logins in the result set — what the server's cap actually bounds. */
  readonly accountCount = computed(() =>
    new Set(this.rows().map(r => this.loginOf(r))).size);

  readonly truncated = computed(() => this.accountCount() >= MAX_ACCOUNTS);

  /**
   * Logins in these results that HAVE a duplicate. The server counted them (against the whole database
   * — the search is an anchor, not a census), so the Merge button only appears where a merge is
   * actually possible, and nobody opens the dialog to be told it is empty.
   */
  readonly mergeableCount = computed(() =>
    new Set(this.rows().filter(r => r.mergeCandidateCount > 0).map(r => this.loginOf(r))).size);

  /**
   * Family login, Mother and Father are a PLAYER's columns. The family signs in and selects the child,
   * and the parents live on the household record; an adult signs in as themselves and has none of it —
   * the adult projection in `ChangePasswordRepository` never populates those ten fields, so a Club Rep
   * search renders ten columns of em-dashes across a table that already has to fit on one screen.
   *
   * Gated on "is there a player in these results", NOT on "is this cell empty". The difference matters:
   * a single-mother household has no Father, and Ann must still see the Father columns standing empty —
   * that emptiness is DATA ("no father on file"). It is only when nothing on screen is a household at
   * all that the columns are structurally N/A and come off.
   */
  readonly showHousehold = computed(() => this.rows().some(r => this.isHousehold(r)));

  /** Household-shaped = carries family or parent data at all, which only a player's registration does. */
  private isHousehold(r: ChangePasswordSearchResultDto): boolean {
    return !!(r.familyUserName || r.familyEmail
      || r.momFirstName || r.momLastName || r.momEmail || r.momPhone
      || r.dadFirstName || r.dadLastName || r.dadEmail || r.dadPhone);
  }

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
  readonly contactFields = signal<ContactField[]>([]);
  readonly savingContacts = signal(false);

  /** Save is live only when something actually changed. See `pending`. */
  readonly canSaveContacts = computed(() =>
    !this.savingContacts()
    && this.contactFields().some(f => this.pending(f) !== null));

  /**
   * True when this save will also move the FAMILY LOGIN. The login is the mother, so changing her email
   * or phone changes the address her password reset goes to — and the dialog says so before she saves,
   * rather than letting her find out afterwards.
   */
  readonly mirroringLogin = computed(() =>
    this.contactFields().some(f =>
      (f.key === 'momEmail' || f.key === 'momPhone') && this.pending(f) !== null));

  ngOnInit(): void {
    this.api.getRoleOptions().subscribe({
      next: options => {
        this.roleOptions.set(options);
        if (!this.roleId() && options.length) this.setRole(options[0].roleId);
      },
      error: () => this.toast.show('Could not load the role list.', 'danger')
    });
  }

  // ── Search ───────────────────────────────────────────────────────────────────

  /**
   * Changing the role away from Player takes the Family-username box off the panel — so it also has to
   * take the VALUE with it. A criterion that is still in the request but no longer on screen is a filter
   * nobody can see: Ann types a family username, switches to Club Rep, searches, and gets nothing, with
   * no field on the panel to explain why.
   */
  setRole(roleId: string): void {
    this.roleId.set(roleId);
    if (!this.isPlayerRole()) this.familyUserName.set('');
  }

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

  /**
   * The two sides can never be the same account: marking one pushes the other out of the way.
   *
   * The candidates are a LIST, not two dropdowns. A `<select>` renders plain text, and half the family
   * usernames here are raw GUIDs — so a dropdown makes the admin pick blind, read the panel, discover
   * they picked the wrong one, and pick again. The list shows the mother, the counts and the username
   * on every row, so the CHOICE is informed, not just the confirmation.
   */
  pickKeep(userName: string): void {
    this.keepUserName.set(userName);
    if (this.retireUserName() === userName) {
      const other = this.mergeAccounts().find(a => a.userName !== userName);
      this.retireUserName.set(other?.userName ?? '');
    }
  }

  pickRetire(userName: string): void {
    this.retireUserName.set(userName);
    if (this.keepUserName() === userName) {
      const other = this.mergeAccounts().find(a => a.userName !== userName);
      this.keepUserName.set(other?.userName ?? '');
    }
  }

  /** The mother, or the adult themselves — whichever this candidate's identity actually is. */
  ownerOf(account: MergeCandidateDto): string {
    return account.momName || account.personName || '(no name)';
  }

  ownerEmailOf(account: MergeCandidateDto): string {
    return displayEmail(account.momEmail ?? account.email);
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

    this.contactFields.set(this.isPlayer(row)
      ? [
          this.emailField('momEmail', "Mother's email", row.momEmail),
          this.phoneField('momPhone', "Mother's phone", row.momPhone),
          this.emailField('dadEmail', "Father's email", row.dadEmail),
          this.phoneField('dadPhone', "Father's phone", row.dadPhone)
        ]
      : [
          this.emailField('userEmail', 'Email', row.email),
          this.phoneField('userPhone', 'Phone', row.phone)
        ]);
  }

  closeContacts(): void {
    this.contactsRow.set(null);
    this.contactFields.set([]);
  }

  /**
   * A phone field holds DIGITS and nothing else. That is why the input can be dumb and the display can
   * be pretty: what is stored is the number, and `PhonePipe` renders it `973-876-3216`. No parsing a
   * format back out, no half-typed punctuation reaching the database.
   */
  setContact(key: ContactFieldKey, value: string): void {
    this.contactFields.update(fields =>
      fields.map(f => (f.key === key
        ? { ...f, value: f.kind === 'phone' ? value.replace(/\D/g, '') : value }
        : f)));
  }

  /**
   * Un-opt-out somebody. It is a real decision — they asked us to stop mailing them, and typing an
   * address here puts them back on the list — so it takes a deliberate click, and it can never happen
   * as a side effect of an admin tabbing through the form.
   */
  startReplace(key: ContactFieldKey): void {
    this.contactFields.update(fields =>
      fields.map(f => (f.key === key ? { ...f, replacing: true, value: '' } : f)));
  }

  saveContacts(): void {
    const row = this.contactsRow();
    if (!row || !this.canSaveContacts()) return;

    this.savingContacts.set(true);

    const save$ = this.isPlayer(row)
      ? this.api.updateFamilyContacts(row.registrationId, {
          momEmail: this.toSend('momEmail'),
          momCellphone: this.toSend('momPhone'),
          dadEmail: this.toSend('dadEmail'),
          dadCellphone: this.toSend('dadPhone')
        })
      : this.api.updateUserContact(row.registrationId, {
          email: this.toSend('userEmail'),
          cellphone: this.toSend('userPhone')
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

  private emailField(
    key: ContactFieldKey,
    label: string,
    stored: string | null | undefined
  ): ContactField {
    // `not@given.com` is an OPT-OUT, written by one place in the system (legacy's EmailOptOutController)
    // when a person asks to be removed from email. It is a flag, not an address — never shown as one,
    // and never typed. Contract §1.
    const optedOut = (stored ?? '').trim().toLowerCase() === NO_EMAIL_SENTINEL;
    const value = optedOut ? '' : (stored ?? '').trim();
    return { key, kind: 'email', label, value, original: value, optedOut, replacing: false };
  }

  /**
   * Phones are held as DIGITS. The stored value may be `973-876-3216` or `1 (516) 551-1969`; both reduce
   * to the same digits, and `original` is the reduced form — so a field the admin never touched compares
   * equal and sends nothing. Opening the dialog can never silently reformat a number.
   */
  private phoneField(
    key: ContactFieldKey,
    label: string,
    stored: string | null | undefined
  ): ContactField {
    const value = (stored ?? '').replace(/\D/g, '');
    return { key, kind: 'phone', label, value, original: value, optedOut: false, replacing: false };
  }

  /** An opt-out the admin has not chosen to replace has nothing to save. Phones have no opt-out. */
  private isSendable(field: ContactField): boolean {
    return field.kind === 'phone' || !field.optedOut || field.replacing;
  }

  /**
   * What this field will actually send. `null` means LEAVE IT ALONE; `''` means CLEAR IT. The server
   * honours both, and collapsing them is what made a stale address unremovable (`5a121a2c`).
   *
   * Two things return `null`, and both are load-bearing:
   *
   *   AN UNTOUCHED OPT-OUT. Sending `''` would clear the `not@given.com` marker and put somebody who
   *   asked to be left alone straight back on the mailing list — silently, with nobody deciding to.
   *
   *   AN UNTOUCHED FIELD. Saving Mom's email is what mirrors the family LOGIN's address. If an
   *   unchanged Mom email were re-sent every time Ann fixed the FATHER's phone, the mirror would fire
   *   and re-point the password-reset address for the 23,916 households whose login has drifted from
   *   `Mom_Email` — as a side effect of an edit that had nothing to do with her.
   */
  private pending(field: ContactField): string | null {
    if (!this.isSendable(field)) return null;

    const value = field.value.trim();
    return value === field.original ? null : value;
  }

  private toSend(key: ContactFieldKey): string | null {
    const field = this.contactFields().find(f => f.key === key);
    return field ? this.pending(field) : null;
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

  /**
   * Job names are STORED as `{Customer}:{Event}` — `Lax For The Cure:Fall Showcase 2024`. The Job cell
   * printed that whole string AND `customerName` underneath, so it said the customer twice and stood
   * three lines tall, which cost roughly 40% of the rows that fit on screen. Show the EVENT: down a
   * column of one family's 244 registrations it is the only part that changes.
   *
   * The prefix is only stripped when it demonstrably IS the customer (`ISP Event Center` covers the
   * `ISP:` prefix). A job whose name merely happens to contain a colon is left whole.
   */
  eventLabel(row: ChangePasswordSearchResultDto): string {
    const name = (row.jobName ?? '').trim();
    const customer = (row.customerName ?? '').trim();
    const colon = name.indexOf(':');
    if (colon <= 0 || !customer) return name;

    const prefix = name.slice(0, colon).trim();
    const isCustomer = customer.localeCompare(prefix, undefined, { sensitivity: 'accent' }) === 0
      || customer.toLowerCase().startsWith(prefix.toLowerCase());

    return isCustomer ? name.slice(colon + 1).trim() || name : name;
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
