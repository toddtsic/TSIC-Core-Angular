import { Component, inject, signal, computed, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ToastService } from '@shared-ui/toast.service';
import { NavEditorService } from './services/nav-editor.service';
import type {
	NavEditorNavDto,
	NavEditorNavItemDto,
	NavEditorLegacyMenuDto,
	NavEditorLegacyItemDto,
} from '@core/api';

@Component({
	selector: 'app-nav-editor',
	standalone: true,
	imports: [CommonModule, FormsModule],
	templateUrl: './nav-editor.component.html',
	styleUrl: './nav-editor.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NavEditorComponent {
	private readonly navEditorService = inject(NavEditorService);
	private readonly toast = inject(ToastService);

	// ── State ──
	isLoading = signal(false);
	error = signal<string | null>(null);

	// Left panel: legacy menus
	legacyMenus = signal<NavEditorLegacyMenuDto[]>([]);
	selectedLegacyRoleId = signal<string | null>(null);

	// Right panel: new nav defaults
	navDefaults = signal<NavEditorNavDto[]>([]);
	selectedNavRoleId = signal<string | null>(null);

	// Editing state
	editingItemId = signal<number | null>(null);
	editText = signal('');
	editIconName = signal('');
	editRouterLink = signal('');
	editNavigateUrl = signal('');
	editActive = signal(true);

	// ── Computed ──

	selectedLegacyMenu = computed(() => {
		const roleId = this.selectedLegacyRoleId();
		if (!roleId) return null;
		return this.legacyMenus().find(m => m.roleId === roleId) ?? null;
	});

	selectedNav = computed(() => {
		const roleId = this.selectedNavRoleId();
		if (!roleId) return null;
		return this.navDefaults().find(n => n.roleId === roleId) ?? null;
	});

	legacyRoles = computed(() =>
		this.legacyMenus()
			.filter(m => m.roleName)
			.map(m => ({ roleId: m.roleId!, roleName: m.roleName! }))
	);

	navRoles = computed(() =>
		this.navDefaults()
			.map(n => ({ roleId: n.roleId, roleName: n.roleName ?? n.roleId }))
	);

	allRoleIds = computed(() => {
		const legacyIds = this.legacyMenus().map(m => m.roleId).filter(Boolean) as string[];
		const navIds = this.navDefaults().map(n => n.roleId);
		return [...new Set([...legacyIds, ...navIds])].sort();
	});

	constructor() {
		this.loadData();
	}

	// ── Data loading ──

	loadData(): void {
		this.isLoading.set(true);
		this.error.set(null);

		// Load legacy menus
		this.navEditorService.getLegacyMenus().subscribe({
			next: menus => {
				this.legacyMenus.set(menus);
				if (menus.length > 0 && !this.selectedLegacyRoleId()) {
					this.selectedLegacyRoleId.set(menus[0].roleId ?? null);
				}
			},
			error: err => {
				this.error.set('Failed to load legacy menus');
				this.isLoading.set(false);
			},
		});

		// Load nav defaults
		this.navEditorService.getDefaults().subscribe({
			next: defaults => {
				this.navDefaults.set(defaults);
				if (defaults.length > 0 && !this.selectedNavRoleId()) {
					this.selectedNavRoleId.set(defaults[0].roleId);
				}
				this.isLoading.set(false);
			},
			error: err => {
				this.error.set('Failed to load nav defaults');
				this.isLoading.set(false);
			},
		});
	}

	// ── Legacy panel actions ──

	selectLegacyRole(roleId: string): void {
		this.selectedLegacyRoleId.set(roleId);
	}

	importLegacyMenu(menu: NavEditorLegacyMenuDto): void {
		if (!menu.roleId) return;
		this.isLoading.set(true);

		this.navEditorService.importLegacy({
			sourceMenuId: menu.menuId,
			targetRoleId: menu.roleId,
		}).subscribe({
			next: nav => {
				this.toast.show('Menu imported successfully', 'success');
				this.loadData();
			},
			error: err => {
				this.toast.show('Failed to import menu', 'danger');
				this.isLoading.set(false);
			},
		});
	}

	// ── Nav panel actions ──

	selectNavRole(roleId: string): void {
		this.selectedNavRoleId.set(roleId);
		this.cancelEdit();
	}

	createNavForRole(roleId: string): void {
		this.isLoading.set(true);
		this.navEditorService.createDefault({ roleId }).subscribe({
			next: nav => {
				this.toast.show(`Nav created for ${nav.roleName ?? roleId}`, 'success');
				this.loadData();
			},
			error: err => {
				this.toast.show('Failed to create nav', 'danger');
				this.isLoading.set(false);
			},
		});
	}

	addRootItem(navId: number): void {
		this.navEditorService.createItem({
			navId,
			text: 'New Section',
			iconName: 'folder',
		}).subscribe({
			next: () => this.loadData(),
			error: err => this.toast.show('Failed to add item', 'danger'),
		});
	}

	addChildItem(navId: number, parentNavItemId: number): void {
		this.navEditorService.createItem({
			navId,
			parentNavItemId,
			text: 'New Item',
		}).subscribe({
			next: () => this.loadData(),
			error: err => this.toast.show('Failed to add item', 'danger'),
		});
	}

	// ── Inline editing ──

	startEdit(item: NavEditorNavItemDto): void {
		this.editingItemId.set(item.navItemId);
		this.editText.set(item.text);
		this.editIconName.set(item.iconName ?? '');
		this.editRouterLink.set(item.routerLink ?? '');
		this.editNavigateUrl.set(item.navigateUrl ?? '');
		this.editActive.set(item.active);
	}

	cancelEdit(): void {
		this.editingItemId.set(null);
	}

	saveEdit(navItemId: number): void {
		this.navEditorService.updateItem(navItemId, {
			text: this.editText(),
			active: this.editActive(),
			iconName: this.editIconName() || null,
			routerLink: this.editRouterLink() || null,
			navigateUrl: this.editNavigateUrl() || null,
		}).subscribe({
			next: () => {
				this.cancelEdit();
				this.loadData();
			},
			error: err => this.toast.show('Failed to save item', 'danger'),
		});
	}

	deleteItem(navItemId: number): void {
		this.navEditorService.deleteItem(navItemId).subscribe({
			next: () => this.loadData(),
			error: err => this.toast.show('Failed to delete item', 'danger'),
		});
	}

	toggleActive(item: NavEditorNavItemDto): void {
		this.navEditorService.updateItem(item.navItemId, {
			text: item.text,
			active: !item.active,
			iconName: item.iconName,
			routerLink: item.routerLink,
			navigateUrl: item.navigateUrl,
		}).subscribe({
			next: () => this.loadData(),
			error: err => this.toast.show('Failed to toggle active', 'danger'),
		});
	}

	// ── Helpers ──

	getLegacyRoute(item: NavEditorLegacyItemDto): string {
		if (item.routerLink) return item.routerLink;
		if (item.navigateUrl) return item.navigateUrl;
		if (item.controller && item.action) return `${item.controller}/${item.action}`;
		return '(none)';
	}
}
