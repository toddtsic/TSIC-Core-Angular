import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ToastsComponent } from '@shared-ui/toasts.component';
import { SelfRosterUpdateModalService } from '@views/registration/self-roster-update/self-roster-update-modal.service';
import { SelfRosterUpdateModalComponent } from '@views/registration/self-roster-update/self-roster-update-modal.component';

@Component({
  selector: 'tsic-root',
  standalone: true,
  imports: [RouterOutlet, ToastsComponent, SelfRosterUpdateModalComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AppComponent {
  readonly sruModal = inject(SelfRosterUpdateModalService);
  title = 'tsic-app';
}
