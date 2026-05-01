import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';

@Component({
    selector: 'app-accounting-coming-soon',
    standalone: true,
    imports: [CommonModule, RouterLink],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './coming-soon.component.html',
    styleUrls: ['./coming-soon.component.scss'],
})
export class AccountingComingSoonComponent {
    private readonly route = inject(ActivatedRoute);

    readonly title = toSignal(
        this.route.data.pipe(map(d => (d['title'] as string) ?? 'This screen')),
        { initialValue: 'This screen' }
    );

    readonly legacyController = toSignal(
        this.route.data.pipe(map(d => (d['legacyController'] as string | undefined))),
        { initialValue: undefined }
    );
}
