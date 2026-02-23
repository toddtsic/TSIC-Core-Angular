import { ChangeDetectionStrategy, Component, HostListener, OnInit, inject, signal, computed, isDevMode } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NavAdminService } from '../../core/services/nav-admin.service';
import { NavItemFormDialogComponent, NavItemFormResult } from './nav-item-form-dialog.component';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { ToastService } from '@shared-ui/toast.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import type { NavEditorNavDto, NavEditorNavItemDto, CreateNavItemRequest, UpdateNavItemRequest } from '@core/api';

@Component({
    selector: 'app-menu-admin',
    standalone: true,
    imports: [FormsModule, NavItemFormDialogComponent, TsicDialogComponent],
    templateUrl: './menu-admin.component.html',
    styleUrl: './menu-admin.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class MenuAdminComponent implements OnInit {
    private readonly navAdminService = inject(NavAdminService);
    private readonly toast = inject(ToastService);
    private readonly auth = inject(AuthService);
    private readonly jobService = inject(JobService);
    readonly isDevMode = isDevMode();

    // Component state
    selectedRoleId = signal<string | null>(null);
    expandedItems = signal<Set<number>>(new Set());

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

    // Move dropdown state — tracks which child item's dropdown is open
    moveDropdownItemId = signal<number | null>(null);

    // Computed values
    navs = computed(() => this.navAdminService.navs());
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

    @HostListener('document:click')
    onDocumentClick(): void {
        this.closeMoveDropdown();
    }

    ngOnInit(): void {
        this.navAdminService.ensureAndLoad().subscribe({
            next: (navs) => {
                if (navs.length > 0 && !this.selectedRoleId()) {
                    this.selectBestDefault(navs);
                }
            }
        });
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
            this.navAdminService.createItem(result.data as CreateNavItemRequest).subscribe({
                next: () => { this.toast.show('Nav item created.', 'success'); this.loadNavs(); },
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
                    const updateAll = confirm(
                        `"${oldItem.text}" also exists in: ${roleNames}\n\n` +
                        `Update route to "${newData.routerLink}" for all roles?`
                    );
                    if (updateAll) {
                        this.updateWithCascade(result.navItemId!, newData, matches.length);
                        return;
                    }
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
            if (parent.navItemId === item.parentNavItemId) return parent.text;
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
        const confirmDelete = confirm(`Delete "${item.text}"?`);
        if (!confirmDelete) return;

        this.navAdminService.deleteItem(item.navItemId).subscribe({
            next: () => { this.toast.show('Nav item deleted.', 'success'); this.loadNavs(); },
            error: () => this.toast.show('Failed to delete nav item.', 'danger')
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
            i => i.text.toLowerCase() === source.text.toLowerCase()
        );

        let replaceExisting = false;
        if (duplicate) {
            const confirmed = confirm(
                `"${targetNav.roleName}" already has a "${source.text}" section.\n\n` +
                `Replace it and its children with the cloned version?`
            );
            if (!confirmed) return;
            replaceExisting = true;
        }

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

    moveItemUp(item: NavEditorNavItemDto, siblings: NavEditorNavItemDto[]): void {
        const currentIndex = siblings.findIndex(s => s.navItemId === item.navItemId);
        if (currentIndex <= 0) return;

        const reordered = [...siblings];
        [reordered[currentIndex - 1], reordered[currentIndex]] = [reordered[currentIndex], reordered[currentIndex - 1]];

        this.reorderSiblings(reordered);
    }

    moveItemDown(item: NavEditorNavItemDto, siblings: NavEditorNavItemDto[]): void {
        const currentIndex = siblings.findIndex(s => s.navItemId === item.navItemId);
        if (currentIndex < 0 || currentIndex >= siblings.length - 1) return;

        const reordered = [...siblings];
        [reordered[currentIndex], reordered[currentIndex + 1]] = [reordered[currentIndex + 1], reordered[currentIndex]];

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
}
