import { ChangeDetectionStrategy, Component, input, model, viewChild } from '@angular/core';
import { RichTextEditorAllModule, RichTextEditorComponent } from '@syncfusion/ej2-angular-richtexteditor';
import { TSIC_RTE_TOOLS } from '@shared-ui/rte-config';

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
    imports: [RichTextEditorAllModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <ejs-richtexteditor #rte
            [value]="body()"
            [toolbarSettings]="rteTools"
            [height]="height()"
            [saveInterval]="200"
            [enabled]="!disabled()"
            [placeholder]="placeholder()"
            (change)="onRteChange($event)">
        </ejs-richtexteditor>
    `
})
export class EmailBodyEditorComponent {
    /** The body as HTML. Writing a new string re-seeds the editor (writing back the
     *  editor's own emission is a no-op — the wrapper only propagates changed strings —
     *  so the [(body)] loop cannot fight the caret). */
    readonly body = model<string>('');
    readonly height = input(250);
    readonly placeholder = input('Compose your email…');
    readonly disabled = input(false);

    // Was a byte-identical copy of the app-wide toolbar; now the shared one. The old
    // note still holds and is why that config omits font-name/size: bodies travel
    // through token substitution and land in arbitrary mail clients, so keep the
    // markup simple. Tables now ride along — mail clients render <table> unevenly,
    // so review a real send before leaning on one in an email body.
    readonly rteTools = TSIC_RTE_TOOLS;

    private readonly rte = viewChild.required<RichTextEditorComponent>('rte');

    onRteChange(event: { value?: string | null }): void {
        this.body.set(event?.value ?? '');
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
        this.body.set(editor.value ?? '');
    }
}
