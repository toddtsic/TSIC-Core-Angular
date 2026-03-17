import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import type { NavItemDto } from '@core/api';
import { JobService } from '@infrastructure/services/job.service';
import { MenuStateService } from '../../services/menu-state.service';

const MAX_TABS = 4;

@Component({
    selector: 'app-bottom-nav',
    standalone: true,
    imports: [RouterLink, RouterLinkActive],
    templateUrl: './bottom-nav.component.html',
    styleUrl: './bottom-nav.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class BottomNavComponent {
    private readonly jobService = inject(JobService);
    readonly menuState = inject(MenuStateService);

    /** First N top-level items become bottom tabs */
    readonly tabItems = computed<NavItemDto[]>(() =>
        this.jobService.navItems().slice(0, MAX_TABS)
    );

    /** True when there are more items than fit in the tab bar */
    readonly hasMoreItems = computed(() =>
        this.jobService.navItems().length > MAX_TABS
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

    openMore(): void {
        this.menuState.toggleOffcanvas();
    }
}
