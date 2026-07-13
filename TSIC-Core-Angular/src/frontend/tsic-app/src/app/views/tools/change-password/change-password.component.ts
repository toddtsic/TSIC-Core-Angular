import {
  Component, OnInit, signal, computed, inject, ChangeDetectionStrategy, type WritableSignal
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  ChangePasswordService,
  ResetTarget,
  NO_EMAIL_SENTINEL,
  type ApiMessage
} from './services/change-password.service';
import { ToastService } from '@shared-ui/toast.service';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { PhonePipe } from '@infrastructure/pipes/phone.pipe';
import type {
  ChangePasswordSearchResultDto,
  ChangePasswordRoleOptionDto,
  MergeCandidateDto,
  MergeCandidatesResponse,
  MergeUsernameRequest
} from '@core/api';

// Mirrors MaxAccounts in ChangePasswordRepository — the server caps the search at this many
// login accounts, so hitting it exactly is how we know results were truncated.
const MAX_ACCOUNTS = 50;

/**
 * View-model for one LOGIN account — the thing whose password we actually reset.
 *
 * The backend search is registration-grained (one row per event a person is in). We rebuild the
 * three levels those flat rows encode:
 *
 *   account (the login)  →  player  →  registration
 *
 * Each field lands at the level where it ACTUALLY VARIES. That is the whole design rule here, and
 * it is the one the first version broke: the family credentials and the parents belong to the
 * ACCOUNT, so they are stored here — not restamped onto every child. Non-player roles have no
 * middle level (the account IS the person), so `players` is empty and `registrations` is read
 * directly.
 */
interface LoginAccount {
  key: string;
  loginUserName: string;
  isFamilyLogin: boolean;
  displayName: string;
  email: string | null;
  phone: string | null;
  roleName: string;
  anyRegId: string;

  // The household — constant across every player under this login, so it lives HERE, once.
  momName: string | null;
  momPhone: string | null;
  dadName: string | null;
  dadPhone: string | null;

  players: PlayerRow[];
  registrations: ChangePasswordSearchResultDto[];
}

/**
 * One child under a family login. Keyed on the player's OWN username — never the display name, or
 * two kids sharing a nickname would collapse into one row.
 *
 * Only fields that VARY between siblings live here. Role, family credentials and the parents do
 * not vary, so they are on the account above.
 */
interface PlayerRow {
  key: string;
  firstName: string;
  lastName: string;
  userName: string;
  email: string | null;
  phone: string | null;
  registrations: ChangePasswordSearchResultDto[];
}

@Component({
  selector: 'app-change-password',
  standalone: true,
  imports: [CommonModule, FormsModule, TsicDialogComponent, PhonePipe],
  templateUrl: './change-password.component.html',
  styleUrl: './change-password.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ChangePasswordComponent implements OnInit {
  private readonly service = inject(ChangePasswordService);
  private readonly toast = inject(ToastService);

  // ── Role options ──
  roleOptions = signal<ChangePasswordRoleOptionDto[]>([]);
  selectedRoleId = signal('');

  // ── Search filters ──
  customerName = signal('');
  jobName = signal('');
  lastName = signal('');
  firstName = signal('');
  email = signal('');
  phone = signal('');
  userName = signal('');
  familyUserName = signal('');

  // ── Results (deduped to login accounts) ──
  accounts = signal<LoginAccount[]>([]);
  isSearching = signal(false);
  hasSearched = signal(false);
  resultsCapped = signal(false);

  // ── Expanded account (only one open at a time) ──
  expandedKey = signal<string | null>(null);

  // ── Expanded players within the open account (any number open) ──
  expandedPlayerKeys = signal<ReadonlySet<string>>(new Set());

  // Key of the account whose username was just copied — flips its icon to a check briefly.
  copiedKey = signal<string | null>(null);

  // ── Reset password (transient, for the open account) ──
  newPassword = signal('');
  isResetting = signal(false);

  // ── Email edit (transient, for the open account) ──
  editingEmail = signal('');        // non-family: the person's own email
  editingFamilyEmail = signal('');  // family: F-Email
  editingMomEmail = signal('');
  editingDadEmail = signal('');
  isSavingEmail = signal(false);

  /** Bound to the "No email" buttons, so the marker is written rather than typed. */
  readonly noEmail = NO_EMAIL_SENTINEL;

  /**
   * `not@given.com` is a FLAG, not an address — never render it as one. Legacy stripped it on the way
   * to the screen (`MomEmail.Replace("not@given.com", "")`) and so do we: the box shows empty and the
   * "No email" button carries the state.
   */
  shownEmail(value: string | null | undefined): string {
    const s = (value ?? '').trim();
    return s.toLowerCase() === this.noEmail ? '' : s;
  }

  hasNoEmail(value: string | null | undefined): boolean {
    return (value ?? '').trim().toLowerCase() === this.noEmail;
  }

  /**
   * Three states, one control. Off → on writes the marker; on → off clears it (which saves as NULL).
   * Typing an address in the box turns it off by itself, because the box owns the same signal.
   */
  toggleNoEmail(field: WritableSignal<string>): void {
    field.set(this.hasNoEmail(field()) ? '' : this.noEmail);
  }

  // ── Per-player editor ──
  // Editable: R-Email. NOTHING else.
  //
  // No password: a player has no usable login — the family signs in and picks the child, so a
  // player's own credentials are vestigial (half of them are raw GUIDs).
  //
  // No username-as-merge either, which USED to be here. A child is not an account you merge; a child
  // is collapsed as part of their household's merge, inside that boundary. The old dropdown ran on a
  // global name+DOB+role sweep that reached across every customer in the system.
  editPlayer = signal<PlayerRow | null>(null);
  editEmail = signal('');
  isSavingPlayer = signal(false);

  // ── Merge ──
  //
  // The parent phones in, says "put everything on <username>", and the SuperUser finds that account
  // in this list and picks it. So the modal is NOT "choose a target for the source" — it is:
  //
  //     pick the SURVIVOR   (radio — the account the parent named)
  //     check what FOLDS IN (checkbox — every other login that keys to the same person)
  //
  // Every account here already passed the identity key server-side (email AND phone AND name), so
  // they are all the same person and folding them all in is the normal case. The checkboxes exist so
  // the SuperUser can *withhold* one, not so they have to hunt for the right one.
  mergePreview = signal<MergeCandidatesResponse | null>(null);
  isCheckingMerge = signal(false);
  showMergeModal = signal(false);
  isMerging = signal(false);

  /** The survivor's username. Everything lands here. */
  mergeTarget = signal('');

  /** Usernames of the accounts to fold in. */
  mergeSources = signal<string[]>([]);

  mergeCandidates = computed(() => this.mergePreview()?.candidates ?? []);

  /**
   * Every account in play — the one the SuperUser searched their way to, plus every other login that
   * keys to the same person. ANY of them can be the survivor: the parent may well name the old
   * account rather than the new one.
   */
  mergeAccounts = computed<MergeCandidateDto[]>(() => {
    const preview = this.mergePreview();
    return preview ? [preview.source, ...preview.candidates] : [];
  });

  isMergeSource = (userName: string): boolean => this.mergeSources().includes(userName);

  /** Registrations that will actually move — the blast radius, from what is CHECKED right now. */
  mergeMovingRegs = computed(() => {
    const selected = new Set(this.mergeSources());
    return this.mergeAccounts()
      .filter(a => selected.has(a.userName))
      .reduce((n, a) => n + a.registrationCount, 0);
  });

  canMerge = computed(() => !!this.mergeTarget() && this.mergeSources().length > 0);

  isPlayerRole = computed(() => {
    const player = this.roleOptions().find(o => o.roleName === 'Player');
    return !!player && this.selectedRoleId() === player.roleId;
  });

  ngOnInit(): void {
    this.service.getRoleOptions().subscribe({
      next: (opts) => {
        this.roleOptions.set(opts);
        const player = opts.find(o => o.roleName === 'Player');
        if (player) this.selectedRoleId.set(player.roleId);
      }
    });
  }

  // ═══════════════════════════════════════════════════════════
  //  Search
  // ═══════════════════════════════════════════════════════════

  onSearch(): void {
    if (!this.selectedRoleId()) return;
    // Snapshotted here so a later role-dropdown change can't re-key results already on screen.
    const asFamily = this.isPlayerRole();
    this.isSearching.set(true);
    this.hasSearched.set(true);
    this.expandedKey.set(null);

    this.service.search({
      roleId: this.selectedRoleId(),
      customerName: this.customerName() || undefined,
      jobName: this.jobName() || undefined,
      lastName: this.lastName() || undefined,
      firstName: this.firstName() || undefined,
      email: this.email() || undefined,
      phone: this.phone() || undefined,
      userName: this.userName() || undefined,
      familyUserName: this.familyUserName() || undefined
    }).subscribe({
      next: (rows) => {
        const accounts = this.buildAccounts(rows, asFamily);
        this.accounts.set(accounts);
        this.resultsCapped.set(accounts.length >= MAX_ACCOUNTS);
        this.isSearching.set(false);
      },
      error: (err) => {
        this.toast.show(err?.error?.message || 'Search failed.', 'danger');
        this.isSearching.set(false);
      }
    });
  }

  onClear(): void {
    this.customerName.set('');
    this.jobName.set('');
    this.lastName.set('');
    this.firstName.set('');
    this.email.set('');
    this.phone.set('');
    this.userName.set('');
    this.familyUserName.set('');
    this.accounts.set([]);
    this.hasSearched.set(false);
    this.expandedKey.set(null);
    this.expandedPlayerKeys.set(new Set());
  }

  /** Collapse registration rows to one account per login, then to one row per player. */
  private buildAccounts(rows: ChangePasswordSearchResultDto[], asFamily: boolean): LoginAccount[] {
    const byLogin = new Map<string, ChangePasswordSearchResultDto[]>();
    for (const r of rows) {
      const key = asFamily ? (r.familyUserName ?? '') : r.userName;
      if (!key) continue; // no login = nothing to reset
      const bucket = byLogin.get(key);
      if (bucket) bucket.push(r);
      else byLogin.set(key, [r]);
    }

    const accounts: LoginAccount[] = [];
    for (const [key, group] of byLogin) {
      const first = group[0];
      accounts.push({
        key,
        loginUserName: key,
        isFamilyLogin: asFamily,
        displayName: asFamily ? this.familyDisplayName(group) : this.personName(first),
        email: asFamily ? (first.familyEmail ?? null) : (first.email ?? null),
        phone: first.phone ?? null,
        roleName: first.roleName,
        anyRegId: first.registrationId,
        // Household facts — identical on every row of this login, which is exactly why they belong
        // here and not restamped down the players grid.
        momName: this.fullName(first.momFirstName, first.momLastName),
        momPhone: first.momPhone ?? null,
        dadName: this.fullName(first.dadFirstName, first.dadLastName),
        dadPhone: first.dadPhone ?? null,
        players: asFamily ? this.buildPlayers(group) : [],
        registrations: this.sortRegistrations(group)
      });
    }

    return accounts.sort((a, b) => a.displayName.localeCompare(b.displayName));
  }

  /** One row per child under a family login. */
  private buildPlayers(rows: ChangePasswordSearchResultDto[]): PlayerRow[] {
    const byPlayer = new Map<string, ChangePasswordSearchResultDto[]>();
    for (const r of rows) {
      // A player with no username of their own can't be grouped with anyone — give the row its own
      // bucket rather than letting every such row merge under the empty key.
      const key = r.userName || `reg:${r.registrationId}`;
      const bucket = byPlayer.get(key);
      if (bucket) bucket.push(r);
      else byPlayer.set(key, [r]);
    }

    const players: PlayerRow[] = [];
    for (const [key, group] of byPlayer) {
      const first = group[0];
      players.push({
        key,
        firstName: first.firstName ?? '',
        lastName: first.lastName ?? '',
        userName: first.userName,
        email: first.email ?? null,
        phone: first.phone ?? null,
        registrations: this.sortRegistrations(group)
      });
    }

    return players.sort((a, b) =>
      a.lastName.localeCompare(b.lastName) || a.firstName.localeCompare(b.firstName));
  }

  /** Customer, then job. Job names embed the season (`ISP:2021-2022`), so this reads chronologically. */
  private sortRegistrations(rows: ChangePasswordSearchResultDto[]): ChangePasswordSearchResultDto[] {
    return [...rows].sort((a, b) =>
      a.customerName.localeCompare(b.customerName) || a.jobName.localeCompare(b.jobName));
  }

  private fullName(first?: string | null, last?: string | null): string | null {
    const s = `${first ?? ''} ${last ?? ''}`.trim();
    return s.length > 0 ? s : null;
  }

  private personName(r: ChangePasswordSearchResultDto): string {
    return this.fullName(r.firstName, r.lastName) ?? r.userName;
  }

  private familyDisplayName(rows: ChangePasswordSearchResultDto[]): string {
    const lastNames = new Set(rows.map(r => (r.lastName ?? '').trim()).filter(Boolean));
    if (lastNames.size === 1) return `${[...lastNames][0]} family`;
    return `${rows[0].familyUserName} family`;
  }

  // ═══════════════════════════════════════════════════════════
  //  Expand / collapse
  // ═══════════════════════════════════════════════════════════

  isExpanded(acct: LoginAccount): boolean {
    return this.expandedKey() === acct.key;
  }

  toggleAccount(acct: LoginAccount): void {
    if (this.expandedKey() === acct.key) {
      this.expandedKey.set(null);
      return;
    }
    this.expandedKey.set(acct.key);
    this.expandedPlayerKeys.set(new Set());
    this.newPassword.set('');
    this.closeMerge();

    const first = acct.registrations[0];
    if (acct.isFamilyLogin) {
      this.editingFamilyEmail.set(first.familyEmail ?? '');
      this.editingMomEmail.set(first.momEmail ?? '');
      this.editingDadEmail.set(first.dadEmail ?? '');
    } else {
      this.editingEmail.set(first.email ?? '');
    }

    this.prefetchMergeCandidates(acct);
  }

  isPlayerExpanded(player: PlayerRow): boolean {
    return this.expandedPlayerKeys().has(player.key);
  }

  /** Any number of players can be open at once. New Set, never a mutation — signals are immutable. */
  togglePlayer(player: PlayerRow): void {
    const next = new Set(this.expandedPlayerKeys());
    if (!next.delete(player.key)) next.add(player.key);
    this.expandedPlayerKeys.set(next);
  }

  playerName(player: PlayerRow): string {
    return `${player.firstName} ${player.lastName}`.trim() || player.userName;
  }

  expandedAccount(): LoginAccount | null {
    const key = this.expandedKey();
    return key ? this.accounts().find(a => a.key === key) ?? null : null;
  }

  /** Copy the login username to the clipboard; flip the row's icon to a check for ~2s. */
  copyUsername(acct: LoginAccount): void {
    navigator.clipboard.writeText(acct.loginUserName).then(() => {
      this.copiedKey.set(acct.key);
      setTimeout(() => { if (this.copiedKey() === acct.key) this.copiedKey.set(null); }, 2000);
    }).catch(() => {
      this.toast.show('Could not copy to the clipboard.', 'danger');
    });
  }

  // ═══════════════════════════════════════════════════════════
  //  Reset password (the hero action)
  // ═══════════════════════════════════════════════════════════

  resetTargetLabel(acct: LoginAccount): string {
    return acct.isFamilyLogin
      ? `Resets the ${acct.displayName} login (${acct.loginUserName})`
      : `Resets ${acct.loginUserName}'s login`;
  }

  confirmReset(acct: LoginAccount): void {
    if (this.newPassword().length < 6 || this.isResetting()) return;
    this.isResetting.set(true);

    // The SERVER resolves the account from the registration's FK. We only say which FK to follow,
    // and assert who we think that is — a mismatch (stale grid, merged-away row) is rejected.
    this.service.resetPassword(acct.anyRegId, {
      target: acct.isFamilyLogin ? ResetTarget.Family : ResetTarget.User,
      newPassword: this.newPassword(),
      expectedUserName: acct.loginUserName
    }).subscribe({
      next: (res) => {
        this.toast.show(res.message, 'success');
        this.newPassword.set('');
        this.isResetting.set(false);
      },
      error: (err) => {
        this.toast.show(err?.error?.message || 'Password reset failed.', 'danger');
        this.isResetting.set(false);
      }
    });
  }

  // ═══════════════════════════════════════════════════════════
  //  Email (secondary account tool)
  // ═══════════════════════════════════════════════════════════

  saveEmail(acct: LoginAccount): void {
    this.isSavingEmail.set(true);
    const done = {
      next: (res: ApiMessage) => {
        this.toast.show(res.message, 'success');
        this.isSavingEmail.set(false);
        this.onSearch(); // refresh face values (collapses; acceptable)
      },
      error: (err: { error?: { message?: string } }) => {
        this.toast.show(err?.error?.message || 'Failed to update email.', 'danger');
        this.isSavingEmail.set(false);
      }
    };

    if (acct.isFamilyLogin) {
      // Send the empty string, NOT undefined: an emptied field means "clear this address", and
      // `|| undefined` dropped it from the payload so a stale address could never be removed.
      this.service.updateFamilyEmails(acct.anyRegId, {
        familyEmail: this.editingFamilyEmail().trim(),
        momEmail: this.editingMomEmail().trim(),
        dadEmail: this.editingDadEmail().trim()
      }).subscribe(done);
    } else {
      this.service.updateUserEmail(acct.anyRegId, {
        email: this.editingEmail().trim()
      }).subscribe(done);
    }
  }

  // ═══════════════════════════════════════════════════════════
  //  Per-player edit (email only)
  // ═══════════════════════════════════════════════════════════

  /** Any of the player's registrations identifies them to the API; they all carry the same UserId. */
  private playerRegId(player: PlayerRow): string {
    return player.registrations[0].registrationId;
  }

  openPlayerEdit(player: PlayerRow): void {
    this.editPlayer.set(player);
    this.editEmail.set(player.email ?? '');
  }

  closePlayerEdit(): void {
    this.editPlayer.set(null);
  }

  playerEditDirty = computed(() => {
    const p = this.editPlayer();
    return !!p && this.editEmail().trim() !== (p.email ?? '');
  });

  savePlayerEdit(): void {
    const player = this.editPlayer();
    if (!player || this.isSavingPlayer() || !this.playerEditDirty()) return;

    this.isSavingPlayer.set(true);

    this.service.updateUserEmail(this.playerRegId(player), { email: this.editEmail().trim() }).subscribe({
      next: () => {
        this.toast.show('Email updated.', 'success');
        this.isSavingPlayer.set(false);
        this.closePlayerEdit();
        this.onSearch();
      },
      error: (err) => {
        this.toast.show(err?.error?.message || 'Failed to update the email; nothing was changed.', 'danger');
        this.isSavingPlayer.set(false);
      }
    });
  }

  // ═══════════════════════════════════════════════════════════
  //  Merge (account level)
  // ═══════════════════════════════════════════════════════════

  private prefetchMergeCandidates(acct: LoginAccount): void {
    this.mergePreview.set(null);
    this.isCheckingMerge.set(true);
    const call = acct.isFamilyLogin
      ? this.service.getFamilyMergeCandidates(acct.anyRegId)
      : this.service.getUserMergeCandidates(acct.anyRegId);
    call.subscribe({
      next: (p) => { this.mergePreview.set(p); this.isCheckingMerge.set(false); },
      error: () => { this.isCheckingMerge.set(false); }
    });
  }

  openMerge(): void {
    // Nothing preselected. This is irreversible, and the survivor is the account the PARENT named —
    // not something for the tool to guess at.
    this.mergeTarget.set('');
    this.mergeSources.set([]);
    this.showMergeModal.set(true);
  }

  closeMerge(): void {
    this.showMergeModal.set(false);
    this.mergeTarget.set('');
    this.mergeSources.set([]);
  }

  /**
   * Choosing the survivor checks everything else by default. They all key to the same person, so
   * folding them all in is the normal case — and a parent who has forgotten their password twice has
   * three logins, not two. Unchecking is the exception, and it stays available.
   */
  setMergeTarget(userName: string): void {
    this.mergeTarget.set(userName);
    this.mergeSources.set(
      this.mergeAccounts().map(a => a.userName).filter(u => u !== userName)
    );
  }

  toggleMergeSource(userName: string): void {
    const current = this.mergeSources();
    this.mergeSources.set(
      current.includes(userName)
        ? current.filter(u => u !== userName)
        : [...current, userName]
    );
  }

  confirmMerge(acct: LoginAccount): void {
    if (!this.canMerge() || this.isMerging()) return;

    this.isMerging.set(true);

    // Both halves are explicit. The server re-derives the candidate set from the identity key and
    // rejects any username that is not in it, so nothing the browser sends can widen the write.
    const req: MergeUsernameRequest = {
      targetUserName: this.mergeTarget(),
      sourceUserNames: this.mergeSources()
    };

    const call = acct.isFamilyLogin
      ? this.service.mergeFamilyUsername(acct.anyRegId, req)
      : this.service.mergeUsername(acct.anyRegId, req);

    call.subscribe({
      next: (res) => {
        this.toast.show(res.message, 'success');
        this.isMerging.set(false);
        this.closeMerge();
        this.onSearch();
      },
      error: (err) => {
        this.toast.show(err?.error?.message || 'Merge failed.', 'danger');
        this.isMerging.set(false);
      }
    });
  }
}
