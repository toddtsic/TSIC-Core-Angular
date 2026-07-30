import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

/** What the tester chose in the popover. */
export interface TestSendOptions {
    recipient: string;
}

/**
 * Non-prod "Send Test" button + popover, shared by every email compose surface.
 * The surface renders tokens against the FIRST registration of its current audience and
 * delivers the result to the single address entered here (default: Ann's test inbox).
 * Parent owns the HTTP call: listen to (send), pass [busy] while in flight, and gate
 * visibility with its own non-prod check.
 */
@Component({
    selector: 'app-test-send-button',
    standalone: true,
    imports: [FormsModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <div class="test-send-wrap">
            <button type="button" class="btn btn-outline-info" (click)="toggle()"
                [disabled]="disabled() || busy()"
                title="Renders for the first recipient and sends a real test email">
                @if (busy()) { <span class="spinner-border spinner-border-sm me-1"></span> Sending Test... }
                @else { Send Test Email }
            </button>

            @if (open()) {
                <div class="test-send-panel" [class.align-left]="align() === 'left'">
                    <div class="mb-2">
                        <label for="test-send-to" class="form-label mb-1">Send test to</label>
                        <input id="test-send-to" type="email" class="form-control form-control-sm"
                            [ngModel]="extra()" (ngModelChange)="extra.set($event)"
                            placeholder="you@example.com">
                        <small class="text-body-secondary">
                            Tokens render with the <strong>first recipient's</strong> real data;
                            only the delivery address is swapped.
                        </small>
                    </div>
                    <div class="d-flex gap-2 justify-content-end">
                        <button type="button" class="btn btn-sm btn-outline-secondary" (click)="open.set(false)">Cancel</button>
                        <button type="button" class="btn btn-sm btn-info" [disabled]="!canFire()" (click)="fire()">Send Test</button>
                    </div>
                </div>
            }
        </div>
    `,
    styles: [`
        .test-send-wrap { position: relative; display: inline-block; }
        .test-send-panel {
            position: absolute;
            bottom: calc(100% + var(--space-2, 8px));
            right: 0;
            z-index: 20;
            min-width: 300px;
            padding: var(--space-3, 12px);
            text-align: left;
            background: var(--bs-body-bg);
            border: 1px solid var(--brand-border, var(--bs-border-color));
            border-radius: var(--radius-md, 8px);
            box-shadow: var(--shadow-lg, 0 8px 24px rgba(0, 0, 0, 0.18));
        }
        .test-send-panel.align-left {
            right: auto;
            left: 0;
        }
        .test-send-panel :focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
        }
    `]
})
export class TestSendButtonComponent {
    readonly disabled = input(false);
    readonly busy = input(false);
    /** Which button edge the popover hugs. Default 'right' suits modal footers (button at the
     *  right edge); use 'left' when the button sits at the left of its container so the panel
     *  opens inward instead of overflowing (e.g. the reg-detail fly-in). */
    readonly align = input<'left' | 'right'>('right');
    readonly send = output<TestSendOptions>();

    readonly open = signal(false);
    /** Default per Todd: Ann's test inbox — same precedent as the invite sandbox test recipient. */
    readonly extra = signal('anntsic@gmail.com');

    readonly canFire = computed(() => this.extra().trim().includes('@'));

    toggle(): void { this.open.update(v => !v); }

    fire(): void {
        const recipient = this.extra().trim();
        if (!recipient.includes('@')) return;
        this.open.set(false);
        this.send.emit({ recipient });
    }
}
