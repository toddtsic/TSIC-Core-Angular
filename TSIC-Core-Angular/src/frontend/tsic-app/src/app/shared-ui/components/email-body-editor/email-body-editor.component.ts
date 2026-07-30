import { ChangeDetectionStrategy, Component, input, model, viewChild } from '@angular/core';
import { RichTextEditorAllModule, RichTextEditorComponent } from '@syncfusion/ej2-angular-richtexteditor';

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

    // Deliberately no font-name/size or image tools: bodies travel through token
    // substitution and land in arbitrary mail clients — keep the markup simple.
    readonly rteTools = {
        items: ['Bold', 'Italic', 'Underline', '|',
            'FontColor', 'BackgroundColor', '|',
            'OrderedList', 'UnorderedList', '|',
            'CreateLink', '|', 'Undo', 'Redo']
    };

    private readonly rte = viewChild.required<RichTextEditorComponent>('rte');

    onRteChange(event: { value?: string | null }): void {
        this.body.set(event?.value ?? '');
    }

    /** Insert a substitution token (e.g. "!PERSON") at the caret as plain text.
     *  Trailing space matches the bulletin editor (punchlist): the substitution engine
     *  matches tokens greedily, so "!PERSONx" typed flush against the token would break it. */
    insertToken(token: string): void {
        const editor = this.rte();
        editor.focusIn();
        editor.executeCommand('insertText', token + ' ');
        // executeCommand bypasses the saveInterval cycle — pull the fresh HTML into the model now
        // so send guards and previews see the token immediately.
        editor.updateValue();
        this.body.set(editor.value ?? '');
    }
}
