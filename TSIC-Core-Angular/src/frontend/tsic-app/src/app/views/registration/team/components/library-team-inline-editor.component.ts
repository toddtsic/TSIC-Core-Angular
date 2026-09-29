import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, OnInit, afterNextRender, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { ClubTeamDto } from '@core/api';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { ToastService } from '@shared-ui/toast.service';
import { LOP_CHOICES, normalizeLop } from '@shared/teams/lop-choices';
import { clubNameInTeamName, isBareYearName } from '@shared/teams/team-name-hints';
import { TeamNameSchedulePreviewComponent } from '@shared/teams/team-name-schedule-preview.component';
import { isDuplicateLibraryName, libraryGradYearOptions, looksLikeDifferentTeam, sameLibraryText } from '@shared/teams/library-team-form';

/**
 * Edit a library team IN ITS ROW on the teams board (Todd 2026-09-27: "make the editing inline …
 * drop the modals"). Same rules as the Edit modal (team-form-modal), from the same helpers:
 * club name in the team name blocks (with the live schedule label), a duplicate name blocks, a
 * bare year nudges, and a grad-year / name-year move on a team with history offers "add as a new
 * team instead" → archive the old one or keep both.
 *
 * A library write only — never an event's registration. Owns its own save, like the modal; the
 * board reloads on `saved`.
 */
@Component({
    selector: 'app-library-team-inline-editor',
    standalone: true,
    imports: [TeamNameSchedulePreviewComponent],
    template: `
    <div class="ie" role="group" [attr.aria-label]="'Edit ' + team().clubTeamName + ' in your Club Team Library'"
         (keydown.escape)="cancel()">
      @if (phase() === 'added') {
        <!-- "Add as a new team" landed; the old row is still active. -->
        <p class="ie-msg ie-msg--ok" role="status">
          <i class="bi bi-check-circle-fill" aria-hidden="true"></i>
          <span><b>{{ addedName() }}</b> is in your library.
            @if (archiveLockReason(); as lock) {
              {{ team().clubTeamName }} stays active: {{ lock }}.
            } @else {
              Archive <b>{{ team().clubTeamName }}</b>? Its event history stays with it and you can restore it any time.
            }
          </span>
        </p>
        @if (errorMsg()) { <p class="ie-msg ie-msg--err" role="alert">{{ errorMsg() }}</p> }
        <div class="ie-actions">
          @if (archiveLockReason()) {
            <button type="button" class="btn-save" (click)="keepBoth()">Done</button>
          } @else {
            <button type="button" class="btn-cancel" [disabled]="saving()" (click)="keepBoth()">Keep both</button>
            <button type="button" class="btn-save" [disabled]="saving()" (click)="archiveOld()">
              <i class="bi bi-box-arrow-in-down" aria-hidden="true"></i>Archive {{ team().clubTeamName }}
            </button>
          }
        </div>
      } @else {
        <label class="ie-field">
          <span class="ie-label">Team name</span>
          <input class="ie-input ie-name" type="text" autocomplete="off"
                 [value]="teamName()" (input)="teamName.set($any($event.target).value)"
                 (keydown.enter)="save()"
                 [class.is-invalid]="!teamName().trim() || nameContainsClub() || nameIsDuplicate()" />
        </label>

        <div class="ie-line">
          <label class="ie-field ie-field--grad">
            <span class="ie-label">Grad</span>
            <select class="ie-input" [class.is-invalid]="!gradYear()" (change)="gradYear.set($any($event.target).value)">
              <option value="" [selected]="!gradYear()">Pick…</option>
              @for (yr of gradYearOptions; track yr) {
                <option [value]="yr" [selected]="yr === gradYear()">{{ yr }}</option>
              }
            </select>
          </label>
          <label class="ie-field ie-field--lop">
            <span class="ie-label">Level</span>
            <select class="ie-input" [class.is-invalid]="!levelOfPlay()" (change)="levelOfPlay.set($any($event.target).value)">
              <option value="" [selected]="!levelOfPlay()">Pick…</option>
              @for (c of lopChoices; track c.value) {
                <option [value]="c.value" [selected]="c.value === levelOfPlay()">{{ c.label }}</option>
              }
            </select>
          </label>
          <div class="ie-actions ie-actions--inline">
            <button type="button" class="btn-cancel" (click)="cancel()">Cancel</button>
            <button type="button" class="btn-save" [disabled]="saving() || !canSave()" (click)="save()">
              {{ saving() ? 'Saving…' : 'Save' }}
            </button>
          </div>
        </div>

        <!-- The club-name hammer, once the name is touched or when it already carries the club. -->
        @if (nameEdited() || clubHit()) {
          <team-name-schedule-preview [clubName]="clubName()" [teamName]="teamName()" />
        }
        @if (nameIsDuplicate()) {
          <p class="ie-msg ie-msg--err"><i class="bi bi-exclamation-triangle" aria-hidden="true"></i>
            <b>{{ teamName().trim() }} &middot; {{ gradYear() }}</b> is already in your library &mdash; change the name or the grad year.</p>
        } @else if (nameIsBareYear()) {
          <p class="ie-msg"><i class="bi bi-lightbulb" aria-hidden="true"></i>
            Just a year? Add a word to tell teams apart &mdash; <b>{{ teamName().trim() }} Blue</b>. Optional.</p>
        }
        @if (differentTeam()) {
          <div class="ie-msg ie-msg--nudge" role="note">
            <i class="bi bi-signpost-split" aria-hidden="true"></i>
            <span><b>Looks like a different team?</b> {{ team().clubTeamName }} has event history. If that squad has
              moved on, add <b>{{ teamName().trim() }}</b> as a new team instead.
              <span class="ie-nudge-actions">
                <button type="button" class="btn-link" [disabled]="saving() || !canSave()" (click)="addInstead()">Add as a new team</button>
                <button type="button" class="btn-link" (click)="nudgeDismissed.set(true)">No, I'm fixing it</button>
              </span>
            </span>
          </div>
        }
        @if (errorMsg()) { <p class="ie-msg ie-msg--err" role="alert">{{ errorMsg() }}</p> }
        <p class="ie-scope">Changes your library only &mdash; registered teams keep their details.</p>
      }
    </div>
    `,
    styles: [`
      .ie { display: flex; flex-direction: column; gap: var(--space-2); }

      /* Grad | Level | Cancel Save — the buttons wrap under on a narrow column rather than squeeze. */
      .ie-line { display: flex; flex-wrap: wrap; align-items: flex-end; gap: var(--space-2); }

      .ie-field { display: flex; flex-direction: column; gap: 1px; min-width: 0; margin: 0; }
      .ie-field--grad { width: 84px; flex-shrink: 0; }
      .ie-field--lop { width: 116px; flex-shrink: 0; }

      .ie-label {
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-semibold);
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--brand-text-muted);
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

        &:focus-visible { outline: none; border-color: var(--bs-primary); box-shadow: var(--shadow-focus); }
        &.is-invalid { border-color: var(--bs-danger); }
      }
      select.ie-input { padding: 3px var(--space-1); font-size: var(--font-size-xs); cursor: pointer; }
      .ie-name { font-weight: var(--font-weight-semibold); }

      .ie-actions { display: flex; justify-content: flex-end; gap: var(--space-2); }
      .ie-actions--inline { margin-left: auto; }

      .btn-cancel, .btn-save {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
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
      .btn-save { border: 1px solid var(--bs-primary); background: var(--bs-primary); color: var(--neutral-0); }

      .btn-link {
        padding: 0;
        border: none;
        background: transparent;
        color: var(--bs-primary);
        font-size: inherit;
        font-weight: var(--font-weight-semibold);
        cursor: pointer;

        &:hover:not(:disabled) { text-decoration: underline; }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); border-radius: var(--radius-sm); }
        &:disabled { opacity: 0.45; cursor: default; }
      }

      .ie-msg {
        display: flex;
        align-items: baseline;
        gap: var(--space-1);
        margin: 0;
        font-size: var(--font-size-2xs);
        color: var(--brand-text);

        .bi { flex-shrink: 0; color: var(--bs-warning); }
        &--err { color: var(--bs-danger); .bi { color: var(--bs-danger); } }
        &--ok .bi { color: var(--bs-success); }
        &--nudge {
          padding: var(--space-2);
          border-radius: var(--radius-sm);
          background: color-mix(in srgb, var(--bs-warning) 10%, var(--brand-surface));
        }
      }

      .ie-nudge-actions { display: flex; flex-wrap: wrap; gap: var(--space-3); margin-top: var(--space-1); }

      .ie-scope { margin: 0; font-size: var(--font-size-2xs); font-style: italic; color: var(--brand-text-muted); }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LibraryTeamInlineEditorComponent implements OnInit {
    private readonly teamReg = inject(TeamRegistrationService);
    private readonly toast = inject(ToastService);
    private readonly destroyRef = inject(DestroyRef);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
    private readonly injector = inject(Injector);

    readonly team = input.required<ClubTeamDto>();
    readonly clubName = input('');
    readonly existingTeams = input<readonly ClubTeamDto[]>([]);
    /** Why the team being edited can't be archived now (null = it can) — for the add-instead follow-up. */
    readonly archiveLockReason = input<string | null>(null);

    /** A write landed — the board reloads and closes the editor. */
    readonly saved = output<void>();
    readonly cancelled = output<void>();

    readonly gradYearOptions = libraryGradYearOptions();
    readonly lopChoices = LOP_CHOICES;

    readonly teamName = signal('');
    readonly gradYear = signal('');
    readonly levelOfPlay = signal('');
    readonly saving = signal(false);
    readonly errorMsg = signal<string | null>(null);
    readonly nudgeDismissed = signal(false);
    readonly phase = signal<'form' | 'added'>('form');
    readonly addedName = signal('');

    readonly clubHit = computed(() => clubNameInTeamName(this.clubName(), this.teamName()));
    readonly nameContainsClub = computed(() => this.clubHit() === 'full');
    readonly nameIsDuplicate = computed(() =>
        isDuplicateLibraryName(this.existingTeams(), this.teamName(), this.gradYear(), this.team().clubTeamId));
    readonly nameIsBareYear = computed(() => isBareYearName(this.teamName()));
    readonly nameEdited = computed(() => this.teamName().trim() !== this.team().clubTeamName.trim());
    readonly differentTeam = computed(() =>
        !this.nudgeDismissed() && looksLikeDifferentTeam(this.team(), this.teamName(), this.gradYear()));

    readonly canSave = computed(() =>
        !!this.teamName().trim() && !this.nameContainsClub() && !this.nameIsDuplicate()
        && !!this.gradYear() && !!this.levelOfPlay());

    ngOnInit(): void {
        const t = this.team();
        this.teamName.set(t.clubTeamName);
        this.gradYear.set(t.clubTeamGradYear ?? '');
        // The library stores LOP free-form; the select speaks the 1–5 scale.
        this.levelOfPlay.set(normalizeLop(t.clubTeamLevelOfPlay));
        afterNextRender(() => {
            const input = this.host.nativeElement.querySelector<HTMLInputElement>('.ie-name');
            input?.focus();
            input?.select();
        }, { injector: this.injector });
    }

    cancel(): void {
        if (this.saving()) return;
        if (this.phase() === 'added') { this.saved.emit(); return; }
        this.cancelled.emit();
    }

    save(): void {
        if (this.saving() || !this.canSave()) return;
        const t = this.team();
        this.saving.set(true);
        this.errorMsg.set(null);
        this.teamReg.updateClubTeam(t.clubTeamId, {
            clubTeamName: this.teamName().trim(),
            clubTeamGradYear: this.gradYear(),
            clubTeamLevelOfPlay: this.levelOfPlay().trim(),
        })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.saving.set(false);
                    this.toast.show('Library team updated. No event was changed.', 'success', 2500);
                    this.saved.emit();
                },
                error: (err: unknown) => {
                    this.saving.set(false);
                    this.errorMsg.set((err as { error?: { message?: string } })?.error?.message || 'Failed to update team.');
                },
            });
    }

    /** Create the typed team as a NEW library row, leave the old one, then offer to archive it. */
    addInstead(): void {
        if (this.saving() || !this.canSave()) return;
        const old = this.team();
        const name = this.teamName().trim();
        // Name + grad year is the identity: a new grad year alone makes a new team.
        if (sameLibraryText(name, old.clubTeamName) && sameLibraryText(this.gradYear(), old.clubTeamGradYear)) {
            this.errorMsg.set(`Change the name or the grad year — ${old.clubTeamName} keeps this one.`);
            return;
        }
        this.saving.set(true);
        this.errorMsg.set(null);
        this.teamReg.createClubTeam({
            clubTeamName: name,
            clubTeamGradYear: this.gradYear(),
            levelOfPlay: this.levelOfPlay().trim() || undefined,
        })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.saving.set(false);
                    this.addedName.set(name);
                    this.phase.set('added');
                },
                error: (err: unknown) => {
                    this.saving.set(false);
                    this.errorMsg.set((err as { error?: { message?: string } })?.error?.message || 'Failed to add the team.');
                },
            });
    }

    archiveOld(): void {
        const old = this.team();
        if (this.saving() || this.archiveLockReason()) return;
        this.saving.set(true);
        this.errorMsg.set(null);
        this.teamReg.archiveClubTeam(old.clubTeamId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.saving.set(false);
                    this.toast.show(`${this.addedName()} added. ${old.clubTeamName} archived with its history.`, 'success', 3500);
                    this.saved.emit();
                },
                error: (err: unknown) => {
                    this.saving.set(false);
                    this.errorMsg.set((err as { error?: { message?: string } })?.error?.message || 'Failed to archive the team.');
                },
            });
    }

    keepBoth(): void {
        this.toast.show(`${this.addedName()} added to your library.`, 'success', 2500);
        this.saved.emit();
    }
}
