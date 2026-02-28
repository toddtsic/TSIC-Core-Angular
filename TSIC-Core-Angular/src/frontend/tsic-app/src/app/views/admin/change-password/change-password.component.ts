import { Component, OnInit, signal, computed, inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChangePasswordService } from './services/change-password.service';
import { ToastService } from '@shared-ui/toast.service';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import type {
  ChangePasswordSearchResultDto,
  ChangePasswordRoleOptionDto,
  MergeCandidateDto
} from '@core/api';

@Component({
  selector: 'app-change-password',
  standalone: true,
  imports: [CommonModule, FormsModule, TsicDialogComponent],
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

  // ── Results ──
  results = signal<ChangePasswordSearchResultDto[]>([]);
  isSearching = signal(false);
  hasSearched = signal(false);
  resultsCapped = signal(false);

  // ── Expanded row ──
  expandedRegId = signal<string | null>(null);

  // ── Inline email editing ──
  editingUserEmail = signal('');
  editingFamilyEmail = signal('');
  editingMomEmail = signal('');
  editingDadEmail = signal('');
  isSavingEmail = signal(false);

  // ── Reset password modal ──
  showResetModal = signal(false);
  resetTarget = signal<{ regId: string; userName: string; label: string } | null>(null);
  newPassword = signal('');
  isResetting = signal(false);

  // ── Merge pre-check (expanded row) ──
  expandedUserMergeCandidates = signal<MergeCandidateDto[]>([]);
  expandedFamilyMergeCandidates = signal<MergeCandidateDto[]>([]);
  isCheckingUserMerge = signal(false);
  isCheckingFamilyMerge = signal(false);

  // ── Merge modal ──
  showMergeModal = signal(false);
  mergeTarget = signal<{ regId: string; type: 'user' | 'family'; currentUserName: string } | null>(null);
  mergeCandidates = signal<MergeCandidateDto[]>([]);
  selectedMergeUserName = signal('');
  isMerging = signal(false);

  // ── Computed ──
  isPlayerSearch = computed(() => {
    const opts = this.roleOptions();
    const selected = this.selectedRoleId();
    const playerOpt = opts.find(o => o.roleName === 'Player');
    return !!playerOpt && selected === playerOpt.roleId;
  });

  ngOnInit(): void {
    this.service.getRoleOptions().subscribe({
      next: (opts) => {
        this.roleOptions.set(opts);
        // Default to Player
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
    this.isSearching.set(true);
    this.hasSearched.set(true);
    this.expandedRegId.set(null);

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
      next: (data) => {
        this.results.set(data);
        this.resultsCapped.set(data.length >= 200);
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
    this.results.set([]);
    this.hasSearched.set(false);
    this.expandedRegId.set(null);
  }

  // ═══════════════════════════════════════════════════════════
  //  Expand/collapse row
  // ═══════════════════════════════════════════════════════════

  toggleRow(row: ChangePasswordSearchResultDto): void {
    const regId = row.registrationId as unknown as string;
    if (this.expandedRegId() === regId) {
      this.expandedRegId.set(null);
    } else {
      this.expandedRegId.set(regId);
      // Pre-populate email fields
      this.editingUserEmail.set(row.email || '');
      this.editingFamilyEmail.set(row.familyEmail || '');
      this.editingMomEmail.set(row.momEmail || '');
      this.editingDadEmail.set(row.dadEmail || '');
      // Pre-fetch merge candidates so buttons only appear when relevant
      this.prefetchMergeCandidates(regId);
    }
  }

  private prefetchMergeCandidates(regId: string): void {
    this.expandedUserMergeCandidates.set([]);
    this.expandedFamilyMergeCandidates.set([]);
    this.isCheckingUserMerge.set(true);

    this.service.getUserMergeCandidates(regId).subscribe({
      next: (c) => { this.expandedUserMergeCandidates.set(c); this.isCheckingUserMerge.set(false); },
      error: () => { this.isCheckingUserMerge.set(false); }
    });

    if (this.isPlayerSearch()) {
      this.isCheckingFamilyMerge.set(true);
      this.service.getFamilyMergeCandidates(regId).subscribe({
        next: (c) => { this.expandedFamilyMergeCandidates.set(c); this.isCheckingFamilyMerge.set(false); },
        error: () => { this.isCheckingFamilyMerge.set(false); }
      });
    }
  }

  isExpanded(row: ChangePasswordSearchResultDto): boolean {
    return this.expandedRegId() === (row.registrationId as unknown as string);
  }

  regIdStr(row: ChangePasswordSearchResultDto): string {
    return row.registrationId as unknown as string;
  }

  // ═══════════════════════════════════════════════════════════
  //  Email editing
  // ═══════════════════════════════════════════════════════════

  saveUserEmail(row: ChangePasswordSearchResultDto): void {
    this.isSavingEmail.set(true);
    this.service.updateUserEmail(this.regIdStr(row), {
      email: this.editingUserEmail()
    }).subscribe({
      next: (res) => {
        this.toast.show(res.message, 'success');
        this.isSavingEmail.set(false);
        this.onSearch(); // Refresh results
      },
      error: (err) => {
        this.toast.show(err?.error?.message || 'Failed to update email.', 'danger');
        this.isSavingEmail.set(false);
      }
    });
  }

  saveFamilyEmails(row: ChangePasswordSearchResultDto): void {
    this.isSavingEmail.set(true);
    this.service.updateFamilyEmails(this.regIdStr(row), {
      familyEmail: this.editingFamilyEmail() || undefined,
      momEmail: this.editingMomEmail() || undefined,
      dadEmail: this.editingDadEmail() || undefined
    }).subscribe({
      next: (res) => {
        this.toast.show(res.message, 'success');
        this.isSavingEmail.set(false);
        this.onSearch();
      },
      error: (err) => {
        this.toast.show(err?.error?.message || 'Failed to update family emails.', 'danger');
        this.isSavingEmail.set(false);
      }
    });
  }

  // ═══════════════════════════════════════════════════════════
  //  Password reset
  // ═══════════════════════════════════════════════════════════

  openResetModal(row: ChangePasswordSearchResultDto, type: 'user' | 'family'): void {
    const uname = type === 'user' ? row.userName : row.familyUserName;
    const label = type === 'user' ? 'User' : 'Family';
    this.resetTarget.set({ regId: this.regIdStr(row), userName: uname || '', label });
    this.newPassword.set('');
    this.showResetModal.set(true);
  }

  cancelReset(): void {
    this.showResetModal.set(false);
    this.resetTarget.set(null);
    this.newPassword.set('');
  }

  confirmResetPassword(): void {
    const target = this.resetTarget();
    if (!target || this.newPassword().length < 6) return;

    this.isResetting.set(true);
    const req = { userName: target.userName, newPassword: this.newPassword() };
    const call = target.label === 'Family'
      ? this.service.resetFamilyPassword(target.regId, req)
      : this.service.resetPassword(target.regId, req);

    call.subscribe({
      next: (res) => {
        this.toast.show(res.message, 'success');
        this.isResetting.set(false);
        this.cancelReset();
      },
      error: (err) => {
        this.toast.show(err?.error?.message || 'Password reset failed.', 'danger');
        this.isResetting.set(false);
      }
    });
  }

  // ═══════════════════════════════════════════════════════════
  //  Username merge
  // ═══════════════════════════════════════════════════════════

  openMergeModal(row: ChangePasswordSearchResultDto, type: 'user' | 'family'): void {
    const currentUserName = type === 'user' ? row.userName : (row.familyUserName || '');
    const candidates = type === 'user'
      ? this.expandedUserMergeCandidates()
      : this.expandedFamilyMergeCandidates();

    this.mergeTarget.set({ regId: this.regIdStr(row), type, currentUserName });
    this.mergeCandidates.set(candidates);
    this.selectedMergeUserName.set(candidates.length > 0 ? candidates[0].userName : '');
    this.showMergeModal.set(true);
  }

  cancelMerge(): void {
    this.showMergeModal.set(false);
    this.mergeTarget.set(null);
    this.mergeCandidates.set([]);
    this.selectedMergeUserName.set('');
  }

  confirmMerge(): void {
    const target = this.mergeTarget();
    const selected = this.selectedMergeUserName();
    if (!target || !selected) return;

    this.isMerging.set(true);
    const req = { targetUserName: selected };
    const call = target.type === 'user'
      ? this.service.mergeUsername(target.regId, req)
      : this.service.mergeFamilyUsername(target.regId, req);

    call.subscribe({
      next: (res) => {
        this.toast.show(res.message, 'success');
        this.isMerging.set(false);
        this.cancelMerge();
        this.onSearch();
      },
      error: (err) => {
        this.toast.show(err?.error?.message || 'Merge failed.', 'danger');
        this.isMerging.set(false);
      }
    });
  }
}
