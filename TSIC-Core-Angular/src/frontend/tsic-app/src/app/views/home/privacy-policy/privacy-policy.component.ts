import { Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-privacy-policy',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './privacy-policy.component.html',
  styleUrl: './privacy-policy.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PrivacyPolicyComponent {
  private readonly location = inject(Location);

  goBack(): void {
    this.location.back();
  }
}
