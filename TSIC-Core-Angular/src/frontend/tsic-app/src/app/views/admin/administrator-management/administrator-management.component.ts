import { Component, inject, signal, computed, effect, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { AdministratorService } from './services/administrator.service';
import { AdminFormModalComponent, AdminFormResult } from './components/admin-form-modal.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { ToastService } from '@shared-ui/toast.service';
import { JobService } from '@infrastructure/services/job.service';
import type { AdministratorDto } from '@core/api';

type SortColumn = 'name' | 'role' | 'username' | 'status' | 'registered';
type SortDirection = 'asc' | 'desc';

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

    // Data
    readonly administrators = signal<AdministratorDto[]>([]);
    readonly isLoading = signal(false);
    readonly errorMessage = signal<string | null>(null);

    // Sorting
    readonly sortColumn = signal<SortColumn>('name');
    readonly sortDirection = signal<SortDirection>('asc');

    readonly sortedAdministrators = computed(() => {
        const admins = [...this.administrators()];
        const col = this.sortColumn();
        const dir = this.sortDirection() === 'asc' ? 1 : -1;

        return admins.sort((a, b) => {
            let aVal: string | number;
            let bVal: string | number;

            switch (col) {
                case 'name':
                    aVal = a.administratorName.toLowerCase();
                    bVal = b.administratorName.toLowerCase();
                    break;
                case 'role':
                    aVal = (a.isSuperuser ? 'Superuser' : (a.roleName ?? '')).toLowerCase();
                    bVal = (b.isSuperuser ? 'Superuser' : (b.roleName ?? '')).toLowerCase();
                    break;
                case 'username':
                    aVal = a.userName.toLowerCase();
                    bVal = b.userName.toLowerCase();
                    break;
                case 'status':
                    aVal = a.isActive ? 1 : 0;
                    bVal = b.isActive ? 1 : 0;
                    break;
                case 'registered':
                    aVal = new Date(a.registeredDate).getTime();
                    bVal = new Date(b.registeredDate).getTime();
                    break;
                default:
                    return 0;
            }

            if (typeof aVal === 'string') {
                return dir * aVal.localeCompare(bVal as string);
            }
            return dir * ((aVal as number) - (bVal as number));
        });
    });

    // Modal state
    readonly showAddModal = signal(false);
    readonly showEditModal = signal(false);
    readonly editTarget = signal<AdministratorDto | null>(null);
    readonly showDeleteConfirm = signal(false);
    readonly deleteTarget = signal<AdministratorDto | null>(null);

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
                this.isLoading.set(false);
            },
            error: err => {
                this.errorMessage.set(err?.error?.message || 'Failed to load administrators.');
                this.isLoading.set(false);
            }
        });
    }

    // Sorting
    sort(column: SortColumn) {
        if (this.sortColumn() === column) {
            this.sortDirection.set(this.sortDirection() === 'asc' ? 'desc' : 'asc');
        } else {
            this.sortColumn.set(column);
            this.sortDirection.set('asc');
        }
    }

    // Status toggle
    toggleStatus(admin: AdministratorDto) {
        this.adminService.toggleStatus(admin.registrationId).subscribe({
            next: admins => {
                this.administrators.set(admins);
                this.toast.show(
                    admin.isActive
                        ? `${admin.administratorName} inactivated.`
                        : `${admin.administratorName} activated.`,
                    'success'
                );
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to update status.', 'danger', 4000);
            }
        });
    }

    // Primary Contact
    setPrimaryContact(admin: AdministratorDto) {
        this.adminService.setPrimaryContact(admin.registrationId).subscribe({
            next: admins => {
                const wasPrimary = admin.isPrimaryContact;
                this.administrators.set(admins);
                this.toast.show(
                    wasPrimary
                        ? `${admin.administratorName} removed as primary contact.`
                        : `${admin.administratorName} set as primary contact.`,
                    'success'
                );
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to update primary contact.', 'danger', 4000);
            }
        });
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
