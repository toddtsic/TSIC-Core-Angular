import { ChangeDetectionStrategy, Component, ElementRef, HostListener, Input, ViewChild, inject, signal } from '@angular/core';


/**
 * Click-triggered info popover. Self-contained (no Bootstrap JS dependency).
 * Panel is position: fixed so it escapes overflow: hidden / scroll containers.
 * Usage: <app-info-tooltip message="Your help text here" />
 */
@Component({
  selector: 'app-info-tooltip',
  standalone: true,
  imports: [],
  template: `
    <span class="info-tooltip-wrapper">
      <button
        #btn
        type="button"
        class="btn btn-link p-0 ms-2 text-decoration-none info-tooltip-btn"
        tabindex="0"
        [attr.aria-label]="'Information: ' + message"
        [attr.aria-expanded]="open()"
        (click)="toggle($event)">
        <i class="bi bi-info-circle text-info" aria-hidden="true"></i>
      </button>
      @if (open()) {
        <span class="info-tooltip-panel" role="tooltip"
              [style.top.px]="top()"
              [style.left.px]="left()">
          {{ message }}
        </span>
      }
    </span>
  `,
  styles: [`
    .info-tooltip-wrapper {
      position: relative;
      display: inline-block;
      line-height: 1;
    }

    .info-tooltip-btn {
      font-size: inherit;
      line-height: 1;
      vertical-align: middle;
      opacity: 0.7;
      transition: opacity 0.2s ease;
      cursor: pointer;
    }

    .info-tooltip-btn:hover,
    .info-tooltip-btn:focus {
      opacity: 1;
    }

    .info-tooltip-btn i {
      font-size: 1.1em;
    }

    .info-tooltip-panel {
      position: fixed;
      transform: translateX(-50%);
      z-index: 2000;
      min-width: 220px;
      max-width: 320px;
      padding: var(--space-2) var(--space-3);
      border-radius: var(--radius-md);
      background: var(--brand-surface);
      color: var(--brand-text);
      border: 1px solid var(--border-color);
      box-shadow: var(--shadow-md);
      font-size: var(--font-size-sm);
      font-weight: var(--font-weight-normal);
      line-height: 1.4;
      text-align: left;
      letter-spacing: normal;
      text-transform: none;
      white-space: normal;
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class InfoTooltipComponent {
  @Input() message: string = '';

  @ViewChild('btn', { static: false }) btnRef?: ElementRef<HTMLButtonElement>;

  private readonly host = inject(ElementRef<HTMLElement>);
  readonly open = signal(false);
  readonly top = signal(0);
  readonly left = signal(0);

  toggle(event: MouseEvent): void {
    event.stopPropagation();
    const willOpen = !this.open();
    this.open.set(willOpen);
    if (willOpen) this.updatePosition();
  }

  private updatePosition(): void {
    const el = this.btnRef?.nativeElement ?? this.host.nativeElement.querySelector('button');
    if (!el) return;
    const rect = el.getBoundingClientRect();
    this.top.set(rect.bottom + 4);
    this.left.set(rect.left + rect.width / 2);
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.open()) return;
    if (!this.host.nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.open()) this.open.set(false);
  }

  @HostListener('window:scroll')
  @HostListener('window:resize')
  onReflow(): void {
    if (this.open()) this.updatePosition();
  }
}
