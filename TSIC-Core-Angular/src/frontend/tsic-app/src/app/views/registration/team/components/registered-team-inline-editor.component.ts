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
 *   • Save only when something moves: the name or a real level pick.
 *   • A level stored off the 1–5 scale starts BLANK and blank is never a change — nothing gets
 *     erased that nobody asked to erase.
 *   • THIS EVENT ONLY — no "rename it in my library too" (Todd 2026-09-27). The one right-side
 *     control that wrote the left side is gone; the library is renamed by its own editor, which
 *     carries the grad-year and different-team guards this row does not.
 *   • Teams.TeamName is varchar(100).
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
        <span class="ie-label">Team Name</span>
        <input class="ie-input ie-name" type="text" autocomplete="off"
               maxlength="100"
               [value]="name()" (input)="name.set($any($event.target).value)"
               (keydown.enter)="save()"
               [class.is-invalid]="!name().trim() || duplicate()" />
      </label>

      <!-- The row's "AG" line is hidden while the row is open — the one moment it matters most. -->
      <p class="ie-ag">
        <span class="ie-label">Registered age group</span>
        <span class="ie-ag-chip">{{ ageGroup() }}</span>
      </p>

      <!-- Reps try to MOVE a team by renaming it (Todd 2026-09-27). Always a warning; red, with the
           real way to move, the moment the name's year leaves the REGISTERED age group. -->
      @if (yearMoved(); as yr) {
        <div class="ie-move ie-move--alarm" role="alert">
          <p class="ie-move-line">
            <i class="bi bi-x-octagon-fill" aria-hidden="true"></i>
            <span><b>Renaming does not move this team.</b> {{ name().trim() }} is still registered in <b>{{ ageGroup() }}</b>.</span>
          </p>
          <p class="ie-move-how">
            @if (undoMinutes() > 0) {
              To play in {{ yr }}:
              <button type="button" class="btn-link" [disabled]="saving()" (click)="undo.emit()">Undo this registration</button>
              and register it again in {{ yr }}.
            } @else {
              Only the event director can move a team to another age group.
            }
          </p>
        </div>
      } @else {
        <div class="ie-move ie-move--warn">
          <p class="ie-move-line">
            <i class="bi bi-exclamation-triangle-fill" aria-hidden="true"></i>
            <span><b>Renaming does not change the registered age group.</b> This team stays registered in <b>{{ ageGroup() }}</b>.</span>
          </p>
        </div>
      }
      @if (duplicate()) {
        <p class="ie-msg ie-msg--err" role="alert"><i class="bi bi-exclamation-triangle" aria-hidden="true"></i>
          {{ name().trim() }} is already registered in {{ ageGroup() }} &mdash; give this team a different name.</p>
      }

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
            {{ saving() ? 'Saving…' : yearMoved() ? 'Rename — keep in ' + ageGroup() : 'Save' }}
          </button>
        </div>
      </div>

      @if (error()) { <p class="ie-msg ie-msg--err" role="alert"><i class="bi bi-exclamation-triangle" aria-hidden="true"></i>{{ error() }}</p> }
      <p class="ie-scope">
        {{ eventName() }} only &mdash; your Club Team Library stays as it is.
      </p>
    </div>
    `,
    styles: [`
      .ie { display: flex; flex-direction: column; gap: var(--space-2); }
      .ie-line { display: flex; flex-wrap: wrap; align-items: flex-end; gap: var(--space-2); }

      .ie-field { display: flex; flex-direction: column; gap: 1px; min-width: 0; margin: 0; }
      /* Wide enough for "5 (strongest)". */
      .ie-field--lop { width: 116px; flex-shrink: 0; }

      .ie-ag { display: flex; align-items: center; gap: var(--space-2); margin: 0; }
      .ie-ag-chip {
        padding: 1px var(--space-2);
        border-radius: var(--radius-sm);
        background: var(--bs-success);
        color: var(--neutral-0);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-bold);
      }

      .ie-move {
        display: flex;
        flex-direction: column;
        gap: var(--space-1);
        padding: var(--space-2);
        border-radius: var(--radius-sm);
        font-size: var(--font-size-xs);

        &--alarm {
          border: 1px solid var(--bs-danger);
          background: color-mix(in srgb, var(--bs-danger) 10%, var(--brand-surface));
          color: var(--brand-text);
          .ie-move-line .bi { color: var(--bs-danger); }
        }
        &--warn {
          border: 1px solid var(--bs-warning);
          background: color-mix(in srgb, var(--bs-warning) 14%, var(--brand-surface));
          color: var(--brand-text);
          .ie-move-line .bi { color: color-mix(in srgb, var(--bs-warning) 70%, var(--brand-text)); }
        }
      }
      .ie-move-line {
        display: flex;
        align-items: baseline;
        gap: var(--space-1);
        margin: 0;

        .bi { flex-shrink: 0; }
      }
      .ie-move-how { margin: 0; }

      .btn-link {
        padding: 0;
        border: none;
        background: transparent;
        color: var(--bs-primary);
        font-size: inherit;
        font-weight: var(--font-weight-semibold);
        text-decoration: underline;
        cursor: pointer;

        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); border-radius: var(--radius-sm); }
        &:disabled { opacity: 0.45; cursor: default; }
      }

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
    readonly eventName = input('this event');
    /** This event's registered teams — for the same-name-in-this-age-group check (the server enforces it too). */
    readonly registeredTeams = input<readonly RegisteredTeamDto[]>([]);
    /** Whole minutes of mistake-undo left on this team; 0 = none. Offered as the real way to move. */
    readonly undoMinutes = input(0);

    /** Landed — carries the toast text; the host reloads, then shows it. */
    readonly saved = output<string>();
    readonly cancelled = output<void>();
    /** "Undo this registration" — the host runs its usual remove confirm. */
    readonly undo = output<void>();

    readonly lopChoices = LOP_CHOICES;

    readonly name = signal('');
    readonly lop = signal('');
    readonly saving = signal(false);
    readonly error = signal<string | null>(null);

    /** The stored level, on-scale — what "changed" is measured against. */
    private readonly lopBaseline = computed(() => normalizeLop(this.team().levelOfPlay));
    private readonly lopChanged = computed(() => this.lop().length > 0 && this.lop() !== this.lopBaseline());
    private readonly nameChanged = computed(() => this.name().trim() !== this.team().teamName.trim());

    /** The age group as the rep reads it. */
    readonly ageGroup = computed(() => this.team().ageGroupDisplayName || this.team().ageGroupName);

    /**
     * The name's year, whenever it differs from the age group's year — the rename-to-move mistake.
     * No exemption for a name that already carried that year (Todd 2026-09-27: "that caution should
     * always appear"). An age group with no year in its name can't be compared: calm line only.
     */
    readonly yearMoved = computed<string | null>(() => {
        const year = (s: string | null | undefined) => /\b(20\d{2})\b/.exec(s ?? '')?.[1] ?? null;
        const typed = year(this.name());
        const agYear = year(this.ageGroup());
        return typed && agYear && typed !== agYear ? typed : null;
    });

    /** Another of this club's teams already has this name in this age group (Todd 2026-09-27). */
    readonly duplicate = computed(() => {
        const n = this.name().trim().toLowerCase();
        if (!n || !this.nameChanged()) return false;
        const t = this.team();
        return this.registeredTeams().some(o =>
            o.teamId !== t.teamId && o.ageGroupId === t.ageGroupId && o.teamName.trim().toLowerCase() === n);
    });

    readonly canSave = computed(() =>
        this.name().trim().length > 0 && !this.duplicate()
        && (this.nameChanged() || this.lopChanged()));

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
            // Never from the board: the library is renamed in the library (Todd 2026-09-27).
            alsoPropagate: false,
            // NULL = unchanged: the server leaves the level alone.
            levelOfPlay: this.lopChanged() ? this.lop() : null,
        };
        // When the name's year moved, the toast says it again: the team did not move.
        const message = renameSuccessMessage(this.team().teamName, c)
            + (this.yearMoved() ? ` It still plays in ${this.ageGroup()}.` : '');
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
