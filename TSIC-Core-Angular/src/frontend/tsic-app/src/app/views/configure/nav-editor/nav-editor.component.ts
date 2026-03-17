import { ChangeDetectionStrategy, Component, HostListener, OnInit, inject, signal, computed, isDevMode } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NavAdminService, DeleteNavItemConflict } from '../../../core/services/nav-admin.service';
import { NavItemFormDialogComponent, NavItemFormResult } from './nav-item-form-dialog.component';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { ToastService } from '@shared-ui/toast.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import type { NavEditorNavDto, NavEditorNavItemDto, CreateNavItemRequest, UpdateNavItemRequest, ToggleHideRequest } from '@core/api';

@Component({
    selector: 'app-nav-editor',
    standalone: true,
    imports: [FormsModule, NavItemFormDialogComponent, TsicDialogComponent, ConfirmDialogComponent],
    templateUrl: './nav-editor.component.html',
    styleUrl: './nav-editor.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class NavEditorComponent implements OnInit {
    private readonly navAdminService = inject(NavAdminService);
    private readonly toast = inject(ToastService);
    private readonly auth = inject(AuthService);
    private readonly jobService = inject(JobService);
    readonly isDevMode = isDevMode();

    // Tab state
    activeTab = signal<'defaults' | 'this-job'>('defaults');

    // Component state
    selectedRoleId = signal<string | null>(null);
    expandedItems = signal<Set<number>>(new Set());

    // This Job tab state
    jobSelectedRoleId = signal<string | null>(null);
    jobExpandedItems = signal<Set<number>>(new Set());

    // Edit dialog state
    editDialogOpen = signal(false);
    editNavId = signal(0);
    editParentNavItemId = signal<number | undefined>(undefined);
    editExistingItem = signal<NavEditorNavItemDto | undefined>(undefined);

    // Export dialog state
    exportDialogOpen = signal(false);
    exportedSql = signal('');
    exportLoading = signal(false);
    copySuccess = signal(false);

    // Clone dialog state
    cloneDialogOpen = signal(false);
    cloneSourceItem = signal<NavEditorNavItemDto | null>(null);
    cloneTargetNavId = signal<number | null>(null);
    cloneLoading = signal(false);

    // Move dropdown state — tracks which child item's "move to group" dropdown is open
    moveDropdownItemId = signal<number | null>(null);

    // Reorder dropdown state — tracks which item's "move N spots" dropdown is open
    reorderDropdownItemId = signal<number | null>(null);

    // Confirm dialog state
    confirmDialogOpen = signal(false);
    confirmDialogTitle = signal('');
    confirmDialogMessage = signal('');
    confirmDialogVariant = signal<'danger' | 'warning' | 'primary'>('primary');
    confirmDialogLabel = signal('Confirm');
    private confirmDialogAction = signal<(() => void) | null>(null);

    // Pending data for cascade confirm
    private pendingCascadeItemId = signal<number | null>(null);
    private pendingCascadeData = signal<UpdateNavItemRequest | null>(null);
    private pendingCascadeMatchCount = signal(0);

    // Pending data for delete confirm
    private pendingDeleteItem = signal<NavEditorNavItemDto | null>(null);
    private pendingForceDeleteItemId = signal<number | null>(null);

    // "Add under default section" in This Job tab
    pendingJobAddSection = signal<NavEditorNavItemDto | null>(null);

    // Computed values
    navs = computed(() => this.navAdminService.navs());
    jobOverrides = computed(() => this.navAdminService.jobOverrides());
    isLoading = computed(() => this.navAdminService.isLoading());

    selectedNav = computed(() => {
        const roleId = this.selectedRoleId();
        if (!roleId) return null;
        return this.navs().find(n => n.roleId === roleId) ?? null;
    });

    /** Navs excluding the currently selected one — for clone target picker. */
    cloneTargetNavs = computed(() => {
        const current = this.selectedNav();
        if (!current) return [];
        return this.navs().filter(n => n.navId !== current.navId);
    });

    /** The job override nav for the currently selected role in the "This Job" tab. */
    selectedJobOverrideNav = computed(() => {
        const roleId = this.jobSelectedRoleId();
        if (!roleId) return null;
        return this.jobOverrides().find(n => n.roleId === roleId) ?? null;
    });

    /** The platform default nav for the currently selected role in the "This Job" tab. */
    selectedDefaultForJobRole = computed(() => {
        const roleId = this.jobSelectedRoleId();
        if (!roleId) return null;
        return this.navs().find(n => n.roleId === roleId) ?? null;
    });

    /**
     * Build a set of suppressed default NavItemIds from the current job override nav.
     * An item is suppressed when a hide row (defaultNavItemId set, active=false) exists for it.
     */
    suppressedDefaultIds = computed((): Set<number> => {
        const override = this.selectedJobOverrideNav();
        const result = new Set<number>();
        if (!override) return result;
        for (const item of override.items) {
            if (item.defaultNavItemId != null && !item.active) {
                result.add(item.defaultNavItemId);
            }
            for (const child of item.children ?? []) {
                if (child.defaultNavItemId != null && !child.active) {
                    result.add(child.defaultNavItemId);
                }
            }
        }
        return result;
    });

    @HostListener('document:click')
    onDocumentClick(): void {
        this.closeMoveDropdown();
        this.closeReorderDropdown();
    }

    ngOnInit(): void {
        this.navAdminService.ensureAndLoad().subscribe({
            next: (navs) => {
                if (navs.length > 0 && !this.selectedRoleId()) {
                    this.selectBestDefault(navs);
                }
                if (!this.jobSelectedRoleId() && navs.length > 0) {
                    this.jobSelectedRoleId.set(navs[0].roleId);
                }
            }
        });
        this.navAdminService.loadJobOverrides();
    }

    loadNavs(): void {
        this.navAdminService.loadNavs().subscribe({
            next: (navs) => {
                if (navs.length > 0 && !this.selectedRoleId()) {
                    this.selectBestDefault(navs);
                }
                // Refresh the live menu so changes are visible immediately
                this.jobService.loadNav();
            }
        });
        this.navAdminService.loadJobOverrides();
    }

    switchTab(tab: 'defaults' | 'this-job'): void {
        this.activeTab.set(tab);
        // Keep role selection in sync between tabs
        if (tab === 'this-job' && this.selectedRoleId()) {
            this.jobSelectedRoleId.set(this.selectedRoleId());
            this.jobExpandedItems.set(new Set());
        } else if (tab === 'defaults' && this.jobSelectedRoleId()) {
            this.selectedRoleId.set(this.jobSelectedRoleId());
            this.expandedItems.set(new Set());
        }
    }

    onJobRoleChange(roleId: string): void {
        this.jobSelectedRoleId.set(roleId);
        this.jobExpandedItems.set(new Set());
    }

    toggleJobItemExpanded(itemId: number): void {
        const expanded = new Set(this.jobExpandedItems());
        if (expanded.has(itemId)) {
            expanded.delete(itemId);
        } else {
            expanded.add(itemId);
        }
        this.jobExpandedItems.set(expanded);
    }

    isJobItemExpanded(itemId: number): boolean {
        return this.jobExpandedItems().has(itemId);
    }

    /**
     * Toggle hide/show for a platform default item in this job's override nav.
     */
    toggleHideItem(item: NavEditorNavItemDto, hide: boolean): void {
        const roleId = this.jobSelectedRoleId();
        if (!roleId) return;

        if (hide && !item.parentNavItemId && item.children && item.children.length > 0) {
            const childCount = item.children.filter(c => c.active).length;
            if (childCount > 0) {
                this.showConfirm(
                    'Hide Entire Section',
                    `Hiding "<strong>${item.text}</strong>" will also suppress its ${childCount} child item${childCount > 1 ? 's' : ''} for this job. Continue?`,
                    'warning',
                    'Hide Section',
                    () => this.doToggleHide(item, roleId, hide)
                );
                return;
            }
        }

        this.doToggleHide(item, roleId, hide);
    }

    /**
     * Open the create-item dialog to add a job-specific child under a default section.
     * Creates the override nav for this role on demand if it doesn't exist yet.
     */
    addJobOverrideItem(sectionItem: NavEditorNavItemDto): void {
        const roleId = this.jobSelectedRoleId();
        if (!roleId) return;

        this.pendingJobAddSection.set(sectionItem);

        const overrideNav = this.selectedJobOverrideNav();

        const openDialog = (navId: number) => {
            this.editNavId.set(navId);
            this.editParentNavItemId.set(undefined); // no parent in override nav
            this.editExistingItem.set(undefined);
            this.editDialogOpen.set(true);
        };

        if (overrideNav) {
            openDialog(overrideNav.navId);
        } else {
            this.navAdminService.ensureJobOverrideNav(roleId).subscribe({
                next: (navId) => {
                    this.navAdminService.loadJobOverrides();
                    openDialog(navId);
                },
                error: () => this.toast.show('Failed to initialize job override nav.', 'danger')
            });
        }
    }

    private doToggleHide(item: NavEditorNavItemDto, roleId: string, hide: boolean): void {
        const req: ToggleHideRequest = {
            roleId,
            defaultNavItemId: item.navItemId,
            hide
        };
        this.navAdminService.toggleHide(req).subscribe({
            next: () => {
                this.toast.show(hide ? `"${item.text}" hidden for this job.` : `"${item.text}" restored for this job.`, 'success');
                this.navAdminService.loadJobOverrides();
                this.jobService.loadNav();
            },
            error: (err) => this.toast.show(`Failed: ${err.error?.message || err.statusText}`, 'danger')
        });
    }

    private selectBestDefault(navs: NavEditorNavDto[]): void {
        // 1. Prefer nav matching current user's role
        const userRole = this.auth.currentUser()?.role;
        const forCurrentRole = userRole
            ? navs.find(n => n.roleName?.toLowerCase() === userRole.toLowerCase())
            : null;

        // 2. Fall back to first nav with items, then first nav
        const pick = forCurrentRole
            ?? navs.find(n => n.items && n.items.length > 0)
            ?? navs[0];

        if (pick?.roleId) {
            this.selectedRoleId.set(pick.roleId);
        }
    }

    onRoleChange(roleId: string): void {
        this.selectedRoleId.set(roleId);
        this.expandedItems.set(new Set());
    }

    toggleNavActive(nav: NavEditorNavDto): void {
        this.navAdminService.toggleNavActive(nav.navId, !nav.active).subscribe({
            next: () => this.loadNavs()
        });
    }

    toggleItemExpanded(itemId: number): void {
        const expanded = new Set(this.expandedItems());
        if (expanded.has(itemId)) {
            expanded.delete(itemId);
        } else {
            expanded.add(itemId);
        }
        this.expandedItems.set(expanded);
    }

    isItemExpanded(itemId: number): boolean {
        return this.expandedItems().has(itemId);
    }

    // ── Dialog operations ──

    createParentItem(): void {
        const nav = this.selectedNav();
        if (!nav) return;

        this.editNavId.set(nav.navId);
        this.editParentNavItemId.set(undefined);
        this.editExistingItem.set(undefined);
        this.editDialogOpen.set(true);
    }

    createChildItem(parentItem: NavEditorNavItemDto): void {
        const nav = this.selectedNav();
        if (!nav) return;

        this.editNavId.set(nav.navId);
        this.editParentNavItemId.set(parentItem.navItemId);
        this.editExistingItem.set(undefined);
        this.editDialogOpen.set(true);
    }

    editItem(item: NavEditorNavItemDto): void {
        const nav = this.selectedNav();
        if (!nav) return;

        this.editNavId.set(nav.navId);
        this.editParentNavItemId.set(item.parentNavItemId ?? undefined);
        this.editExistingItem.set(item);
        this.editDialogOpen.set(true);
    }

    onItemSaved(result: NavItemFormResult): void {
        this.editDialogOpen.set(false);

        if (result.type === 'create') {
            const jobSection = this.pendingJobAddSection();
            this.pendingJobAddSection.set(null);

            const createRequest: CreateNavItemRequest = jobSection
                ? { ...(result.data as CreateNavItemRequest), defaultParentNavItemId: jobSection.navItemId }
                : result.data as CreateNavItemRequest;

            this.navAdminService.createItem(createRequest).subscribe({
                next: () => {
                    this.toast.show('Nav item created.', 'success');
                    if (jobSection) {
                        this.navAdminService.loadJobOverrides();
                        this.jobService.loadNav();
                    } else {
                        this.loadNavs();
                    }
                },
                error: (err) => {
                    console.error('Create nav item failed:', err);
                    this.toast.show(`Failed to create: ${err.status} ${err.statusText}`, 'danger');
                }
            });
        } else {
            const oldItem = this.editExistingItem();
            const newData = result.data as UpdateNavItemRequest;
            const routeChanged = oldItem && oldItem.routerLink !== newData.routerLink;

            // Check for matching items across other roles when route changes
            if (routeChanged) {
                const matches = this.findMatchingItemsAcrossRoles(oldItem);
                if (matches.length > 0) {
                    const roleNames = matches.map(m => m.roleName).join(', ');
                    this.pendingCascadeItemId.set(result.navItemId!);
                    this.pendingCascadeData.set(newData);
                    this.pendingCascadeMatchCount.set(matches.length);
                    this.showConfirm(
                        'Cascade Route Update',
                        `"${oldItem.text}" also exists in: <strong>${roleNames}</strong>.<br><br>Update route to "${newData.routerLink}" for all roles?`,
                        'primary',
                        'Update All',
                        () => {
                            const id = this.pendingCascadeItemId();
                            const data = this.pendingCascadeData();
                            const count = this.pendingCascadeMatchCount();
                            if (id && data) this.updateWithCascade(id, data, count);
                        }
                    );
                    return;
                }
            }

            this.navAdminService.updateItem(result.navItemId!, newData).subscribe({
                next: () => { this.toast.show('Nav item updated.', 'success'); this.loadNavs(); },
                error: (err) => {
                    console.error('Update nav item failed:', err);
                    this.toast.show(`Failed to update: ${err.status} ${err.statusText}`, 'danger');
                }
            });
        }
    }

    /**
     * Find role names that have matching nav items (same text + parent text) across other roles.
     * Used to build the confirm dialog — actual cascade is done server-side.
     */
    private findMatchingItemsAcrossRoles(item: NavEditorNavItemDto): Array<{ roleName: string }> {
        const currentNav = this.selectedNav();
        if (!currentNav) return [];

        const parentText = this.getParentTextForItem(item, currentNav);
        const matches: Array<{ roleName: string }> = [];

        for (const nav of this.navs()) {
            if (nav.navId === currentNav.navId) continue;

            for (const parent of nav.items) {
                // Match top-level items
                if (!item.parentNavItemId && parent.text === item.text) {
                    matches.push({ roleName: nav.roleName ?? 'Unknown' });
                    continue;
                }

                // Match child items under same parent text
                if (item.parentNavItemId && parent.text === parentText && parent.children) {
                    for (const child of parent.children) {
                        if (child.text === item.text) {
                            matches.push({ roleName: nav.roleName ?? 'Unknown' });
                        }
                    }
                }
            }
        }

        return matches;
    }

    /**
     * Get the parent item's text for a child nav item.
     */
    private getParentTextForItem(item: NavEditorNavItemDto, nav: NavEditorNavDto): string | null {
        if (!item.parentNavItemId) return null;
        for (const parent of nav.items) {
            if (parent.navItemId === item.parentNavItemId) return parent.text ?? null;
        }
        return null;
    }

    /**
     * Update the primary item, then cascade the route change to matching items
     * across all roles in a single backend transaction.
     */
    private updateWithCascade(
        primaryId: number,
        newData: UpdateNavItemRequest,
        matchCount: number
    ): void {
        // Update the primary item first
        this.navAdminService.updateItem(primaryId, newData).subscribe({
            next: () => {
                // Cascade route to all matching items in one backend call
                this.navAdminService.cascadeRoute({
                    navItemId: primaryId,
                    routerLink: newData.routerLink,
                    navigateUrl: newData.navigateUrl,
                    target: newData.target
                }).subscribe({
                    next: (res) => {
                        this.toast.show(`Route updated across ${res.updated + 1} roles.`, 'success');
                        this.loadNavs();
                    },
                    error: (err) => {
                        console.error('Cascade route failed:', err);
                        this.toast.show(`Primary updated, but cascade failed: ${err.status}`, 'danger');
                        this.loadNavs();
                    }
                });
            },
            error: (err) => {
                console.error('Update nav item failed:', err);
                this.toast.show(`Failed to update: ${err.status} ${err.statusText}`, 'danger');
            }
        });
    }

    deleteItem(item: NavEditorNavItemDto): void {
        this.pendingDeleteItem.set(item);
        this.pendingForceDeleteItemId.set(null);
        this.showConfirm(
            'Delete Nav Item',
            `Delete "<strong>${item.text}</strong>"?`,
            'danger',
            'Delete',
            () => this.executeDelete(item.navItemId, false)
        );
    }

    private executeDelete(navItemId: number, force: boolean): void {
        this.navAdminService.deleteItem(navItemId, force).subscribe({
            next: () => {
                this.pendingDeleteItem.set(null);
                this.pendingForceDeleteItemId.set(null);
                this.toast.show('Nav item deleted.', 'success');
                this.loadNavs();
            },
            error: (err) => {
                if (err.status === 409) {
                    const conflict = err.error as DeleteNavItemConflict;
                    this.pendingForceDeleteItemId.set(navItemId);
                    this.showConfirm(
                        'Confirm Delete',
                        conflict.message || `${conflict.affectedCount} job override(s) reference this item and will also be deleted.`,
                        'danger',
                        'Delete Anyway',
                        () => {
                            const id = this.pendingForceDeleteItemId();
                            if (id != null) this.executeDelete(id, true);
                        }
                    );
                } else {
                    this.toast.show('Failed to delete nav item.', 'danger');
                }
            }
        });
    }

    // ── Clone branch ──

    openCloneDialog(parentItem: NavEditorNavItemDto): void {
        this.cloneSourceItem.set(parentItem);
        this.cloneTargetNavId.set(null);
        this.cloneDialogOpen.set(true);
    }

    cloneBranch(): void {
        const source = this.cloneSourceItem();
        const targetNavId = this.cloneTargetNavId();
        if (!source || !targetNavId) return;

        const targetNav = this.navs().find(n => n.navId === targetNavId);
        if (!targetNav) return;

        // Client-side duplicate detection
        const duplicate = targetNav.items.find(
            i => (i.text ?? '').toLowerCase() === (source.text ?? '').toLowerCase()
        );

        if (duplicate) {
            this.showConfirm(
                'Replace Existing Section',
                `"<strong>${targetNav.roleName}</strong>" already has a "<strong>${source.text}</strong>" section.<br><br>Replace it and its children with the cloned version?`,
                'warning',
                'Replace',
                () => this.executeClone(source, targetNavId, targetNav, true)
            );
            return;
        }

        this.executeClone(source, targetNavId, targetNav, false);
    }

    private executeClone(
        source: NavEditorNavItemDto,
        targetNavId: number,
        targetNav: NavEditorNavDto,
        replaceExisting: boolean
    ): void {
        this.cloneLoading.set(true);

        this.navAdminService.cloneBranch({
            sourceNavItemId: source.navItemId,
            targetNavId: targetNavId,
            replaceExisting: replaceExisting
        }).subscribe({
            next: (count) => {
                this.toast.show(
                    `Cloned "${source.text}" (${count} items) to ${targetNav.roleName}.`,
                    'success'
                );
                this.cloneDialogOpen.set(false);
                this.cloneLoading.set(false);
                this.loadNavs();
            },
            error: (err) => {
                console.error('Clone branch failed:', err);
                this.toast.show(
                    `Clone failed: ${err.error?.message || err.statusText}`,
                    'danger'
                );
                this.cloneLoading.set(false);
            }
        });
    }

    // ── Reorder dropdown ──

    toggleReorderDropdown(itemId: number, event: MouseEvent): void {
        event.stopPropagation();
        this.closeMoveDropdown();
        this.reorderDropdownItemId.set(
            this.reorderDropdownItemId() === itemId ? null : itemId
        );
    }

    closeReorderDropdown(): void {
        this.reorderDropdownItemId.set(null);
    }

    /** Build move options based on current position within siblings. */
    getMoveOptions(item: NavEditorNavItemDto, siblings: NavEditorNavItemDto[]): Array<{ label: string; spots: number }> {
        const idx = siblings.findIndex(s => s.navItemId === item.navItemId);
        if (idx < 0) return [];

        const options: Array<{ label: string; spots: number }> = [];

        // Up options (negative spots)
        for (let i = idx; i >= 1; i--) {
            options.push({ label: `Up ${i}`, spots: -i });
        }

        // Down options (positive spots)
        const maxDown = siblings.length - 1 - idx;
        for (let i = 1; i <= maxDown; i++) {
            options.push({ label: `Down ${i}`, spots: i });
        }

        return options;
    }

    moveItemByN(item: NavEditorNavItemDto, siblings: NavEditorNavItemDto[], spots: number): void {
        this.closeReorderDropdown();

        const currentIndex = siblings.findIndex(s => s.navItemId === item.navItemId);
        if (currentIndex < 0) return;

        const targetIndex = currentIndex + spots;
        if (targetIndex < 0 || targetIndex >= siblings.length) return;

        const reordered = [...siblings];
        reordered.splice(currentIndex, 1);
        reordered.splice(targetIndex, 0, item);

        this.reorderSiblings(reordered);
    }

    private reorderSiblings(siblings: NavEditorNavItemDto[]): void {
        const nav = this.selectedNav();
        if (!nav) return;

        const firstItem = siblings[0];
        this.navAdminService.reorderItems({
            navId: nav.navId,
            parentNavItemId: firstItem.parentNavItemId ?? undefined,
            orderedItemIds: siblings.map(s => s.navItemId)
        }).subscribe({
            next: () => this.loadNavs()
        });
    }

    // ── Move to group ──

    /** Returns Level 1 items from the current nav excluding the child's current parent. */
    getOtherParentItems(childItem: NavEditorNavItemDto): NavEditorNavItemDto[] {
        const nav = this.selectedNav();
        if (!nav) return [];
        return nav.items.filter(p => p.navItemId !== childItem.parentNavItemId);
    }

    toggleMoveDropdown(childItemId: number, event: MouseEvent): void {
        event.stopPropagation();
        this.closeReorderDropdown();
        this.moveDropdownItemId.set(
            this.moveDropdownItemId() === childItemId ? null : childItemId
        );
    }

    closeMoveDropdown(): void {
        this.moveDropdownItemId.set(null);
    }

    moveItemToGroup(childItem: NavEditorNavItemDto, targetParent: NavEditorNavItemDto): void {
        this.moveDropdownItemId.set(null);

        this.navAdminService.moveItem(childItem.navItemId, targetParent.navItemId).subscribe({
            next: () => {
                this.toast.show(`Moved "${childItem.text}" to "${targetParent.text}".`, 'success');
                this.loadNavs();
            },
            error: (err) => {
                console.error('Move item failed:', err);
                this.toast.show(`Move failed: ${err.error?.message || err.statusText}`, 'danger');
            }
        });
    }

    // ── Export SQL ──

    exportSql(): void {
        this.exportLoading.set(true);
        this.exportedSql.set('');
        this.copySuccess.set(false);
        this.exportDialogOpen.set(true);

        this.navAdminService.exportSql().subscribe({
            next: (sql) => {
                this.exportedSql.set(sql);
                this.exportLoading.set(false);
            },
            error: () => {
                this.exportedSql.set('-- Error generating SQL export');
                this.exportLoading.set(false);
            }
        });
    }

    copyToClipboard(): void {
        navigator.clipboard.writeText(this.exportedSql()).then(() => {
            this.copySuccess.set(true);
            setTimeout(() => this.copySuccess.set(false), 2000);
        });
    }

    // ── Confirm dialog helpers ──

    private showConfirm(
        title: string,
        message: string,
        variant: 'danger' | 'warning' | 'primary',
        label: string,
        action: () => void
    ): void {
        this.confirmDialogTitle.set(title);
        this.confirmDialogMessage.set(message);
        this.confirmDialogVariant.set(variant);
        this.confirmDialogLabel.set(label);
        this.confirmDialogAction.set(action);
        this.confirmDialogOpen.set(true);
    }

    onConfirmDialogConfirmed(): void {
        const action = this.confirmDialogAction();
        this.confirmDialogOpen.set(false);
        this.confirmDialogAction.set(null);
        action?.();
    }

    onConfirmDialogCancelled(): void {
        this.confirmDialogOpen.set(false);
        this.confirmDialogAction.set(null);

        // For cascade decline — still update the single item (matches original behavior)
        const cascadeId = this.pendingCascadeItemId();
        const cascadeData = this.pendingCascadeData();
        if (cascadeId && cascadeData) {
            this.pendingCascadeItemId.set(null);
            this.pendingCascadeData.set(null);
            this.navAdminService.updateItem(cascadeId, cascadeData).subscribe({
                next: () => { this.toast.show('Nav item updated.', 'success'); this.loadNavs(); },
                error: (err) => {
                    console.error('Update nav item failed:', err);
                    this.toast.show(`Failed to update: ${err.status} ${err.statusText}`, 'danger');
                }
            });
            return;
        }

        this.pendingDeleteItem.set(null);
        this.pendingForceDeleteItemId.set(null);
    }
}
