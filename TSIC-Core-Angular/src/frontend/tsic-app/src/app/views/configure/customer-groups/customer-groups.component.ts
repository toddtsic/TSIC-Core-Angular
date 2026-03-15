import { Component, inject, signal, computed, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { ToastService } from '@shared-ui/toast.service';
import { CustomerGroupsService } from './customer-groups.service';
import type { CustomerGroupDto, CustomerGroupMemberDto, CustomerLookupDto } from '@core/api';

@Component({
    selector: 'app-customer-groups',
    standalone: true,
    imports: [CommonModule, FormsModule, ConfirmDialogComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './customer-groups.component.html',
    styleUrl: './customer-groups.component.scss'
})
export class CustomerGroupsComponent {
    private readonly service = inject(CustomerGroupsService);
    private readonly toast = inject(ToastService);

    // ── Data signals ──────────────────────────────────
    readonly groups = signal<CustomerGroupDto[]>([]);
    readonly selectedGroup = signal<CustomerGroupDto | null>(null);
    readonly members = signal<CustomerGroupMemberDto[]>([]);
    readonly availableCustomers = signal<CustomerLookupDto[]>([]);

    // ── UI state ──────────────────────────────────────
    readonly isLoading = signal(false);
    readonly isSaving = signal(false);

    // ── Create group ──────────────────────────────────
    readonly showCreateInput = signal(false);
    readonly newGroupName = signal('');

    // ── Rename group ──────────────────────────────────
    readonly showRenameInput = signal(false);
    readonly renameValue = signal('');

    // ── Delete confirm ────────────────────────────────
    readonly showDeleteConfirm = signal(false);

    // ── Add member ────────────────────────────────────
    readonly selectedCustomerId = signal('');

    // ── Computed ──────────────────────────────────────
    readonly canDelete = computed(() => {
        const group = this.selectedGroup();
        return group !== null && group.memberCount === 0;
    });

    constructor() {
        this.loadGroups();
    }

    // ── Group operations ──────────────────────────────

    loadGroups(): void {
        this.isLoading.set(true);
        this.service.getGroups().subscribe({
            next: groups => {
                this.groups.set(groups);
                this.isLoading.set(false);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to load groups', 'danger');
                this.isLoading.set(false);
            }
        });
    }

    selectGroup(group: CustomerGroupDto): void {
        this.selectedGroup.set(group);
        this.loadGroupDetails(group.id);
    }

    private loadGroupDetails(groupId: number): void {
        this.service.getMembers(groupId).subscribe({
            next: members => this.members.set(members),
            error: err => this.toast.show(err?.error?.message || 'Failed to load members', 'danger')
        });
        this.service.getAvailableCustomers(groupId).subscribe({
            next: customers => this.availableCustomers.set(customers),
            error: err => this.toast.show(err?.error?.message || 'Failed to load customers', 'danger')
        });
    }

    // ── Create ────────────────────────────────────────

    openCreateInput(): void {
        this.showCreateInput.set(true);
        this.newGroupName.set('');
    }

    cancelCreate(): void {
        this.showCreateInput.set(false);
        this.newGroupName.set('');
    }

    submitCreate(): void {
        const name = this.newGroupName().trim();
        if (!name) return;

        this.isSaving.set(true);
        this.service.createGroup({ customerGroupName: name }).subscribe({
            next: group => {
                this.toast.show('Group created', 'success');
                this.showCreateInput.set(false);
                this.newGroupName.set('');
                this.isSaving.set(false);
                this.loadGroups();
                this.selectGroup(group);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to create group', 'danger');
                this.isSaving.set(false);
            }
        });
    }

    // ── Rename ────────────────────────────────────────

    openRename(): void {
        const group = this.selectedGroup();
        if (!group) return;
        this.renameValue.set(group.customerGroupName);
        this.showRenameInput.set(true);
    }

    cancelRename(): void {
        this.showRenameInput.set(false);
        this.renameValue.set('');
    }

    submitRename(): void {
        const group = this.selectedGroup();
        if (!group) return;
        const name = this.renameValue().trim();
        if (!name) return;

        this.isSaving.set(true);
        this.service.renameGroup(group.id, { customerGroupName: name }).subscribe({
            next: updated => {
                this.toast.show('Group renamed', 'success');
                this.showRenameInput.set(false);
                this.renameValue.set('');
                this.isSaving.set(false);
                this.selectedGroup.set(updated);
                this.loadGroups();
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to rename group', 'danger');
                this.isSaving.set(false);
            }
        });
    }

    // ── Delete ────────────────────────────────────────

    confirmDelete(): void {
        this.showDeleteConfirm.set(true);
    }

    onDeleteConfirmed(): void {
        const group = this.selectedGroup();
        if (!group) return;

        this.isSaving.set(true);
        this.service.deleteGroup(group.id).subscribe({
            next: () => {
                this.toast.show('Group deleted', 'success');
                this.showDeleteConfirm.set(false);
                this.selectedGroup.set(null);
                this.members.set([]);
                this.availableCustomers.set([]);
                this.isSaving.set(false);
                this.loadGroups();
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Cannot delete group', 'danger');
                this.showDeleteConfirm.set(false);
                this.isSaving.set(false);
            }
        });
    }

    // ── Member operations ─────────────────────────────

    addMember(): void {
        const group = this.selectedGroup();
        const customerId = this.selectedCustomerId();
        if (!group || !customerId) return;

        this.isSaving.set(true);
        this.service.addMember(group.id, { customerId }).subscribe({
            next: () => {
                this.toast.show('Member added', 'success');
                this.selectedCustomerId.set('');
                this.isSaving.set(false);
                this.loadGroupDetails(group.id);
                // Update member count in groups list
                this.groups.update(list =>
                    list.map(g => g.id === group.id ? { ...g, memberCount: g.memberCount + 1 } : g)
                );
                this.selectedGroup.update(g => g ? { ...g, memberCount: g.memberCount + 1 } : g);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to add member', 'danger');
                this.isSaving.set(false);
            }
        });
    }

    removeMember(member: CustomerGroupMemberDto): void {
        const group = this.selectedGroup();
        if (!group) return;

        this.isSaving.set(true);
        this.service.removeMember(group.id, member.id).subscribe({
            next: () => {
                this.toast.show('Member removed', 'success');
                this.isSaving.set(false);
                this.loadGroupDetails(group.id);
                // Update member count in groups list
                this.groups.update(list =>
                    list.map(g => g.id === group.id ? { ...g, memberCount: Math.max(0, g.memberCount - 1) } : g)
                );
                this.selectedGroup.update(g => g ? { ...g, memberCount: Math.max(0, g.memberCount - 1) } : g);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to remove member', 'danger');
                this.isSaving.set(false);
            }
        });
    }
}
