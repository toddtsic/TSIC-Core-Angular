import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import type { NavItemDto } from '@core/api';
import { JobService } from '@infrastructure/services/job.service';
import { ScrollFadeDirective } from '@shared-ui/directives/scroll-fade.directive';
import { MenuStateService } from '../../services/menu-state.service';

@Component({
    selector: 'app-bottom-nav',
    standalone: true,
    imports: [RouterLink, RouterLinkActive, ScrollFadeDirective],
    templateUrl: './bottom-nav.component.html',
    styleUrl: './bottom-nav.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class BottomNavComponent {
    private readonly jobService = inject(JobService);
    readonly menuState = inject(MenuStateService);

    /** All top-level items become tabs — the bar scrolls horizontally when they overflow. */
    readonly tabItems = computed<NavItemDto[]>(() =>
        this.jobService.navItems()
    );

    /** Only render the bar when nav is loaded */
    readonly navVisible = computed(() =>
        this.jobService.navItems().length > 0
    );

    getIcon(item: NavItemDto): string {
        return item.iconName || 'circle';
    }

    /** Extracts the path portion from a routerLink (strips query string) */
    getLink(item: NavItemDto): string | null {
        if (item.navigateUrl) return null;
        if (item.routerLink) return item.routerLink.split('?')[0];
        return null;
    }

    isExternalLink(item: NavItemDto): boolean {
        return !!item.navigateUrl;
    }

    /**
     * A top-level item with no direct route is a category header — tapping it
     * opens a focused sheet showing only that category's children (re-tapping
     * closes it), instead of being a dead tab.
     */
    openCategory(item: NavItemDto): void {
        this.menuState.toggleMobileSheet(item.navItemId);
    }

    /** True when this category's focused sheet is currently open (highlights the tab). */
    isCategoryOpen(item: NavItemDto): boolean {
        return this.menuState.mobileSheetCategoryId() === String(item.navItemId);
    }
}
