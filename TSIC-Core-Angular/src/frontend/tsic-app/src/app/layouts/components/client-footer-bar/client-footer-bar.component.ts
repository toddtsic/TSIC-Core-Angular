import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
    selector: 'app-client-footer-bar',
    standalone: true,
    templateUrl: './client-footer-bar.component.html',
    styleUrl: './client-footer-bar.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ClientFooterBarComponent {
    readonly currentYear = new Date().getFullYear();
}
