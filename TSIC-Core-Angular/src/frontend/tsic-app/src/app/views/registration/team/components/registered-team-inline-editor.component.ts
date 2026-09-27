import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, OnInit, afterNextRender, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { RegisteredTeamDto } from '@core/api';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { extractHttpErrorMessage } from '@infrastructure/interceptors/http-error-utils';
import { LOP_CHOICES, normalizeLop } from '@shared/teams/lop-choices';
import { renameSuccessMessage, type TeamRenameConfirmation } from '@shared/teams/team-rename-confirm.component';

/**
 * Edit a REGISTERED team in its row on the teams board — this event's name and level of play
 * (Todd 2026-09-27, inline, no modal). The Registered Teams pencil's dialog (team-rename-confirm,
 * rep / event origin / LOP on), in a row:
 *
 *   • Save only when something moves: the name, a real level pick, or the library tick.
 *   • A level stored off the 1–5 scale starts BLANK and blank is never a change — nothing gets
 *     erased that nobody asked to erase.
 *   • "Rename it in my Club Team Library too" only when there is a library entry whose name
 *     differs from the new one; ALWAYS unticked to start (writing the library is the weightier act).
 *   • Teams.TeamName 100 chars; 80 when the tick also writes ClubTeams.ClubTeamName.
 *   • A server refusal stays in the row, the row stays open.
 *
 * Owns its save; `saved` carries the toast text so the board's host can reload and then say it.
 */
@Component({
    selector: 'app-registered-team-inline-editor',
    standalone: true,
    template: `
    <div class="ie" role="group" [attr.aria-label]="'Edit ' + team().teamName + ' for ' + eventName()"
         (keydown.escape)="cancel()">
      <label class="ie-field">
        <span class="ie-label">Name at {{ eventName() }}</span>
        <input class="ie-input ie-name" type="text" autocomplete="off"
               [attr.maxlength]="maxLength()"
               [value]="name()" (input)="name.set($any($event.target).value)"
               (keydown.enter)="save()"
               [class.is-invalid]="!name().trim()" />
      </label>

      <div class="ie-line">
        <label class="ie-field ie-field--lop">
          <span class="ie-label">Level</span>
          <select class="ie-input" (change)="lop.set($any($event.target).value)">
            <option value="" [selected]="!lop()">Pick…</option>
            @for (c of lopChoices; track c.value) {
              <option [value]="c.value" [selected]="c.value === lop()">{{ c.label }}</option>
            }
          </select>
        </label>
        <div class="ie-actions">
          <button type="button" class="btn-cancel" [disabled]="saving()" (click)="cancel()">Cancel</button>
          <button type="button" class="btn-save" [disabled]="saving() || !canSave()" (click)="save()">
            {{ saving() ? 'Saving…' : 'Save' }}
          </button>
        </div>
      </div>

      @if (showPropagate()) {
        <label class="ie-check">
          <input type="checkbox" [checked]="propagate()" (change)="propagate.set($any($event.target).checked)" />
          <span>Rename it in my Club Team Library too
            @if (propagate()) {
              <span class="ie-was">({{ libraryName() }} &rarr; {{ name().trim() }})</span>
            }
          </span>
        </label>
      }

      @if (error()) { <p class="ie-msg ie-msg--err" role="alert"><i class="bi bi-exclamation-triangle" aria-hidden="true"></i>{{ error() }}</p> }
      <p class="ie-scope">
        {{ eventName() }} only{{ propagateEffective() ? ', plus the name in your library' : ' — your Club Team Library stays as it is' }}.
      </p>
    </div>
    `,
    styles: [`
      .ie { display: flex; flex-direction: column; gap: var(--space-2); }
      .ie-line { display: flex; flex-wrap: wrap; align-items: flex-end; gap: var(--space-2); }

      .ie-field { display: flex; flex-direction: column; gap: 1px; min-width: 0; margin: 0; }
      .ie-field--lop { width: 84px; flex-shrink: 0; }

      .ie-label {
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-semibold);
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--brand-text-muted);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
      }

      .ie-input {
        width: 100%;
        min-width: 0;
        padding: 3px var(--space-2);
        border: 1px solid var(--bs-border-color);
        border-radius: var(--radius-sm);
        background: var(--brand-surface);
        color: var(--brand-text);
        font-size: var(--font-size-sm);

        &:focus-visible { outline: none; border-color: var(--bs-success); box-shadow: var(--shadow-focus); }
        &.is-invalid { border-color: var(--bs-danger); }
      }
      select.ie-input { padding: 3px var(--space-1); font-size: var(--font-size-xs); cursor: pointer; }
      .ie-name { font-weight: var(--font-weight-semibold); }

      .ie-actions { display: flex; gap: var(--space-2); margin-left: auto; }

      .btn-cancel, .btn-save {
        padding: 3px var(--space-2);
        border-radius: var(--radius-sm);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        white-space: nowrap;
        cursor: pointer;

        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        &:disabled { opacity: 0.45; cursor: default; }
      }
      .btn-cancel { border: 1px solid var(--bs-border-color); background: var(--brand-surface); color: var(--brand-text); }
      /* This side's color: success = this event. */
      .btn-save { border: 1px solid var(--bs-success); background: var(--bs-success); color: var(--neutral-0); }

      .ie-check {
        display: flex;
        align-items: baseline;
        gap: var(--space-2);
        margin: 0;
        font-size: var(--font-size-xs);
        color: var(--brand-text);
        cursor: pointer;

        input { flex-shrink: 0; cursor: pointer; }
        input:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
      }
      .ie-was { color: var(--brand-text-muted); }

      .ie-msg {
        display: flex;
        align-items: baseline;
        gap: var(--space-1);
        margin: 0;
        font-size: var(--font-size-2xs);

        &--err { color: var(--bs-danger); }
      }

      .ie-scope { margin: 0; font-size: var(--font-size-2xs); font-style: italic; color: var(--brand-text-muted); }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegisteredTeamInlineEditorComponent implements OnInit {
    private readonly teamReg = inject(TeamRegistrationService);
    private readonly destroyRef = inject(DestroyRef);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
    private readonly injector = inject(Injector);

    readonly team = input.required<RegisteredTeamDto>();
    /** The linked library entry's name; null for an orphan (no library entry → no tick). */
    readonly libraryName = input<string | null>(null);
    readonly eventName = input('this event');

    /** Landed — carries the toast text; the host reloads, then shows it. */
    readonly saved = output<string>();
    readonly cancelled = output<void>();

    readonly lopChoices = LOP_CHOICES;

    readonly name = signal('');
    readonly lop = signal('');
    readonly propagate = signal(false);
    readonly saving = signal(false);
    readonly error = signal<string | null>(null);

    /** The stored level, on-scale — what "changed" is measured against. */
    private readonly lopBaseline = computed(() => normalizeLop(this.team().levelOfPlay));
    private readonly lopChanged = computed(() => this.lop().length > 0 && this.lop() !== this.lopBaseline());
    private readonly nameChanged = computed(() => this.name().trim() !== this.team().teamName.trim());

    readonly showPropagate = computed(() => {
        const lib = (this.libraryName() ?? '').trim();
        return !!lib && lib !== this.name().trim();
    });
    readonly propagateEffective = computed(() => this.showPropagate() && this.propagate());

    /** Teams.TeamName is varchar(100); Clubs.ClubTeams.ClubTeamName is varchar(80). */
    readonly maxLength = computed(() => (this.propagate() ? 80 : 100));

    readonly canSave = computed(() =>
        this.name().trim().length > 0 && (this.nameChanged() || this.lopChanged() || this.propagateEffective()));

    ngOnInit(): void {
        this.name.set(this.team().teamName);
        this.lop.set(this.lopBaseline());
        afterNextRender(() => {
            const input = this.host.nativeElement.querySelector<HTMLInputElement>('.ie-name');
            input?.focus();
            input?.select();
        }, { injector: this.injector });
    }

    cancel(): void {
        if (!this.saving()) this.cancelled.emit();
    }

    save(): void {
        if (this.saving() || !this.canSave()) return;
        const c: TeamRenameConfirmation = {
            name: this.name().trim(),
            alsoPropagate: this.propagateEffective(),
            // NULL = unchanged: the server leaves the level alone.
            levelOfPlay: this.lopChanged() ? this.lop() : null,
        };
        const message = renameSuccessMessage(this.team().teamName, c);
        this.saving.set(true);
        this.error.set(null);
        this.teamReg.renameRegisteredTeam(this.team().teamId, c.name, c.alsoPropagate, c.levelOfPlay)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.saving.set(false);
                    this.saved.emit(message);
                },
                error: (err: unknown) => {
                    this.saving.set(false);
                    this.error.set(extractHttpErrorMessage(err, 'Failed to save.'));
                },
            });
    }
}
