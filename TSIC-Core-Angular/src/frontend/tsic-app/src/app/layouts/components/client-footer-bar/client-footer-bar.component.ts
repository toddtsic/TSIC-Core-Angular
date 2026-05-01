import { ChangeDetectionStrategy, Component } from '@angular/core';
import { environment } from '../../../../environments/environment';

@Component({
    selector: 'app-client-footer-bar',
    standalone: true,
    templateUrl: './client-footer-bar.component.html',
    styleUrl: './client-footer-bar.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ClientFooterBarComponent {
    readonly currentYear = new Date().getFullYear();
    readonly buildVersion = environment.buildVersion;
    readonly envName = environment.envName;
    readonly envBadge = this.envName === 'production' ? null
        : this.envName === 'staging' ? 'STAGING'
        : 'DEV';
}
