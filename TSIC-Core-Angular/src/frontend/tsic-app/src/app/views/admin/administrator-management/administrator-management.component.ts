import { Component, inject, signal, computed, effect, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { AdministratorService } from './services/administrator.service';
import { AdminFormModalComponent, AdminFormResult } from './components/admin-form-modal.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { ToastService } from '@shared-ui/toast.service';
import { JobService } from '@infrastructure/services/job.service';
import type { AdministratorDto } from '@core/api';

@Component({
    selector: 'app-administrator-management',
    standalone: true,
    imports: [CommonModule, DatePipe, AdminFormModalComponent, ConfirmDialogComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './administrator-management.component.html',
    styleUrl: './administrator-management.component.scss'
})
export class AdministratorManagementComponent {
    private readonly adminService = inject(AdministratorService);
    private readonly jobService = inject(JobService);
    private readonly toast = inject(ToastService);

    // State
    readonly administrators = signal<AdministratorDto[]>([]);
    readonly selectedIds = signal<Set<string>>(new Set());
    readonly isLoading = signal(false);
    readonly errorMessage = signal<string | null>(null);

    // Modal state
    readonly showAddModal = signal(false);
    readonly showEditModal = signal(false);
    readonly editTarget = signal<AdministratorDto | null>(null);
    readonly showDeleteConfirm = signal(false);
    readonly deleteTarget = signal<AdministratorDto | null>(null);
    readonly showBatchConfirm = signal(false);
    readonly batchAction = signal<'activate' | 'inactivate'>('activate');

    // Computed
    readonly nonSuperusers = computed(() =>
        this.administrators().filter(a => !a.isSuperuser)
    );

    readonly selectedCount = computed(() => this.selectedIds().size);

    readonly hasSelection = computed(() => this.selectedIds().size > 0);

    readonly allNonSuperusersSelected = computed(() => {
        const nonSu = this.nonSuperusers();
        if (nonSu.length === 0) return false;
        const sel = this.selectedIds();
        return nonSu.every(a => sel.has(a.registrationId));
    });

    // Load administrators when job context becomes available
    private readonly loadOnJobChange = effect(() => {
        const job = this.jobService.currentJob();
        if (job?.jobPath) {
            this.loadAdministrators();
        }
    });

    loadAdministrators() {
        this.isLoading.set(true);
        this.errorMessage.set(null);
        this.adminService.getAdministrators().subscribe({
            next: admins => {
                this.administrators.set(admins);
                this.selectedIds.set(new Set());
                this.isLoading.set(false);
            },
            error: err => {
                this.errorMessage.set(err?.error?.message || 'Failed to load administrators.');
                this.isLoading.set(false);
            }
        });
    }

    // Selection
    toggleSelect(admin: AdministratorDto) {
        if (admin.isSuperuser) return;
        const current = new Set(this.selectedIds());
        if (current.has(admin.registrationId)) {
            current.delete(admin.registrationId);
        } else {
            current.add(admin.registrationId);
        }
        this.selectedIds.set(current);
    }

    toggleSelectAll() {
        const nonSu = this.nonSuperusers();
        if (this.allNonSuperusersSelected()) {
            this.selectedIds.set(new Set());
        } else {
            this.selectedIds.set(new Set(nonSu.map(a => a.registrationId)));
        }
    }

    isSelected(admin: AdministratorDto): boolean {
        return this.selectedIds().has(admin.registrationId);
    }

    // CRUD
    openAdd() {
        this.showAddModal.set(true);
    }

    openEdit(admin: AdministratorDto) {
        this.editTarget.set(admin);
        this.showEditModal.set(true);
    }

    onFormSaved(result: AdminFormResult) {
        if (result.mode === 'add' && result.addRequest) {
            this.adminService.addAdministrator(result.addRequest).subscribe({
                next: () => {
                    this.toast.show('Administrator added successfully.', 'success');
                    this.showAddModal.set(false);
                    this.loadAdministrators();
                },
                error: err => {
                    this.toast.show(err?.error?.message || 'Failed to add administrator.', 'danger', 4000);
                }
            });
        } else if (result.mode === 'edit' && result.updateRequest && result.registrationId) {
            this.adminService.updateAdministrator(result.registrationId, result.updateRequest).subscribe({
                next: () => {
                    this.toast.show('Administrator updated successfully.', 'success');
                    this.showEditModal.set(false);
                    this.editTarget.set(null);
                    this.loadAdministrators();
                },
                error: err => {
                    this.toast.show(err?.error?.message || 'Failed to update administrator.', 'danger', 4000);
                }
            });
        }
    }

    confirmDelete(admin: AdministratorDto) {
        this.deleteTarget.set(admin);
        this.showDeleteConfirm.set(true);
    }

    onDeleteConfirmed() {
        const target = this.deleteTarget();
        if (!target) return;
        this.showDeleteConfirm.set(false);
        this.adminService.deleteAdministrator(target.registrationId).subscribe({
            next: () => {
                this.toast.show('Administrator removed.', 'success');
                this.deleteTarget.set(null);
                this.loadAdministrators();
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to delete administrator.', 'danger', 4000);
            }
        });
    }

    // Batch
    openBatchConfirm(action: 'activate' | 'inactivate') {
        this.batchAction.set(action);
        this.showBatchConfirm.set(true);
    }

    onBatchConfirmed() {
        const isActive = this.batchAction() === 'activate';
        this.showBatchConfirm.set(false);
        this.adminService.batchUpdateStatus(isActive).subscribe({
            next: result => {
                this.toast.show(
                    `${result.updated} administrator(s) ${isActive ? 'activated' : 'inactivated'}.`,
                    'success'
                );
                this.loadAdministrators();
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Batch update failed.', 'danger', 4000);
            }
        });
    }

    getRoleBadgeClass(roleName: string | null | undefined): string {
        if (!roleName) return 'bg-dark-subtle text-dark-emphasis';
        switch (roleName) {
            case 'Director': return 'bg-primary-subtle text-primary-emphasis';
            case 'SuperDirector': return 'bg-info-subtle text-info-emphasis';
            case 'ApiAuthorized': return 'bg-warning-subtle text-warning-emphasis';
            case 'Ref Assignor': return 'bg-success-subtle text-success-emphasis';
            case 'Store Admin': return 'bg-secondary-subtle text-secondary-emphasis';
            case 'STPAdmin': return 'bg-danger-subtle text-danger-emphasis';
            default: return 'bg-secondary-subtle text-secondary-emphasis';
        }
    }
}
