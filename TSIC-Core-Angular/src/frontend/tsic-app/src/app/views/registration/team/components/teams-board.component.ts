import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal, viewChild } from '@angular/core';
import type { AgeGroupDto, ClubTeamDto, RegisteredTeamDto, SameNameEventTeamDto } from '@core/api';
import { formatLop } from '@shared/teams/lop-choices';
import { ToastService } from '@shared-ui/toast.service';
import { type LibraryRegisterRequest } from './library-segment.types';
import { contrastText } from '../../../scheduling/shared/utils/scheduling-helpers';
import { LibraryPanelComponent } from './library-panel.component';
import { RegisteredTeamInlineEditorComponent } from './registered-team-inline-editor.component';
import { TeamAddRowComponent, type TeamAddedEvent } from './team-add-row.component';

/**
 * The Teams step as ONE board, two panels side by side (Todd 2026-09-27 — "like configure pools
 * and roster swapper … NO chance for confusion there"):
 *
 *   LEFT  — Club Team Library: the shared LibraryPanelComponent (the library page shows the same
 *           one). Every write on this side is to the LIBRARY (future events). Register → opens a
 *           small editor on the row (level + age group, preselected); confirming moves the team across.
 *   RIGHT — {event} Registered Teams: what this event holds (no money — the Payment step carries it). The pencil
 *           edits THIS EVENT's record; Undo / remove take a team back off.
 *
 * A team is on exactly one side, so "which list am I editing?" answers itself. One open row editor
 * across both sides. Owns no domain state: the Teams step feeds rows and runs every mutation.
 */
type BoardSide = 'lib' | 'reg';

@Component({
    selector: 'app-teams-board',
    standalone: true,
    imports: [LibraryPanelComponent, RegisteredTeamInlineEditorComponent, TeamAddRowComponent],
    template: `
    <div class="board" [class.board--list]="!showLibrary()">

      @if (showLibrary()) {
      <!-- ═══ LEFT: Club Team Library — the shared panel, the same one the library page shows ═══ -->
      <!-- Spotlight (Todd 2026-09-27): the side under the pointer — or holding keyboard focus, so an
           open editor keeps its side lit — is lifted; the other softens. Emphasis only: nothing
           moves or resizes. -->
      <app-library-panel
        [class.is-lit]="lit() === 'lib'" [class.is-soft]="lit() === 'reg'"
        (pointerenter)="hover.set('lib')" (pointerleave)="unhover('lib')"
        (focusin)="focus.set('lib')" (focusout)="unfocus('lib')"
        [clubTeams]="clubTeams()"
        [registeredTeams]="registeredTeams()"
        [ageGroups]="ageGroups()"
        [eventName]="eventName()"
        [clubName]="clubName()"
        [canRegister]="canRegister()"
        [actionInProgress]="actionInProgress()"
        [pendingLibraryOnly]="pendingLibraryOnly()"
        (register)="register.emit($event)"
        (addNew)="addNew.emit()"
        (librarySaved)="librarySaved.emit()"
        (archive)="archive.emit($event)"
        (delete)="delete.emit($event)"
        (restore)="restore.emit($event)"
        (editorOpened)="renameId.set(null)" />
      }

      <!-- ═══ RIGHT: this event's Registered Teams ═══ -->
      <!-- The event is named in the page title right above — here the side only has to say what it holds. -->
      <header class="board-head board-head--reg"
              [class.is-lit]="lit() === 'reg'" [class.is-soft]="lit() === 'lib'"
              (pointerenter)="hover.set('reg')" (pointerleave)="unhover('reg')">
        @if (!showLibrary()) {
          <!-- The list's zone heading, as loud as Add a Team's above it (Todd 2026-09-28). -->
          <div class="zone-head zone-head--reg">
            <span class="zone-icon" aria-hidden="true"><i class="bi bi-clipboard-check-fill"></i></span>
            <div class="zone-text">
              <h3 class="zone-title" id="board-reg-title">Your Registered Teams</h3>
              <p class="zone-sub">
                @if (registeredRows().length === 0) { Nothing registered for {{ eventName() }} yet. }
                @else { Registered for {{ eventName() }}. The pencil edits a team for this event only. }
              </p>
            </div>
            <span class="zone-count">{{ registeredRows().length }} {{ registeredRows().length === 1 ? 'team' : 'teams' }}</span>
          </div>
        } @else {
        <div class="board-head-text">
          <h3 class="board-title" id="board-reg-title" [attr.title]="'Registered for ' + eventName()">
            <i class="bi bi-clipboard-check-fill" aria-hidden="true"></i>Registered Teams
          </h3>
          <!-- The team count only (Todd 2026-09-27): no money on this side — Paid and Due now went
               with the row money; the Continue card and the Payment step carry it. -->
          @if (registeredRows().length === 0) {
            <span class="board-sub">None yet for this event</span>
          } @else {
            <span class="board-sub board-stats">
              <span class="stat"><span class="stat-key">Teams</span><span class="stat-val">{{ registeredRows().length }}</span></span>
            </span>
          }
        </div>
        }
      </header>

      @if (!showLibrary() && canRegister()) {
        <!-- The one way in (Todd 2026-09-28): pick a library team or type a new one. Outside the
             panel, so its dropdown is never clipped by the panel's scroll box. -->
        <app-team-add-row class="board-add"
          [clubTeams]="clubTeams()"
          [registeredTeams]="registeredTeams()"
          [ageGroups]="ageGroups()"
          [clubName]="clubName()"
          [eventName]="eventName()"
          [sameNameEventTeams]="sameNameEventTeams()"
          [actionInProgress]="actionInProgress()"
          (started)="addStarted.emit()"
          (added)="teamAdded.emit($event)"
          (failed)="addFailed.emit($event)" />
      }

      <section class="panel panel--reg" aria-labelledby="board-reg-title"
               [class.is-lit]="lit() === 'reg'" [class.is-soft]="lit() === 'lib'"
               (pointerenter)="hover.set('reg')" (pointerleave)="unhover('reg')"
               (focusin)="focus.set('reg')" (focusout)="unfocus('reg')">
        <!-- One line per team, in columns (Ann 2026-09-27 — scans like prod's grid): the rows share
             the body's tracks (subgrid), so the badges and names line up down the list. No money per row
             (Ann/Todd 2026-09-27) — the Payment step's grid carries it. -->
        <div class="panel-body reg-grid" [class.is-short]="registeredRows().length + sameNameEventTeams().length <= 3">
          @if (registeredRows().length > 0 || otherRepGroups().length > 0) {
            <div class="reg-cols" aria-hidden="true">
              <span class="col-num">#</span><span title="Age group">AG</span><span>Team</span><span class="col-lop">LOP</span>
            </div>
          }
          @if (otherRepGroups().length > 0) {
            <!-- Other reps share this club name here (Todd 2026-10-06): each rep's teams under their
                 own name, this rep's first — one list would read as one club's bill. -->
            <div class="reg-group reg-group--own">
              <i class="bi bi-person-fill" aria-hidden="true"></i>
              <span class="reg-group-name">You{{ repName() ? ' (' + repName() + ')' : '' }}</span>
            </div>
          }
          @for (t of registeredRows(); track t.teamId; let i = $index) {
            @let undoMin = undoMinutesLeft(t.teamId);
            @let removable = canRemove() && t.paidTotal === 0;
            <div class="reg-row" [class.is-open]="renameId() === t.teamId" [class.reg-row--mine]="otherRepGroups().length > 0">
              @if (renameId() === t.teamId) {
                <!-- This event's name + level, edited in the row (Todd 2026-09-27, inline, no modal). -->
                <app-registered-team-inline-editor class="reg-editor-host"
                  [team]="t"
                  [eventName]="eventName()"
                  [registeredTeams]="registeredTeams()"
                  [undoMinutes]="undoMinutesLeft(t.teamId)"
                  (undo)="remove.emit(t)"
                  (saved)="onRenameSaved($event)"
                  (cancelled)="renameId.set(null)" />
              } @else {
                <!-- The age group first, as a badge in ITS color (Todd 2026-09-27) — down the left edge the
                     colors read as a column. A WAITLIST age group is an age group like any other: its own
                     name, its own color, no special casing. No "registered in" — this whole side is. -->
                <span class="reg-num">{{ i + 1 }}</span>
                <span class="reg-ag">
                  <span class="ag-badge" [style.background]="agBg(t.ageGroupColor)" [style.color]="agText(t.ageGroupColor)">{{ t.ageGroupName }}</span>
                </span>
                <!-- The team cell carries its own actions (Todd 2026-09-27): pencil just before the name it
                     edits, trash (when it applies) just after — never side by side. LOP is its own column. -->
                <span class="reg-team">
                  <button type="button" class="btn-icon" [class.is-locked]="!!renameLockReason()"
                          [disabled]="actionInProgress()"
                          [attr.aria-disabled]="!!renameLockReason()"
                          [attr.aria-label]="'Edit the registration of ' + t.teamName"
                          [attr.title]="renameLockReason() ?? 'Edit the registration of ' + t.teamName + ' for ' + eventName() + ' (name, level of play). Your Club Team Library is not changed.'"
                          (click)="renameLockReason() ? explainLock(renameLockReason()!) : startRename(t)">
                    <i class="bi bi-pencil" aria-hidden="true"></i>
                  </button>
                  <span class="row-name" [attr.title]="t.teamName">{{ t.teamName }}</span>
                  @if (!removable && undoMin > 0) {
                    <!-- The mistake-undo is a trash can too (Todd 2026-09-27) — a worded button broke the
                         one-line row. Same action and same words as remove; no countdown anywhere. -->
                    <button type="button" class="btn-icon btn-icon--danger" [disabled]="actionInProgress()"
                            [attr.aria-label]="'Remove ' + t.teamName + ' from ' + eventName()"
                            [attr.title]="'Remove ' + t.teamName + ' from ' + eventName()"
                            (click)="remove.emit(t)">
                      <i class="bi bi-trash" aria-hidden="true"></i>
                    </button>
                  } @else if (removable) {
                    <button type="button" class="btn-icon btn-icon--danger" [disabled]="actionInProgress()"
                            [attr.aria-label]="'Remove ' + t.teamName + ' from ' + eventName()"
                            [attr.title]="'Remove ' + t.teamName + ' from ' + eventName()"
                            (click)="remove.emit(t)">
                      <i class="bi bi-trash" aria-hidden="true"></i>
                    </button>
                  }
                </span>
                <span class="reg-lop"><span class="visually-hidden">Level of play </span>{{ formatLop(t.levelOfPlay) || '—' }}</span>
              }
            </div>
          } @empty {
            <div class="reg-row row-placeholder">
              <span class="row-meta">
                @if (!canRegister()) {
                  <span>Team registration for <b class="ev-name">{{ eventName() }}</b> is closed.</span>
                } @else if (!showLibrary()) {
                  <span>Add your first team above &mdash; pick it from your Club Team Library or type its name.</span>
                } @else if (empty()) {
                  <span>Build your list of teams in the Club Team Library, then press <b>Register</b> on each one you're bringing.</span>
                } @else if (activeCount() === 0) {
                  <span>Restore or add a team in the Club Team Library, then press <b>Register</b> on it.</span>
                } @else {
                  <span>Press <b>Register</b> on a library team to register it for <b class="ev-name">{{ eventName() }}</b>.</span>
                }
              </span>
            </div>
          }
          @for (g of otherRepGroups(); track g.repName) {
            <div class="reg-group reg-group--other">
              <i class="bi bi-person" aria-hidden="true"></i>
              <span class="reg-group-name">{{ g.repName }}</span>
              <span class="reg-group-note">Registered and paid for by {{ g.repName }} &mdash; not on your bill.</span>
            </div>
            @for (t of g.teams; track $index) {
              <!-- Read-only: another rep's team — no pencil, no trash, no level of play. -->
              <div class="reg-row reg-row--other">
                <span class="reg-num"></span>
                <span class="reg-ag">
                  <span class="ag-badge" [style.background]="agBg(ageGroupColors().get(t.ageGroupName))"
                        [style.color]="agText(ageGroupColors().get(t.ageGroupName))">{{ t.ageGroupName }}</span>
                </span>
                <span class="reg-team"><span class="pencil-gap" aria-hidden="true"></span><span class="row-name" [attr.title]="t.teamName">{{ t.teamName }}</span></span>
                <span class="reg-lop"></span>
              </div>
            }
          }
        </div>
      </section>
    </div>
    `,
    styleUrls: ['./teams-board.shared.scss', './zone-heading.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamsBoardComponent {
    private readonly toast = inject(ToastService);
    private readonly libraryPanel = viewChild(LibraryPanelComponent);
    private readonly addRow = viewChild(TeamAddRowComponent);

    /**
     * An inline form is open or holds unsaved input — the add row started, a registered row's
     * editor, a library row's editor or register editor. The wizard keeps Back / Proceed shut
     * while it is (Todd 2026-09-28).
     */
    readonly editing = computed(() =>
        (this.addRow()?.dirty() ?? false)
        || this.renameId() !== null
        || this.libraryPanel()?.openId() != null
        || this.libraryPanel()?.editId() != null);

    readonly clubTeams = input.required<readonly ClubTeamDto[]>();
    readonly registeredTeams = input<readonly RegisteredTeamDto[]>([]);
    readonly ageGroups = input<readonly AgeGroupDto[]>([]);
    readonly eventName = input('this event');
    readonly clubName = input('');
    /** Teams other same-name reps already registered here — the add row's duplicate warning. */
    readonly sameNameEventTeams = input<readonly SameNameEventTeamDto[]>([]);
    /** The signed-in rep's name — heads their own group when other reps' groups follow. */
    readonly repName = input('');
    /** Team registration open AND the director allows adds. */
    readonly canRegister = input(false);
    /** The director's own remove toggle (unpaid rows); the mistake-undo is separate. */
    readonly canRemove = input(false);
    /** Non-null = the pencil is locked, with this reason. */
    readonly renameLockReason = input<string | null>(null);
    readonly actionInProgress = input(false);
    readonly undoDeadlines = input<ReadonlyMap<string, number>>(new Map());
    readonly now = input(0);
    readonly pendingLibraryOnly = input<ReadonlySet<number>>(new Set());
    /**
     * false = no library panel (Todd 2026-09-28): Registered Teams alone, one column, and teams come
     * in through the add row on top of it — a library team picked, or a new one typed.
     */
    readonly showLibrary = input(true);

    /** The add row registered a team: the step reloads, then toasts this. */
    readonly teamAdded = output<TeamAddedEvent>();
    /** Add pressed: the step locks the screen until its reload lands. */
    readonly addStarted = output<void>();
    /** The add was refused: the step unlocks (reload = a library row was made first, show it). */
    readonly addFailed = output<{ reload: boolean }>();
    readonly register = output<LibraryRegisterRequest>();
    readonly remove = output<RegisteredTeamDto>();
    /** An inline event edit landed — carries the toast text; the step reloads, then shows it. */
    readonly renameSaved = output<string>();
    readonly addNew = output<void>();
    /** A library edit saved (inline) — the step reloads. */
    readonly librarySaved = output<void>();
    readonly archive = output<ClubTeamDto>();
    readonly delete = output<ClubTeamDto>();
    readonly restore = output<ClubTeamDto>();

    readonly formatLop = formatLop;

    /** The registered row being edited inline (teamId). */
    readonly renameId = signal<string | null>(null);

    // ── Spotlight: which side is emphasized. The pointer wins while it is over a side; otherwise
    //    the side holding keyboard focus (an open editor) stays lit. Neither → both at rest. ──
    readonly hover = signal<BoardSide | null>(null);
    readonly focus = signal<BoardSide | null>(null);
    /** One side only (no library panel) → nothing to spotlight against. */
    readonly lit = computed(() => this.showLibrary() ? this.hover() ?? this.focus() : null);
    unhover(side: BoardSide): void { if (this.hover() === side) this.hover.set(null); }
    unfocus(side: BoardSide): void { if (this.focus() === side) this.focus.set(null); }

    readonly activeCount = computed(() => this.clubTeams().filter(t => !t.bArchived).length);
    readonly empty = computed(() => this.clubTeams().length === 0);

    /** This event's teams, by age group then name — the shape a director reads them in. */
    readonly registeredRows = computed(() => [...this.registeredTeams()].sort((a, b) =>
        (a.ageGroupName ?? '').localeCompare(b.ageGroupName ?? '') || a.teamName.localeCompare(b.teamName)));

    /**
     * Other same-name reps' teams in this event, one group per rep (Todd 2026-10-06) — read-only, shown
     * under the rep's own so a shared club name never reads as one list. Same order as registeredRows.
     */
    readonly otherRepGroups = computed(() => {
        const byRep = new Map<string, SameNameEventTeamDto[]>();
        for (const t of this.sameNameEventTeams()) {
            const rep = t.repName.trim() || 'Another club rep';
            byRep.set(rep, [...(byRep.get(rep) ?? []), t]);
        }
        return [...byRep].map(([repName, teams]) => ({
            repName,
            teams: [...teams].sort((a, b) =>
                a.ageGroupName.localeCompare(b.ageGroupName) || a.teamName.localeCompare(b.teamName)),
        }));
    });

    /** Another rep's team has no color of its own here: borrow the age group's from a team of ours. */
    readonly ageGroupColors = computed(() =>
        new Map(this.registeredTeams().map(t => [t.ageGroupName ?? '', t.ageGroupColor ?? null])));

    /** Age-group badge: the age group's own color, text picked for contrast (the scheduling helper). */
    agBg(color: string | null | undefined): string { return color || 'var(--bs-secondary-bg)'; }
    agText(color: string | null | undefined): string { return contrastText(color); }


    undoMinutesLeft(teamId: string): number {
        const deadline = this.undoDeadlines().get(teamId);
        if (deadline === undefined) return 0;
        const ms = deadline - this.now();
        return ms > 0 ? Math.ceil(ms / 60000) : 0;
    }

    explainLock(reason: string): void { this.toast.show(reason, 'warning', 3500); }

    // ── Inline registered-team edit (this event's name + level) ──
    startRename(t: RegisteredTeamDto): void {
        this.libraryPanel()?.closeEditors();
        this.renameId.set(t.teamId);
    }

    onRenameSaved(message: string): void {
        this.renameId.set(null);
        this.renameSaved.emit(message);
    }
}
