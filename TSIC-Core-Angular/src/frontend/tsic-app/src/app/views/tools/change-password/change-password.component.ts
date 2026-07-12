import { Component, OnInit, signal, computed, inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Observable, concat } from 'rxjs';
import { toArray } from 'rxjs/operators';
import { ChangePasswordService, type ApiMessage } from './services/change-password.service';
import { ToastService } from '@shared-ui/toast.service';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { PhonePipe } from '@infrastructure/pipes/phone.pipe';
import type {
  ChangePasswordSearchResultDto,
  ChangePasswordRoleOptionDto,
  MergeCandidateDto
} from '@core/api';

// Mirrors MaxAccounts in ChangePasswordRepository — the server caps the search at this many
// login accounts, so hitting it exactly is how we know results were truncated.
const MAX_ACCOUNTS = 50;

/**
 * View-model for one LOGIN account — the thing whose password we actually reset.
 * The backend search is registration-grained (one row per event a person is in).
 * We rebuild the three levels those flat rows encode:
 *
 *   account (the login)  →  player  →  registration
 *
 * Each field lands at the level where it actually varies: family credentials and the
 * parents on the account, the person's own name/username/email on the player, and only
 * customer + job on the registration. Non-player roles have no middle level — the
 * account IS the person — so `players` is empty and `registrations` is read directly.
 */
interface LoginAccount {
  key: string;
  loginUserName: string;
  isFamilyLogin: boolean;
  displayName: string;
  email: string | null;
  phone: string | null;
  anyRegId: string;
  players: PlayerRow[];
  registrations: ChangePasswordSearchResultDto[];
}

/**
 * One child under a family login. Keyed on the player's OWN username — never the display
 * name, or two kids sharing a nickname would collapse into one row.
 *
 * `source` is any one of the player's rows: every column legacy's grid carried (role, family
 * username/email, Mom/Dad names, emails, phones) is identical across a player's rows, so the
 * grid reads them straight off it. Name/username/email/phone are lifted out because sorting
 * and the aria labels need them directly.
 */
interface PlayerRow {
  key: string;
  firstName: string;
  lastName: string;
  userName: string;
  email: string | null;
  phone: string | null;
  source: ChangePasswordSearchResultDto;
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
  editingEmail = signal('');        // non-family: user email
  editingFamilyEmail = signal('');  // family: family/mom/dad
  editingMomEmail = signal('');
  editingDadEmail = signal('');
  isSavingEmail = signal(false);

  // ── Per-player editor ──
  // Editable: R-Email and R-UserName (a merge). NOT the password: a player has no usable
  // login — the family signs in and picks the child, so the player's own credentials are
  // vestigial (half of them are raw GUIDs). Legacy exposed NewUserPassword because its one
  // grid also served the adult roles, where the person's username IS their login.
  editPlayer = signal<PlayerRow | null>(null);
  editEmail = signal('');
  editUserName = signal('');
  editMergeCandidates = signal<MergeCandidateDto[]>([]);
  isLoadingEditCandidates = signal(false);
  isSavingPlayer = signal(false);

  // ── Merge (secondary; auto-hidden when no duplicate exists) ──
  mergeCandidates = signal<MergeCandidateDto[]>([]);
  isCheckingMerge = signal(false);
  showMergeModal = signal(false);
  selectedMergeUserName = signal('');
  isMerging = signal(false);

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
      if (!key) continue; // no login = nothing to reset; skip
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
        email: asFamily ? (first.familyEmail ?? first.email ?? null) : (first.email ?? null),
        phone: first.phone ?? null,
        anyRegId: first.registrationId as unknown as string,
        players: asFamily ? this.buildPlayers(group) : [],
        registrations: this.sortRegistrations(group)
      });
    }

    // Alphabetical by display name — stable, scannable.
    return accounts.sort((a, b) => a.displayName.localeCompare(b.displayName));
  }

  /** One row per child under a family login. */
  private buildPlayers(rows: ChangePasswordSearchResultDto[]): PlayerRow[] {
    const byPlayer = new Map<string, ChangePasswordSearchResultDto[]>();
    for (const r of rows) {
      // A player with no username of their own can't be grouped with anyone — give the row
      // its own bucket rather than letting every such row merge under the empty key.
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
        source: first,
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

  private personName(r: ChangePasswordSearchResultDto): string {
    return `${r.firstName ?? ''} ${r.lastName ?? ''}`.trim() || r.userName;
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

    // Seed the (secondary) email editors from the account's rows.
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

  // ═══════════════════════════════════════════════════════════
  //  Per-player edit (email / username / password)
  // ═══════════════════════════════════════════════════════════

  /** Any of the player's registrations identifies them to the API; they all carry the same UserId. */
  private playerRegId(player: PlayerRow): string {
    return player.registrations[0].registrationId as unknown as string;
  }

  openPlayerEdit(player: PlayerRow): void {
    this.editPlayer.set(player);
    this.editEmail.set(player.email ?? '');
    this.editUserName.set(player.userName);
    this.editMergeCandidates.set([]);
    this.isLoadingEditCandidates.set(true);

    this.service.getUserMergeCandidates(this.playerRegId(player)).subscribe({
      next: (c) => { this.editMergeCandidates.set(c); this.isLoadingEditCandidates.set(false); },
      error: () => { this.isLoadingEditCandidates.set(false); }
    });
  }

  closePlayerEdit(): void {
    this.editPlayer.set(null);
  }

  /** True once something in the editor differs from the row it was seeded from. */
  playerEditDirty = computed(() => {
    const p = this.editPlayer();
    if (!p) return false;
    return this.editEmail().trim() !== (p.email ?? '')
      || this.editUserName() !== p.userName;
  });

  savePlayerEdit(): void {
    const player = this.editPlayer();
    if (!player || this.isSavingPlayer()) return;

    const regId = this.playerRegId(player);
    const email = this.editEmail().trim();
    const targetUserName = this.editUserName();

    // Sequenced, not parallel: the merge repoints registrations, so the email edit must land
    // on the source user first — after the merge, this regId belongs to the target.
    const ops: Observable<ApiMessage>[] = [];
    // Empty is a real edit — it clears the address (legacy parity). Compare, don't truthiness-test.
    if (email !== (player.email ?? '')) {
      ops.push(this.service.updateUserEmail(regId, { email }));
    }
    if (targetUserName && targetUserName !== player.userName) {
      ops.push(this.service.mergeUsername(regId, { targetUserName }));
    }

    if (ops.length === 0) {
      this.closePlayerEdit();
      return;
    }

    this.isSavingPlayer.set(true);
    concat(...ops).pipe(toArray()).subscribe({
      next: (results) => this.toast.show(results.map(r => r.message).join(' '), 'success'),
      error: (err) => {
        this.toast.show(err?.error?.message || 'Update failed.', 'danger');
        this.isSavingPlayer.set(false);
      },
      complete: () => {
        this.isSavingPlayer.set(false);
        this.closePlayerEdit();
        this.onSearch();
      }
    });
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
    const req = { userName: acct.loginUserName, newPassword: this.newPassword() };
    const call = acct.isFamilyLogin
      ? this.service.resetFamilyPassword(acct.anyRegId, req)
      : this.service.resetPassword(acct.anyRegId, req);

    call.subscribe({
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
      next: (res: { message: string }) => {
        this.toast.show(res.message, 'success');
        this.isSavingEmail.set(false);
        this.onSearch(); // refresh face values (collapses; acceptable — matches prior tool)
      },
      error: (err: { error?: { message?: string } }) => {
        this.toast.show(err?.error?.message || 'Failed to update email.', 'danger');
        this.isSavingEmail.set(false);
      }
    };

    if (acct.isFamilyLogin) {
      // Send the empty string, NOT undefined: an emptied field means "clear this address",
      // and `|| undefined` dropped it from the payload so a stale address could never be removed.
      this.service.updateFamilyEmails(acct.anyRegId, {
        familyEmail: this.editingFamilyEmail().trim(),
        momEmail: this.editingMomEmail().trim(),
        dadEmail: this.editingDadEmail().trim()
      }).subscribe(done);
    } else {
      this.service.updateUserEmail(acct.anyRegId, {
        email: this.editingEmail()
      }).subscribe(done);
    }
  }

  // ═══════════════════════════════════════════════════════════
  //  Merge username (secondary; only surfaces when a duplicate exists)
  // ═══════════════════════════════════════════════════════════

  private prefetchMergeCandidates(acct: LoginAccount): void {
    this.mergeCandidates.set([]);
    this.isCheckingMerge.set(true);
    const call = acct.isFamilyLogin
      ? this.service.getFamilyMergeCandidates(acct.anyRegId)
      : this.service.getUserMergeCandidates(acct.anyRegId);
    call.subscribe({
      next: (c) => { this.mergeCandidates.set(c); this.isCheckingMerge.set(false); },
      error: () => { this.isCheckingMerge.set(false); }
    });
  }

  openMerge(): void {
    const candidates = this.mergeCandidates();
    this.selectedMergeUserName.set(candidates.length > 0 ? candidates[0].userName : '');
    this.showMergeModal.set(true);
  }

  closeMerge(): void {
    this.showMergeModal.set(false);
    this.selectedMergeUserName.set('');
  }

  confirmMerge(acct: LoginAccount): void {
    const target = this.selectedMergeUserName();
    if (!target || this.isMerging()) return;
    this.isMerging.set(true);
    const req = { targetUserName: target };
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

  expandedAccount(): LoginAccount | null {
    const key = this.expandedKey();
    return key ? this.accounts().find(a => a.key === key) ?? null : null;
  }
}
