import { Component, OnInit, signal, computed, inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChangePasswordService } from './services/change-password.service';
import { ToastService } from '@shared-ui/toast.service';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { PhonePipe } from '@infrastructure/pipes/phone.pipe';
import type {
  ChangePasswordSearchResultDto,
  ChangePasswordRoleOptionDto,
  MergeCandidateDto
} from '@core/api';

/**
 * View-model for one LOGIN account — the thing whose password we actually reset.
 * The backend search is registration-grained (one row per event a person is in);
 * we collapse those rows to one account per login: UserName for non-players,
 * FamilyUserName for players (the kid logs in under the family account).
 * The underlying rows are kept as `registrations` — pure identity proof.
 */
interface LoginAccount {
  key: string;
  loginUserName: string;
  isFamilyLogin: boolean;
  displayName: string;
  email: string | null;
  phone: string | null;
  anyRegId: string;
  playerCount: number;
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

  // ── Reset password (transient, for the open account) ──
  newPassword = signal('');
  isResetting = signal(false);

  // ── Email edit (transient, for the open account) ──
  editingEmail = signal('');        // non-family: user email
  editingFamilyEmail = signal('');  // family: family/mom/dad
  editingMomEmail = signal('');
  editingDadEmail = signal('');
  isSavingEmail = signal(false);

  // ── Merge (secondary; auto-hidden when no duplicate exists) ──
  mergeCandidates = signal<MergeCandidateDto[]>([]);
  isCheckingMerge = signal(false);
  showMergeModal = signal(false);
  selectedMergeUserName = signal('');
  isMerging = signal(false);

  // Whether the CURRENT search targets players (branch on login type). Snapshotted
  // at search time so a later role-dropdown change can't re-key existing results.
  private searchedAsFamily = false;

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
        this.searchedAsFamily = asFamily;
        this.accounts.set(this.buildAccounts(rows, asFamily));
        this.resultsCapped.set(rows.length >= 200);
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
  }

  /** Collapse registration rows to one account per login. */
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
        playerCount: asFamily ? this.distinctPlayers(group).length : 1,
        registrations: group
      });
    }

    // Alphabetical by display name — stable, scannable.
    return accounts.sort((a, b) => a.displayName.localeCompare(b.displayName));
  }

  private personName(r: ChangePasswordSearchResultDto): string {
    return `${r.firstName ?? ''} ${r.lastName ?? ''}`.trim() || r.userName;
  }

  private distinctPlayers(rows: ChangePasswordSearchResultDto[]): string[] {
    const seen = new Set<string>();
    for (const r of rows) {
      const name = this.personName(r);
      if (name) seen.add(name);
    }
    return [...seen];
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

  /** Registrations as identity breadcrumbs. Family rows carry the player name. */
  proofLabel(r: ChangePasswordSearchResultDto): string {
    return `${r.customerName} : ${r.jobName}`;
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
      this.service.updateFamilyEmails(acct.anyRegId, {
        familyEmail: this.editingFamilyEmail() || undefined,
        momEmail: this.editingMomEmail() || undefined,
        dadEmail: this.editingDadEmail() || undefined
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
