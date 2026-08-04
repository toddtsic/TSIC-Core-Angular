import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { NavigationEnd, Router, RouterModule } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';

@Component({
    selector: 'app-scheduling-shell',
    standalone: true,
    imports: [RouterModule],
    templateUrl: './scheduling-shell.component.html',
    styleUrl: './scheduling-shell.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class SchedulingShellComponent {
    private readonly router = inject(Router);

    /**
     * True on any child route below the checklist index. With the Scheduling menu
     * collapsed to the single checklist entry, this bar is the contextual way back.
     */
    readonly showBackBar = toSignal(
        this.router.events.pipe(
            filter((e): e is NavigationEnd => e instanceof NavigationEnd),
            map(e => SchedulingShellComponent.isChildRoute(e.urlAfterRedirects))
        ),
        { initialValue: SchedulingShellComponent.isChildRoute(this.router.url) }
    );

    private static isChildRoute(url: string): boolean {
        return /\/scheduling\/[^/?#]+/.test(url);
    }
}
