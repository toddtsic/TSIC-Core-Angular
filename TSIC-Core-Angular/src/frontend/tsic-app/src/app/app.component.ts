import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ToastsComponent } from '@shared-ui/toasts.component';

@Component({
  selector: 'tsic-root',
  standalone: true,
  imports: [RouterOutlet, ToastsComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AppComponent {
  title = 'tsic-app';
}
