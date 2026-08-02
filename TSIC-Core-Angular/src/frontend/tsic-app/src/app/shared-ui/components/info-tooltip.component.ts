import { ChangeDetectionStrategy, Component, ElementRef, HostListener, Injector, afterNextRender, inject, signal, input, viewChild } from '@angular/core';


/**
 * Info icon that reveals a panel. Supports two triggers:
 *   trigger="click"  (default) — toggle on click; closes on outside click or Escape.
 *   trigger="hover"  — opens on mouseenter, closes on mouseleave; also keyboard-accessible.
 *
 * Panel is position: fixed so it escapes overflow: hidden / scroll containers.
 * Usage: <app-info-tooltip message="Your help text here" />
 *        <app-info-tooltip trigger="hover" message="..." />
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
        class="btn btn-link p-0 ms-1 text-decoration-none info-tooltip-btn"
        tabindex="0"
        [attr.aria-label]="'Information: ' + message()"
        [attr.aria-expanded]="open()"
        (click)="onClick($event)"
        (mouseenter)="onMouseEnter()"
        (mouseleave)="onMouseLeave()"
        (focus)="onFocus()"
        (blur)="onBlur()">
        <i class="bi bi-info-circle text-info" aria-hidden="true"></i>
      </button>
      @if (open()) {
        <span #panel class="info-tooltip-panel" role="tooltip"
              [style.top.px]="top()"
              [style.left.px]="left()">
          {{ message() }}
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
      /* never wider than the viewport minus the 8px clamp margins */
      max-width: min(320px, calc(100vw - 16px));
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
  readonly message = input<string>('');
  readonly trigger = input<'click' | 'hover'>('click');

  readonly btnRef = viewChild<ElementRef<HTMLButtonElement>>('btn');
  readonly panelRef = viewChild<ElementRef<HTMLElement>>('panel');

  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly injector = inject(Injector);
  readonly open = signal(false);
  readonly top = signal(0);
  readonly left = signal(0);

  onClick(event: MouseEvent): void {
    if (this.trigger() !== 'click') return;
    event.stopPropagation();
    const willOpen = !this.open();
    this.open.set(willOpen);
    if (willOpen) this.updatePosition();
  }

  onMouseEnter(): void {
    if (this.trigger() !== 'hover') return;
    this.open.set(true);
    this.updatePosition();
  }

  onMouseLeave(): void {
    if (this.trigger() !== 'hover') return;
    this.open.set(false);
  }

  onFocus(): void {
    if (this.trigger() !== 'hover') return;
    this.open.set(true);
    this.updatePosition();
  }

  onBlur(): void {
    if (this.trigger() !== 'hover') return;
    this.open.set(false);
  }

  private updatePosition(): void {
    const el = this.btnRef()?.nativeElement ?? this.host.nativeElement.querySelector('button');
    if (!el) return;
    const rect = el.getBoundingClientRect();
    this.top.set(rect.bottom + 4);
    this.left.set(rect.left + rect.width / 2);
    // The panel is inside @if, so it only exists after the next render — clamp
    // then (AM-038: a header ⓘ near the viewport's right edge spilled off-page).
    afterNextRender(() => this.clampToViewport(), { injector: this.injector });
  }

  /** Shifts the centered panel back inside the viewport (8px margin) if it overflows either edge. */
  private clampToViewport(): void {
    const panel = this.panelRef()?.nativeElement;
    if (!panel) return;
    const margin = 8;
    const r = panel.getBoundingClientRect();
    if (r.right > window.innerWidth - margin) {
      this.left.set(this.left() - (r.right - (window.innerWidth - margin)));
    } else if (r.left < margin) {
      this.left.set(this.left() + (margin - r.left));
    }
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (this.trigger() !== 'click' || !this.open()) return;
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
