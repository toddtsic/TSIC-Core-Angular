import { Component, Input } from '@angular/core';


/**
 * Info tooltip component for displaying contextual help messages
 * Usage: <app-info-tooltip message="Your help text here" />
 */
@Component({
    selector: 'app-info-tooltip',
    standalone: true,
    imports: [],
    template: `
    <button
      type="button"
      class="btn btn-link p-0 ms-2 text-decoration-none info-tooltip-btn"
      [attr.aria-label]="'Information: ' + message"
      [title]="message"
      data-bs-toggle="tooltip"
      data-bs-placement="top"
      data-bs-custom-class="info-tooltip">
      <i class="bi bi-info-circle text-info" aria-hidden="true"></i>
    </button>
  `,
    styles: [`
    .info-tooltip-btn {
      font-size: 1rem;
      line-height: 1;
      vertical-align: middle;
      opacity: 0.7;
      transition: opacity 0.2s ease;
    }

    .info-tooltip-btn:hover,
    .info-tooltip-btn:focus {
      opacity: 1;
    }

    .info-tooltip-btn i {
      font-size: 1.1em;
    }
  `]
})
export class InfoTooltipComponent {
    @Input() message: string = '';
}
