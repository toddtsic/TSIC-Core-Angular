import { AfterViewInit, ChangeDetectionStrategy, Component, OnChanges, SimpleChanges, input, model, viewChild } from '@angular/core';
import { RichTextEditorAllModule, RichTextEditorComponent } from '@syncfusion/ej2-angular-richtexteditor';
import { TsicRteDirective } from '@shared-ui/rte.directive';

/**
 * The one email-body compose widget. Every surface that lets a user author an email body
 * MUST use this instead of a raw <textarea>: the send pipeline is HTML end-to-end
 * (TextSubstitutionService injects tags, EmailService sends text/html), so plain-text
 * composition silently ships run-on emails and raw-HTML templates read as tag soup.
 * This editor makes "body is HTML" true at the point of authoring.
 *
 * Two-way bind the body: <app-email-body-editor [(body)]="emailBody" />
 * Token chips stay in the parent; call insertToken() via a viewChild ref to insert at the caret.
 */
@Component({
    selector: 'app-email-body-editor',
    standalone: true,
    imports: [RichTextEditorAllModule, TsicRteDirective],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <ejs-richtexteditor #rte
            tsicRte
            [height]="height()"
            [saveInterval]="200"
            [enabled]="!disabled()"
            [placeholder]="placeholder()"
            (change)="onRteChange($event)">
        </ejs-richtexteditor>
    `
})
export class EmailBodyEditorComponent implements AfterViewInit, OnChanges {
    /** The body as HTML. Writing a new string from OUTSIDE re-seeds the editor; the editor's
     *  own emissions are never written back (see seed()). */
    readonly body = model<string>('');
    readonly height = input(250);
    readonly placeholder = input('Compose your email…');
    readonly disabled = input(false);

    // Was a byte-identical copy of the app-wide toolbar; now the shared one. The old
    // note still holds and is why that config omits font-name/size: bodies travel
    // through token substitution and land in arbitrary mail clients, so keep the
    // markup simple. Tables now ride along — mail clients render <table> unevenly,
    // so review a real send before leaning on one in an email body.

    private readonly rte = viewChild.required<RichTextEditorComponent>('rte');

    private viewReady = false;

    /**
     * The last HTML this component pushed into `body`.
     *
     * This is the whole fix, so it is worth stating why. `value` used to be BOUND
     * (`[value]="body()"`), which made a Syncfusion editor — a widget that owns its own
     * contenteditable DOM — into a controlled input. Every emission travelled model → binding →
     * editor, and Syncfusion answers a `value` write by re-rendering `inputElement.innerHTML`
     * from the string (it normalizes the HTML on the way in, so the string virtually never
     * matches the live DOM and the re-render virtually always happens).
     *
     * Harmless while typing. Fatal across a dialog: the Insert Link / Insert Image dialogs save
     * the caret when they open and restore it when you press Insert. A re-render in between
     * detaches the nodes that saved caret points at, the restore falls back to the root element,
     * and the insert replaces the whole body — the reported "my text vanished, only the link is
     * left".
     *
     * So the editor is now authoritative for its own content and we only write to it when the
     * value genuinely came from somewhere else (a template picked, a modal reset, a loaded draft).
     * Comparing against the last emission is what tells the two apart.
     */
    private lastEmitted: string | null = null;

    ngAfterViewInit(): void {
        this.viewReady = true;
        this.seed(this.body());
    }

    ngOnChanges(changes: SimpleChanges): void {
        // Before ngAfterViewInit there is no editor yet, and the initial value is seeded there.
        if (!this.viewReady || !changes['body']) { return; }
        const next = this.body();
        if (next === this.lastEmitted) { return; } // our own emission arriving back — never re-render on it
        this.seed(next);
    }

    /** Push HTML INTO the editor. Only for values that did not come from the editor. */
    private seed(html: string): void {
        this.lastEmitted = html;
        this.rte().value = html;
    }

    onRteChange(event: { value?: string | null }): void {
        // Syncfusion emits null for an empty editor; the send guards and previews want ''.
        const html = event?.value ?? '';
        this.lastEmitted = html;
        this.body.set(html);
    }

    /** Insert a substitution token (e.g. "!PERSON") at the caret, followed by a separator
     *  space so the next token/typed word can't glue onto it. Must be a NON-BREAKING space:
     *  a plain " " at the end of a contenteditable line is collapsed by HTML normalization
     *  before the next insert, so consecutive chip clicks produced "!EMAIL!JOBNAME". */
    insertToken(token: string): void {
        const editor = this.rte();
        editor.focusIn();
        editor.executeCommand('insertText', token + '\u00A0');
        // executeCommand bypasses the saveInterval cycle — pull the fresh HTML into the model now
        // so send guards and previews see the token immediately.
        editor.updateValue();
        const html = editor.value ?? '';
        this.lastEmitted = html;
        this.body.set(html);
    }
}
