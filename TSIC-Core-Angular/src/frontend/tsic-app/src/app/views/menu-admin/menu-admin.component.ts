import { ChangeDetectionStrategy, Component, OnInit, inject, signal, computed, isDevMode } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { switchMap } from 'rxjs';
import { NavAdminService } from '../../core/services/nav-admin.service';
import { NavItemFormDialogComponent, NavItemFormResult } from './nav-item-form-dialog.component';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
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

    // Computed values
    navs = computed(() => this.navAdminService.navs());
    isLoading = computed(() => this.navAdminService.isLoading());

    selectedNav = computed(() => {
        const roleId = this.selectedRoleId();
        if (!roleId) return null;
        return this.navs().find(n => n.roleId === roleId) ?? null;
    });

    ngOnInit(): void {
        this.navAdminService.ensureAndLoad().subscribe({
            next: (navs) => {
                if (navs.length > 0 && !this.selectedRoleId() && navs[0].roleId) {
                    this.selectedRoleId.set(navs[0].roleId);
                }
            }
        });
    }

    loadNavs(): void {
        this.navAdminService.loadNavs().subscribe({
            next: (navs) => {
                if (navs.length > 0 && !this.selectedRoleId() && navs[0].roleId) {
                    this.selectedRoleId.set(navs[0].roleId);
                }
            }
        });
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
                next: () => this.loadNavs()
            });
        } else {
            this.navAdminService.updateItem(result.navItemId!, result.data as UpdateNavItemRequest).subscribe({
                next: () => this.loadNavs()
            });
        }
    }

    deleteItem(item: NavEditorNavItemDto): void {
        const confirmDelete = confirm(`Delete "${item.text}"?`);
        if (!confirmDelete) return;

        this.navAdminService.deleteItem(item.navItemId).subscribe({
            next: () => this.loadNavs()
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
