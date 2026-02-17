import {
    ChangeDetectionStrategy,
    Component,
    ElementRef,
    input,
    output,
    viewChild,
    effect,
    AfterViewInit,
    OnDestroy,
} from '@angular/core';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';

/**
 * Accessible wizard modal built on TsicDialogComponent (native <dialog>).
 *
 * Features inherited from TsicDialogComponent:
 *  - Native <dialog> element (top-layer, proper stacking)
 *  - Focus trap via FocusTrapDirective
 *  - ESC key handling
 *  - Backdrop click handling
 *  - Open/close animation
 *
 * Features added by WizardModalComponent:
 *  - Standard header with title, close button, aria-labelledby
 *  - Content projection: [modal-body] and [modal-footer]
 *  - Optional custom header class (e.g., bg-primary text-white)
 *  - Returns focus to previously-focused element on close
 *  - Responsive sizing (full-screen on mobile for lg/xl)
 *
 * Usage:
 *   @if (showModal()) {
 *     <app-wizard-modal [title]="'Register a Team'" size="lg" (closed)="onClose()">
 *       <div modal-body>...</div>
 *       <div modal-footer>...</div>
 *     </app-wizard-modal>
 *   }
 */
@Component({
    selector: 'app-wizard-modal',
    standalone: true,
    imports: [TsicDialogComponent],
    template: `
        <tsic-dialog
            [open]="true"
            [size]="size()"
            [closeOnEsc]="closeOnEsc()"
            [closeOnBackdrop]="closeOnBackdrop()"
            (requestClose)="onRequestClose()">
            <div class="modal-content">
                <div class="modal-header" [class]="headerClass()">
                    <h5 class="modal-title" [id]="titleId">
                        @if (titleIcon()) {
                            <i [class]="'bi me-2 ' + titleIcon()" aria-hidden="true"></i>
                        }
                        {{ title() }}
                    </h5>
                    @if (showCloseButton()) {
                        <button
                            type="button"
                            class="btn-close"
                            [class.btn-close-white]="headerClass().includes('text-white')"
                            (click)="onRequestClose()"
                            aria-label="Close">
                        </button>
                    }
                </div>
                <div class="modal-body">
                    <ng-content select="[modal-body]"></ng-content>
                </div>
                <ng-content select="[modal-footer]"></ng-content>
            </div>
        </tsic-dialog>
    `,
    styles: [`
        :host ::ng-deep .modal-footer {
            display: flex;
            justify-content: flex-end;
            gap: 0.5rem;
            padding: 1rem;
            border-top: 1px solid var(--bs-border-color-translucent);
            background: var(--bs-body-bg);
        }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WizardModalComponent implements AfterViewInit, OnDestroy {
    /** Modal title displayed in the header. Also drives aria-labelledby. */
    readonly title = input.required<string>();

    /** Optional Bootstrap icon class(es) for the title (e.g., 'bi-exclamation-octagon text-danger'). */
    readonly titleIcon = input('');

    /** Size variant: sm (480px), md (720px default), lg (960px). */
    readonly size = input<'sm' | 'md' | 'lg' | ''>('');

    /** Whether ESC key closes the modal. Default true. */
    readonly closeOnEsc = input(true);

    /** Whether clicking the backdrop closes the modal. Default true. */
    readonly closeOnBackdrop = input(true);

    /** Whether to show the X close button in the header. Default true. */
    readonly showCloseButton = input(true);

    /** Optional CSS class(es) for the modal header (e.g., 'bg-primary text-white'). */
    readonly headerClass = input('');

    /** Emitted when the modal wants to close (ESC, backdrop click, or close button). */
    readonly closed = output<void>();

    /** Unique ID for aria-labelledby. */
    readonly titleId = `wizard-modal-title-${nextId++}`;

    /** Element that had focus before the modal opened — focus returns here on close. */
    private triggerElement: HTMLElement | null = null;

    ngAfterViewInit(): void {
        this.triggerElement = document.activeElement as HTMLElement | null;
    }

    ngOnDestroy(): void {
        this.returnFocus();
    }

    onRequestClose(): void {
        this.closed.emit();
    }

    private returnFocus(): void {
        if (this.triggerElement && typeof this.triggerElement.focus === 'function') {
            // Delay to let the dialog close animation finish
            setTimeout(() => this.triggerElement?.focus(), 0);
        }
    }
}

let nextId = 0;
