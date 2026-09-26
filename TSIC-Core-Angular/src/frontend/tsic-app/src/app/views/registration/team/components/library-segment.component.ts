import { AfterViewChecked, ChangeDetectionStrategy, Component, ElementRef, OnChanges, SimpleChanges, computed, inject, input, output, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import type { AgeGroupDto, ClubTeamDto, RegisteredTeamDto } from '@core/api';
import { formatLop } from '@shared/teams/lop-choices';
import { clubTeamEditLockReason, clubTeamRemoval, type ClubTeamLockContext, type ClubTeamRemoval } from '@shared/teams/club-team-locks';
import { ToastService } from '@shared-ui/toast.service';
import { resolveOldestOfferedGradYear } from './event-age-group.util';
import { byGradYearThenName, fitsEvent } from './library-register-plan';

interface SegmentRow {
    team: ClubTeamDto;
    /** This event's copy, when registered or waitlisted here. */
    registered: RegisteredTeamDto | null;
    /** A director moved this team's registration here into a DROPPED age group. */
    dropped: boolean;
}

/**
 * Club Team Library — one of the Teams step's two segments (the other is the event's Registered
 * Teams). Replaces the library fly-in (Todd 2026-09-26).
 *
 * LIST-ONLY (Todd 2026-09-26: registering FROM the library blurred the club's list with an event
 * act; the Register-a-team modal is the one register path). Every write here is to the LIBRARY —
 * the teams every future event starts from — and the segment says so where the rep acts:
 *
 *   LEFT  — the library team: name, grad year, level of play, and Edit / Archive / Delete / Restore.
 *   RIGHT — this event, READ-ONLY: where the team is registered here, or that it isn't.
 *
 * Owns no domain state: the Teams step feeds rows and flags and runs every mutation.
 */
@Component({
    selector: 'app-library-segment',
    standalone: true,
    imports: [NgTemplateOutlet],
    template: `
    <!-- ── The head: GET THE LIST RIGHT when nothing is registered here yet (Todd 2026-09-26). The
         rep most likely to be here is a legacy veteran whose library was filled FOR them from past
         registrations, so the first questions are "do I need new teams, should I archive my
         graduated ones, can I fix these names?". Guide, don't intrude: no team is picked for them.
         Once anything is registered, one quiet line. Either way the head says what an edit here
         touches: the library, i.e. future events — never a registration. ── -->
    @if (listFirst()) {
      <div class="list-first">
        <div class="list-first-text">
          <strong class="list-first-title">
            <i class="bi bi-collection-fill" aria-hidden="true"></i>
            Before you register, make sure your Club Team Library is right.
          </strong>
          <span class="list-first-why">
            It's the list of teams every event you register for starts from &mdash; this one and every one after.
          </span>
          <ul class="list-first-checks">
            <li><span class="lf-q">New team this season?</span> <b>Add a New Team</b>.</li>
            <li><span class="lf-q">Graduated or moved on?</span> <b>Archive</b> it &mdash; its history is kept.</li>
            <li><span class="lf-q">Name, grad year or level out of date?</span> <b>Edit</b> it.</li>
          </ul>
          @if (canRegister()) {
            <span class="list-first-then">When your list is right, register the teams you're bringing to {{ eventName() }}.</span>
          } @else {
            <span class="list-first-then">Team registration for {{ eventName() }} is closed. Your library is still yours to keep right.</span>
          }
        </div>
        <div class="list-first-actions">
          @if (canRegister()) {
            <button type="button" class="btn-register-teams" [disabled]="actionInProgress()" (click)="openRegister.emit()">
              <i class="bi bi-trophy-fill" aria-hidden="true"></i>
              Register teams for {{ eventName() }}
            </button>
          }
          <button type="button" class="btn-add-team" [disabled]="actionInProgress()" (click)="addNew.emit()">
            <i class="bi bi-plus-circle" aria-hidden="true"></i>
            {{ canRegister() ? 'Add a New Team' : 'Add Library Team' }}
          </button>
        </div>
      </div>
    } @else {
      <div class="seg-lib-head">
        <p class="lib-lede">
          {{ clubPossessive() }} Club Team Library &mdash; the teams every event you register for starts from.
          Add, edit or archive here to keep it right for the next one.
        </p>
        <button type="button" class="btn-add-team" [disabled]="actionInProgress()" (click)="addNew.emit()">
          <i class="bi bi-plus-circle" aria-hidden="true"></i>
          {{ canRegister() ? 'Add a New Team' : 'Add Library Team' }}
        </button>
      </div>
    }

    <!-- What Edit means HERE, said once, above every Edit button (Todd 2026-09-26). -->
    <p class="edit-scope">
      <i class="bi bi-pencil" aria-hidden="true"></i>
      <span><b>Edit</b> changes the team in your library, so every future event starts from the new details.
        A team already registered for {{ eventName() }} keeps its registration as it is &mdash; change that on
        <b>{{ eventName() }} Registered Teams</b>.</span>
    </p>

    <!-- One row's cells. Shared by every group so a team looks the same wherever it sits. -->
    <ng-template #rowTpl let-row>
      @let team = row.team;
      @let reg = row.registered;
      <div class="lib-row"
           [class.is-registered]="!!reg"
           [class.is-archived]="team.bArchived"
           [class.is-pending]="isPending(team.clubTeamId)"
           [attr.data-club-team-id]="team.clubTeamId">

        <!-- LEFT: the library team + library housekeeping -->
        <div class="col-team">
          <span class="team-name" [attr.title]="team.clubTeamName">{{ team.clubTeamName }}</span>
          <span class="team-meta">
            <span class="meta-pair"><span class="meta-key">Grad</span>{{ team.clubTeamGradYear || '—' }}</span>
            <span class="meta-pair"><span class="meta-key">LOP</span>{{ formatLop(team.clubTeamLevelOfPlay) || '—' }}</span>
            @if (isPending(team.clubTeamId) && !reg) {
              <span class="pending-pill"><i class="bi bi-exclamation-triangle-fill" aria-hidden="true"></i>Just added &middot; not registered yet</span>
            }
          </span>
          <span class="lib-actions">
            @if (team.bArchived) {
              <button type="button" class="btn-lib" [disabled]="actionInProgress()"
                      title="Restore to your active Club Team Library" (click)="restore.emit(team)">
                <i class="bi bi-arrow-counterclockwise" aria-hidden="true"></i>Restore
              </button>
            } @else {
              @let editLock = editLockReason(team);
              @let removal = removalFor(row);
              <button type="button" class="btn-lib" [class.is-locked]="!!editLock"
                      [disabled]="actionInProgress()"
                      [attr.title]="editLock ?? 'Edit this team in your Club Team Library — what future events start from. Its registration for ' + eventName() + ' is not changed.'"
                      [attr.aria-disabled]="!!editLock"
                      (click)="editLock ? explainLock(editLock) : edit.emit(team)">
                <i class="bi bi-pencil" aria-hidden="true"></i>Edit
              </button>
              @if (removal.kind === 'archive') {
                <button type="button" class="btn-lib" [class.is-locked]="!!removal.lockReason"
                        [disabled]="actionInProgress()"
                        [attr.title]="removal.lockReason ?? 'Archive: hide it from your Club Team Library, keep its history'"
                        [attr.aria-disabled]="!!removal.lockReason"
                        (click)="removal.lockReason ? explainLock(removal.lockReason) : archive.emit(team)">
                  <i class="bi bi-box-arrow-in-down" aria-hidden="true"></i>Archive
                </button>
              } @else {
                <button type="button" class="btn-lib btn-lib--danger" [class.is-locked]="!!removal.lockReason"
                        [disabled]="actionInProgress()"
                        [attr.title]="removal.lockReason ?? 'Delete from your Club Team Library'"
                        [attr.aria-disabled]="!!removal.lockReason"
                        (click)="removal.lockReason ? explainLock(removal.lockReason) : delete.emit(team)">
                  <i class="bi bi-trash" aria-hidden="true"></i>Delete
                </button>
              }
            }
          </span>
        </div>

        <!-- RIGHT: this event — status only. Registering happens in the Register-a-team modal. -->
        <div class="col-event">
          @if (reg) {
            <span class="ev-done" [class.ev-done--wl]="reg.isWaitlisted">
              @if (reg.isWaitlisted) {
                <i class="bi bi-hourglass-split" aria-hidden="true"></i>On the {{ reg.ageGroupDisplayName || reg.ageGroupName }} waitlist
              } @else {
                <i class="bi bi-check-circle-fill" aria-hidden="true"></i>Registered in {{ reg.ageGroupDisplayName || reg.ageGroupName }}
              }
            </span>
            <span class="ev-sub">
              @if (reg.levelOfPlay) { LOP {{ formatLop(reg.levelOfPlay) }} }
              @if (reg.teamName && reg.teamName !== team.clubTeamName) { &middot; as {{ reg.teamName }} }
            </span>
          } @else if (row.dropped) {
            <span class="ev-muted"><i class="bi bi-x-circle" aria-hidden="true"></i>Dropped from {{ eventName() }} by the director</span>
          } @else if (team.bArchived) {
            <span class="ev-muted">Archived &middot; not offered for registration</span>
          } @else {
            <span class="ev-muted"><i class="bi bi-dash-circle" aria-hidden="true"></i>Not registered</span>
          }
        </div>
      </div>
    </ng-template>

    @if (activeCount() === 0 && archivedRows().length > 0) {
      <p class="all-archived">
        <i class="bi bi-archive" aria-hidden="true"></i>
        Every team in your Club Team Library is archived. Restore one below, or add a new team.
      </p>
    }

    <div class="lib-table" aria-label="Club Team Library">
      <div class="lib-head">
        <span>Library team</span>
        <span>At {{ eventName() }}</span>
      </div>

      <!-- Not registered here, and fits an age group -->
      @if (notRegisteredRows().length > 0) {
        <div class="lib-group">
          <span class="lib-group-title">Not registered</span>
          <span class="lib-group-count">{{ notRegisteredRows().length }}</span>
          <span class="lib-group-hint">fit an age group at {{ eventName() }}</span>
        </div>
        @for (row of notRegisteredRows(); track row.team.clubTeamId) {
          <ng-container *ngTemplateOutlet="rowTpl; context: { $implicit: row }" />
        }
      }

      <!-- Registered here: shown, never hidden, so the library never looks like it lost a team -->
      @if (registeredRows().length > 0) {
        <div class="lib-group lib-group--registered">
          <i class="bi bi-check-circle-fill" aria-hidden="true"></i>
          <span class="lib-group-title">Registered for {{ eventName() }}</span>
          <span class="lib-group-count">{{ registeredRows().length }}</span>
        </div>
        @for (row of registeredRows(); track row.team.clubTeamId) {
          <ng-container *ngTemplateOutlet="rowTpl; context: { $implicit: row }" />
        }
      }

      <!-- Older than every age group here. While the rep is getting the list right these are exactly
           the graduated teams they came to archive, so they show, open, with the hint that says so.
           Afterwards they fold away. -->
      @if (outsideRows().length > 0) {
        <button type="button" class="lib-group lib-group--toggle"
                [attr.aria-expanded]="outsideOpen()" (click)="toggleOutside()">
          <i class="bi" [class.bi-chevron-down]="outsideOpen()" [class.bi-chevron-right]="!outsideOpen()" aria-hidden="true"></i>
          <span class="lib-group-title">Older than every age group at {{ eventName() }}</span>
          <span class="lib-group-count">{{ outsideRows().length }}</span>
          <span class="lib-group-hint">
            @if (oldestOffered() !== null) { oldest here is {{ oldestOffered() }} &middot; }
            graduated or moved on? Archive them
          </span>
        </button>
        @if (outsideOpen()) {
          @for (row of outsideRows(); track row.team.clubTeamId) {
            <ng-container *ngTemplateOutlet="rowTpl; context: { $implicit: row }" />
          }
        }
      }

      <!-- Archived: folded; Restore only -->
      @if (archivedRows().length > 0) {
        <button type="button" class="lib-group lib-group--toggle"
                [attr.aria-expanded]="showArchived()" (click)="showArchived.set(!showArchived())">
          <i class="bi" [class.bi-chevron-down]="showArchived()" [class.bi-chevron-right]="!showArchived()" aria-hidden="true"></i>
          <i class="bi bi-archive-fill" aria-hidden="true"></i>
          <span class="lib-group-title">Archived</span>
          <span class="lib-group-count">{{ archivedRows().length }}</span>
          <span class="lib-group-hint">hidden from registration</span>
        </button>
        @if (showArchived()) {
          @for (row of archivedRows(); track row.team.clubTeamId) {
            <ng-container *ngTemplateOutlet="rowTpl; context: { $implicit: row }" />
          }
        }
      }
    </div>
    `,
    styles: [`
      :host { display: flex; flex-direction: column; gap: var(--space-3); }

      /* ── Head: list-first card (nothing registered here) or one quiet line ── */
      .seg-lib-head {
        display: flex;
        align-items: center;
        gap: var(--space-3);
      }

      .lib-lede {
        flex: 1;
        margin: 0;
        font-size: var(--font-size-sm);
        color: var(--brand-text-muted);
      }

      .list-first {
        display: flex;
        align-items: flex-start;
        gap: var(--space-3);
        padding: var(--space-3) var(--space-4);
        border: 1px solid color-mix(in srgb, var(--bs-primary) 25%, transparent);
        border-left: 3px solid var(--bs-primary);
        border-radius: var(--radius-md);
        background: color-mix(in srgb, var(--bs-primary) 6%, var(--brand-surface));
      }

      .list-first-text {
        flex: 1;
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
        min-width: 0;
        font-size: var(--font-size-sm);
        line-height: var(--line-height-normal);
        color: var(--brand-text);
      }

      .list-first-title {
        display: inline-flex;
        align-items: baseline;
        gap: var(--space-2);
        font-size: var(--font-size-base);
        line-height: var(--line-height-tight);

        .bi { color: var(--bs-primary); }
      }

      .list-first-checks {
        display: flex;
        flex-direction: column;
        gap: 2px;
        margin: 0;
        padding-left: var(--space-5);
      }

      .lf-q { font-weight: var(--font-weight-semibold); }

      .list-first-then { color: var(--brand-text-muted); }
      .list-first-why { color: var(--brand-text-muted); }

      .list-first-actions { display: flex; flex-direction: column; align-items: stretch; gap: var(--space-2); flex-shrink: 0; }

      .btn-register-teams {
        display: inline-flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        border: 1px solid var(--bs-success);
        border-radius: var(--radius-sm);
        background: var(--bs-success);
        color: var(--neutral-0);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        white-space: nowrap;
        cursor: pointer;
        box-shadow: var(--shadow-xs);
        transition: filter 0.12s ease;

        &:hover:not(:disabled) { filter: brightness(0.93); }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        &:disabled { opacity: 0.45; cursor: default; }
      }

      /* What Edit means on this tab — library, i.e. future events; never a registration */
      .edit-scope {
        display: flex;
        align-items: baseline;
        gap: var(--space-2);
        margin: 0;
        padding: var(--space-2) var(--space-3);
        border-radius: var(--radius-sm);
        background: color-mix(in srgb, var(--bs-primary) 5%, transparent);
        font-size: var(--font-size-xs);
        color: var(--brand-text);

        .bi { color: var(--bs-primary); flex-shrink: 0; }
      }

      .btn-add-team {
        display: inline-flex;
        align-items: center;
        gap: var(--space-2);
        flex-shrink: 0;
        padding: var(--space-2) var(--space-3);
        border: 1px solid var(--bs-primary);
        border-radius: var(--radius-sm);
        background: var(--brand-surface);
        color: var(--bs-primary);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        white-space: nowrap;
        cursor: pointer;
        transition: background-color 0.12s ease;

        &:hover:not(:disabled) { background: color-mix(in srgb, var(--bs-primary) 8%, var(--brand-surface)); }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        &:disabled { opacity: 0.45; cursor: default; }
      }

      .all-archived {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        margin: 0;
        font-size: var(--font-size-sm);
        color: var(--brand-text-muted);
      }

      /* ── Table ── */
      .lib-table {
        border: 1px solid var(--bs-border-color);
        border-radius: var(--radius-md);
        background: var(--brand-surface);
      }

      .lib-head,
      .lib-row {
        display: grid;
        grid-template-columns: minmax(0, 1fr) minmax(0, 1.1fr);
        gap: var(--space-3);
        padding: var(--space-2) var(--space-3);
      }

      .lib-head {
        border-bottom: 1px solid var(--bs-border-color);
        background: color-mix(in srgb, var(--bs-body-color) 3%, transparent);
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-semibold);
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--brand-text-muted);
      }

      .lib-row {
        align-items: center;
        border-bottom: 1px solid color-mix(in srgb, var(--bs-body-color) 6%, transparent);
        transition: background-color 0.1s ease;

        &:last-child { border-bottom: none; }
        &:hover { background: color-mix(in srgb, var(--bs-body-color) 2%, transparent); }

        &.is-pending {
          background: color-mix(in srgb, var(--bs-warning) 8%, transparent);
          box-shadow: inset 3px 0 0 var(--bs-warning);
        }
        &.is-archived .team-name { font-style: italic; font-weight: var(--font-weight-medium); color: var(--brand-text-muted); }
      }

      /* Group dividers inside the table */
      .lib-group {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        width: 100%;
        padding: var(--space-2) var(--space-3);
        border: none;
        border-bottom: 1px solid var(--bs-border-color);
        background: color-mix(in srgb, var(--bs-body-color) 3%, transparent);
        color: var(--brand-text-muted);
        text-align: left;
      }
      .lib-group:not(:first-child) { border-top: 1px solid var(--bs-border-color); }

      .lib-group--registered { color: var(--bs-success); }
      .lib-group--toggle {
        cursor: pointer;
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
      }

      .lib-group-title {
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-bold);
        text-transform: uppercase;
        letter-spacing: 0.06em;
      }

      .lib-group-count {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        min-width: 22px;
        padding: 1px var(--space-2);
        border-radius: var(--radius-full);
        background: color-mix(in srgb, var(--bs-body-color) 8%, transparent);
        color: var(--brand-text);
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-bold);
      }

      .lib-group-hint { font-size: var(--font-size-2xs); font-style: italic; }

      /* LEFT: library team */
      .col-team { display: flex; flex-direction: column; gap: 2px; min-width: 0; }

      .team-name {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
      }

      .team-meta {
        display: inline-flex;
        flex-wrap: wrap;
        align-items: baseline;
        gap: var(--space-3);
        font-size: var(--font-size-2xs);
        color: var(--brand-text-muted);
        font-variant-numeric: tabular-nums;
      }

      .meta-pair { display: inline-flex; align-items: baseline; gap: var(--space-1); }
      .meta-key { text-transform: uppercase; letter-spacing: 0.06em; font-weight: var(--font-weight-semibold); opacity: 0.7; }

      .pending-pill {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        color: var(--brand-text);
        font-weight: var(--font-weight-semibold);
        .bi { color: var(--bs-warning); }
      }

      .lib-actions { display: inline-flex; flex-wrap: wrap; gap: 2px; margin-left: calc(-1 * var(--space-1)); }

      .btn-lib {
        display: inline-flex;
        align-items: center;
        gap: 3px;
        padding: 1px var(--space-1);
        border: 1px solid transparent;
        border-radius: var(--radius-sm);
        background: transparent;
        color: var(--brand-text-muted);
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-medium);
        cursor: pointer;
        transition: background-color 0.1s ease, color 0.1s ease;

        &:hover:not(:disabled):not(.is-locked) {
          background: color-mix(in srgb, var(--bs-primary) 8%, transparent);
          color: var(--bs-primary);
        }
        &--danger:hover:not(:disabled):not(.is-locked) {
          background: color-mix(in srgb, var(--bs-danger) 10%, transparent);
          color: var(--bs-danger);
        }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        &:disabled { opacity: 0.3; cursor: default; }
        &.is-locked {
          opacity: 0.5;
          cursor: help;
          font-style: italic;
          text-decoration: line-through;
          text-decoration-thickness: 1px;
        }
      }


      /* RIGHT: this event — status only */
      .col-event { display: flex; flex-direction: column; align-items: flex-start; gap: 3px; min-width: 0; }

      .ev-sub {
        font-size: var(--font-size-2xs);
        color: var(--brand-text-muted);
        font-variant-numeric: tabular-nums;
      }

      .ev-done {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--bs-success);

        &--wl { color: var(--brand-text); .bi { color: var(--bs-warning); } }
      }

      .ev-muted {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
      }

      /* ── Mobile ── */
      @media (max-width: 575.98px) {
        .seg-lib-head, .list-first { flex-direction: column; align-items: stretch; }
        .list-first { padding: var(--space-3); }
        .list-first-actions { flex-direction: column; align-items: stretch; }
        .btn-add-team, .btn-register-teams { justify-content: center; }
        .lib-head { display: none; }
        .lib-row { grid-template-columns: 1fr; row-gap: var(--space-1); }
        .lib-group-hint { display: none; }
      }

      @media (prefers-reduced-motion: reduce) {
        .btn-add-team, .btn-register-teams, .btn-lib, .lib-row { transition: none !important; }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LibrarySegmentComponent implements OnChanges, AfterViewChecked {
    private readonly toast = inject(ToastService);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

    readonly clubTeams = input.required<readonly ClubTeamDto[]>();
    readonly registeredTeams = input<readonly RegisteredTeamDto[]>([]);
    readonly droppedTeams = input<readonly RegisteredTeamDto[]>([]);
    readonly ageGroups = input<readonly AgeGroupDto[]>([]);
    readonly clubName = input('');
    readonly eventName = input('this event');
    /** Team registration open AND the director allows adds. */
    readonly canRegister = input(false);
    readonly actionInProgress = input(false);
    /** Nothing is registered for this event yet: the head asks the rep to get the list right first. */
    readonly listFirst = input(false);
    /** Saved to the library (not registered) during this session — tinted and scrolled to. */
    readonly pendingLibraryOnly = input<ReadonlySet<number>>(new Set());
    /** Bumped by the step to bring the first pending row into view again. */
    readonly revealPending = input(0);

    /** "Register teams for {event}": the step opens the Register-a-team modal. */
    readonly openRegister = output<void>();
    readonly addNew = output<void>();
    readonly edit = output<ClubTeamDto>();
    readonly archive = output<ClubTeamDto>();
    readonly delete = output<ClubTeamDto>();
    readonly restore = output<ClubTeamDto>();

    readonly formatLop = formatLop;

    /**
     * The "older than every age group" fold: OPEN while the rep is getting the list right (those are
     * the graduated teams to archive), folded afterwards. The rep's own toggle wins either way.
     */
    private readonly outsideToggled = signal<boolean | null>(null);
    readonly outsideOpen = computed(() => this.outsideToggled() ?? this.listFirst());
    toggleOutside(): void { this.outsideToggled.set(!this.outsideOpen()); }
    readonly showArchived = signal(false);

    /** "STEPS Elite NJ's" / "Your club's". */
    readonly clubPossessive = computed(() => {
        const club = this.clubName().trim();
        return club && club !== 'your club' ? `${club}'s` : "Your club's";
    });

    readonly oldestOffered = computed(() => resolveOldestOfferedGradYear(this.ageGroups()));

    private readonly registeredByClubTeam = computed(() => {
        const map = new Map<number, RegisteredTeamDto>();
        for (const r of this.registeredTeams()) if (r.clubTeamId != null) map.set(r.clubTeamId, r);
        return map;
    });

    private readonly droppedClubTeamIds = computed(() =>
        new Set(this.droppedTeams().map(d => d.clubTeamId).filter((id): id is number => id != null)));

    /** Every library team with its status here, in club order (grad year, then name). */
    private readonly rows = computed<SegmentRow[]>(() => {
        const registered = this.registeredByClubTeam();
        const dropped = this.droppedClubTeamIds();
        return [...this.clubTeams()]
            .sort(byGradYearThenName)
            .map(team => ({ team, registered: registered.get(team.clubTeamId) ?? null, dropped: dropped.has(team.clubTeamId) }));
    });

    private fits(team: ClubTeamDto): boolean {
        return fitsEvent(team, { oldestOffered: this.oldestOffered() });
    }

    readonly notRegisteredRows = computed(() =>
        this.rows().filter(r => !r.team.bArchived && !r.registered && this.fits(r.team)));
    /** Registered here — including a team whose library row was archived since, so no registration ever drops out of sight. */
    readonly registeredRows = computed(() => this.rows().filter(r => !!r.registered));
    readonly outsideRows = computed(() =>
        this.rows().filter(r => !r.team.bArchived && !r.registered && !this.fits(r.team)));
    readonly archivedRows = computed(() => this.rows().filter(r => r.team.bArchived && !r.registered));
    readonly activeCount = computed(() => this.clubTeams().filter(t => !t.bArchived).length);

    // ── Locks: the shared rules, so this segment and the library page never disagree ──
    private lockContext(registeredHere: boolean): ClubTeamLockContext {
        return { registeredHere, eventLabel: this.eventName() };
    }
    editLockReason(team: ClubTeamDto): string | null { return clubTeamEditLockReason(team, this.lockContext(false)); }
    removalFor(row: SegmentRow): ClubTeamRemoval { return clubTeamRemoval(row.team, this.lockContext(!!row.registered)); }

    /** A locked action answers with its reason — touch has no tooltip. */
    explainLock(reason: string): void {
        this.toast.show(reason, 'warning', 3500);
    }

    isPending(clubTeamId: number): boolean {
        return this.pendingLibraryOnly().has(clubTeamId);
    }

    // ── A team saved to the library only is brought into view (it may land mid-list) ──
    private scrollToTeamId: number | null = null;

    ngOnChanges(changes: SimpleChanges): void {
        if (changes['revealPending'] && !changes['revealPending'].firstChange) {
            const first = [...this.pendingLibraryOnly()][0];
            if (first !== undefined) this.scrollToTeamId = first;
        }
        if (changes['pendingLibraryOnly']) {
            const prev = (changes['pendingLibraryOnly'].previousValue as ReadonlySet<number> | undefined) ?? new Set<number>();
            const current = [...this.pendingLibraryOnly()];
            const added = current.filter(id => !prev.has(id));
            const target = added.length ? added[added.length - 1] : (changes['pendingLibraryOnly'].firstChange ? current[0] : undefined);
            if (target !== undefined) {
                this.scrollToTeamId = target;
                // An outside-the-age-groups team is folded away by default; unfold it so it can be seen.
                const team = this.clubTeams().find(t => t.clubTeamId === target);
                if (team && !team.bArchived && !this.fits(team)) this.outsideToggled.set(true);
            }
        }
    }

    ngAfterViewChecked(): void {
        const id = this.scrollToTeamId;
        if (id === null) return;
        const row = this.host.nativeElement.querySelector<HTMLElement>(`[data-club-team-id="${id}"]`);
        if (!row) return; // the parent's reload hasn't landed yet — next pass
        this.scrollToTeamId = null;
        const reduce = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
        row.scrollIntoView({ block: 'center', behavior: reduce ? 'auto' : 'smooth' });
    }
}
