import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { filter, take } from 'rxjs/operators';
import { SelfRosterUpdateModalService } from './self-roster-update-modal.service';

/**
 * Route shim for the legacy `/{jobPath}/PlayerWaiverUpdate` URL.
 *
 * That URL is baked into `Jobs.PlayerReg_ConfirmationOnScreen` and
 * `Jobs.PlayerReg_ConfirmationEmail` on 120 jobs — the "to delete this registration or to
 * change your player's Team or Uniform# ... go to:" line — and JobCloneResetRules copies both
 * columns onto every clone, so the population grows on its own. The links are already on the
 * correct host (www is this app); only the route was missing, so they resolved to the 404 view.
 *
 * Despite the legacy name this was never a waiver page: the old
 * PlayerWaiverUpdateController.Login redirected to RegPlayerFixTeamAutorosterOrUniformNumber.
 * Its replacement here is the self-roster update MODAL, not a page — hence a shim rather than a
 * component of its own. The modal is self-contained for a cold arrival: it defaults to its
 * `login` phase and authenticates the family itself, which is exactly what the legacy
 * controller did (it signed the visitor out and presented a login form).
 *
 * Same treatment, and for the same stated reason, as the PlayerVIUpdate / ClubRepVIUpdate
 * routes: preserve the exact-case legacy path so DB-stored confirmation links resolve.
 */
@Component({
    selector: 'app-player-waiver-update-route',
    standalone: true,
    template: '',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlayerWaiverUpdateRouteComponent {
    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    private readonly modal = inject(SelfRosterUpdateModalService);
    private readonly destroyRef = inject(DestroyRef);

    constructor() {
        const jobPath = this.route.parent?.snapshot.paramMap.get('jobPath')
            ?? this.route.snapshot.paramMap.get('jobPath')
            ?? '';

        // Open FIRST, then watch. The signal write is synchronous, so the observable's initial
        // emission is already `true` and the filter drops it — no reliance on skip(1) counting,
        // whose correctness would depend on when the internal effect first runs.
        this.modal.open(jobPath);

        // The route has nothing to show once the modal is dismissed, so send the visitor to the
        // job landing. take(1) because this component's only job is that one hand-off.
        toObservable(this.modal.isOpen)
            .pipe(
                filter(isOpen => !isOpen),
                take(1),
                takeUntilDestroyed()
            )
            .subscribe(() => { void this.router.navigate(['/', jobPath]); });

        // Left by another route change (back button, a link inside the layout) while the modal
        // is still up: close it, or it would hang over whatever page comes next. On the normal
        // path the subscription above has already navigated and isOpen is false, so this is a
        // no-op.
        this.destroyRef.onDestroy(() => {
            if (this.modal.isOpen()) {
                this.modal.close();
            }
        });
    }
}
