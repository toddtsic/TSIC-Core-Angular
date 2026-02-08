import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { NgbModal } from '@ng-bootstrap/ng-bootstrap';
import { MenuAdminService } from '../../core/services/menu-admin.service';
import { MenuItemFormModalComponent } from '../../shared/modals/menu-item-form-modal.component';
import type { MenuAdminDto, MenuItemAdminDto } from '@core/api';

@Component({
    selector: 'app-menu-admin',
    standalone: true,
    imports: [CommonModule, FormsModule],
    templateUrl: './menu-admin.component.html',
    styleUrl: './menu-admin.component.scss'
})
export class MenuAdminComponent implements OnInit {
    private readonly menuAdminService = inject(MenuAdminService);
    private readonly modalService = inject(NgbModal);

    // Component state
    selectedRoleId = signal<string | null>(null);
    expandedItems = signal<Set<string>>(new Set());

    // Computed values
    menus = computed(() => this.menuAdminService.menus());
    isLoading = computed(() => this.menuAdminService.isLoading());

    selectedMenu = computed(() => {
        const roleId = this.selectedRoleId();
        if (!roleId) return null;
        return this.menus().find(m => m.roleId === roleId);
    });

    ngOnInit(): void {
        this.loadMenus();
    }

    loadMenus(): void {
        this.menuAdminService.loadMenus().subscribe({
            next: (menus) => {
                // Auto-select first role if available
                if (menus.length > 0 && !this.selectedRoleId() && menus[0].roleId) {
                    this.selectedRoleId.set(menus[0].roleId);
                }
            }
        });
    }

    onRoleChange(roleId: string): void {
        this.selectedRoleId.set(roleId);
        this.expandedItems.set(new Set()); // Collapse all when switching roles
    }

    toggleMenuActive(menu: MenuAdminDto): void {
        this.menuAdminService.toggleMenuActive(menu.menuId, !menu.active).subscribe({
            next: () => this.loadMenus()
        });
    }

    toggleItemExpanded(itemId: string): void {
        const expanded = new Set(this.expandedItems());
        if (expanded.has(itemId)) {
            expanded.delete(itemId);
        } else {
            expanded.add(itemId);
        }
        this.expandedItems.set(expanded);
    }

    isItemExpanded(itemId: string): boolean {
        return this.expandedItems().has(itemId);
    }

    ensureAllRoleMenus(): void {
        this.menuAdminService.ensureAllRoleMenus().subscribe({
            next: ({ created }) => {
                if (created > 0) {
                    alert(`Created ${created} missing role menu(s)`);
                } else {
                    alert('All role menus already exist');
                }
            }
        });
    }

    // Placeholder methods for item CRUD (will open modals in Phase 8)
    createParentItem(): void {
        const menu = this.selectedMenu();
        if (!menu) return;

        const modalRef = this.modalService.open(MenuItemFormModalComponent, { size: 'lg' });
        modalRef.componentInstance.menuId = menu.menuId;

        modalRef.result.then((result) => {
            if (result?.type === 'create') {
                this.menuAdminService.createMenuItem(result.data).subscribe({
                    next: () => this.loadMenus()
                });
            }
        }).catch(() => { }); // Dismissed
    }

    createChildItem(parentItem: MenuItemAdminDto): void {
        const menu = this.selectedMenu();
        if (!menu) return;

        const modalRef = this.modalService.open(MenuItemFormModalComponent, { size: 'lg' });
        modalRef.componentInstance.menuId = menu.menuId;
        modalRef.componentInstance.parentMenuItemId = parentItem.menuItemId;

        modalRef.result.then((result) => {
            if (result?.type === 'create') {
                this.menuAdminService.createMenuItem(result.data).subscribe({
                    next: () => this.loadMenus()
                });
            }
        }).catch(() => { }); // Dismissed
    }

    editItem(item: MenuItemAdminDto): void {
        const modalRef = this.modalService.open(MenuItemFormModalComponent, { size: 'lg' });
        modalRef.componentInstance.existingItem = item;
        modalRef.componentInstance.menuId = item.menuId;

        modalRef.result.then((result) => {
            if (result?.type === 'update') {
                this.menuAdminService.updateMenuItem(result.menuItemId, result.data).subscribe({
                    next: () => this.loadMenus()
                });
            }
        }).catch(() => { }); // Dismissed
    }

    deleteItem(item: MenuItemAdminDto): void {
        const confirmDelete = confirm(`Delete "${item.text}"?`);
        if (!confirmDelete) return;

        this.menuAdminService.deleteMenuItem(item.menuItemId).subscribe({
            next: () => this.loadMenus()
        });
    }

    moveItemUp(item: MenuItemAdminDto, siblings: MenuItemAdminDto[]): void {
        const currentIndex = siblings.findIndex(s => s.menuItemId === item.menuItemId);
        if (currentIndex <= 0) return;

        // Swap with previous sibling
        const reordered = [...siblings];
        [reordered[currentIndex - 1], reordered[currentIndex]] = [reordered[currentIndex], reordered[currentIndex - 1]];

        this.reorderSiblings(reordered);
    }

    moveItemDown(item: MenuItemAdminDto, siblings: MenuItemAdminDto[]): void {
        const currentIndex = siblings.findIndex(s => s.menuItemId === item.menuItemId);
        if (currentIndex < 0 || currentIndex >= siblings.length - 1) return;

        // Swap with next sibling
        const reordered = [...siblings];
        [reordered[currentIndex], reordered[currentIndex + 1]] = [reordered[currentIndex + 1], reordered[currentIndex]];

        this.reorderSiblings(reordered);
    }

    private reorderSiblings(siblings: MenuItemAdminDto[]): void {
        const menu = this.selectedMenu();
        if (!menu) return;

        const firstItem = siblings[0];
        this.menuAdminService.reorderItems({
            menuId: menu.menuId,
            parentMenuItemId: firstItem.parentMenuItemId ?? undefined,
            orderedItemIds: siblings.map(s => s.menuItemId)
        }).subscribe({
            next: () => this.loadMenus()
        });
    }
}
