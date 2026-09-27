import { ChangeDetectionStrategy, Component, ElementRef, Injector, afterNextRender, computed, inject, input, output, signal } from '@angular/core';
import { CurrencyPipe, DatePipe, NgTemplateOutlet } from '@angular/common';
import type { AgeGroupDto, ClubTeamDto, RegisteredTeamDto } from '@core/api';
import { LOP_CHOICES, formatLop, normalizeLop } from '@shared/teams/lop-choices';
import { clubTeamArchiveLockReason, clubTeamEditLockReason, clubTeamRemoval, type ClubTeamRemoval } from '@shared/teams/club-team-locks';
import { ToastService } from '@shared-ui/toast.service';
import { resolveRecommendedAgeGroupId, type SlotPricing } from './event-age-group.util';
import { ageGroupLabel, type LibraryRegisterRequest } from './library-segment.types';
import { ageGroupWaitlists, byGradYearThenName, pricingOfAgeGroup } from './library-register-plan';
import { boardFeeStatusOf, type BoardFeeStatus } from './board-money';
import { LibraryTeamInlineEditorComponent } from './library-team-inline-editor.component';
import { RegisteredTeamInlineEditorComponent } from './registered-team-inline-editor.component';

/** The open register editor's two picks. '' = no pick. */
interface RegPick { lop: string; ag: string; }

interface LibRow {
    team: ClubTeamDto;
    registered: RegisteredTeamDto | null;
}

/**
 * The Teams step as ONE board, two panels side by side (Todd 2026-09-27 — "like configure pools
 * and roster swapper … NO chance for confusion there"):
 *
 *   LEFT  — Club Team Library: the club's teams not registered here. Every write on this side is
 *           to the LIBRARY (future events). Register → opens a small editor on the row (level +
 *           age group, preselected); confirming moves the team across.
 *   RIGHT — {event} Registered Teams: what this event holds and what each team owes. The pencil
 *           edits THIS EVENT's record; Undo / remove take a team back off.
 *
 * A team is on exactly one side, so "which list am I editing?" answers itself. Owns no domain
 * state: the Teams step feeds rows and runs every mutation.
 */
@Component({
    selector: 'app-teams-board',
    standalone: true,
    imports: [CurrencyPipe, DatePipe, NgTemplateOutlet, LibraryTeamInlineEditorComponent, RegisteredTeamInlineEditorComponent],
    template: `
    <div class="board">

      <!-- ═══ LEFT: Club Team Library ═══
           Headers sit ABOVE their panels, in one grid row, so both are always the same height and
           the panels start level (Todd 2026-09-27). -->
      <header class="board-head board-head--lib">
        <div class="board-head-text">
          <h3 class="board-title" id="board-lib-title">
            <i class="bi bi-collection-fill" aria-hidden="true"></i>Club Team Library
          </h3>
          <!-- CRITICAL (Todd 2026-09-27): says what this list IS — never drop it for the group labels. -->
          <span class="board-sub">Your teams to choose from</span>
        </div>
        <!-- One header for every club, new or established (Todd 2026-09-27): no empty-state variant. -->
        <button type="button" class="btn-add" [disabled]="actionInProgress()" (click)="addNew.emit()">
          <i class="bi bi-plus-circle" aria-hidden="true"></i>Add Team
        </button>
      </header>

      <section class="panel panel--lib" aria-labelledby="board-lib-title">
        <div class="panel-body">
          <!-- Three labelled piles (Todd 2026-09-27): Registered (collapsed, TOP — says where a team
               went before anyone scrolls to look), Available Teams (open), Archived (collapsed, bottom).
               Registered and Archived hide when empty; Available always shows. -->
          @if (registeredLibRows().length > 0) {
            <button type="button" class="fold" [attr.aria-expanded]="showRegisteredLib()"
                    (click)="showRegisteredLib.set(!showRegisteredLib())">
              <i class="bi" [class.bi-chevron-down]="showRegisteredLib()" [class.bi-chevron-right]="!showRegisteredLib()" aria-hidden="true"></i>
              Registered <i class="bi bi-arrow-right" aria-hidden="true"></i>
              <span class="fold-count">{{ registeredLibRows().length }}</span>
            </button>
            @if (showRegisteredLib()) {
              <!-- READ-ONLY (Todd 2026-09-27): no icons. A second pencil beside the registered team's
                   pencil invited the wrong one, and a library edit here changes nothing on the right.
                   The library entry is editable again under Available Teams in the next event. -->
              @for (row of registeredLibRows(); track row.team.clubTeamId) {
                @let r = row.registered!;
                <div class="lib-row is-quiet">
                  <div class="row-main">
                    <div class="row-text">
                      <!-- Where it went, in words (Todd 2026-09-27): "{name} registered in {age group}". -->
                      <span class="row-name-line">
                        <span class="row-name" [attr.title]="row.team.clubTeamName">{{ row.team.clubTeamName }}</span>
                        <span class="reg-in">{{ r.isWaitlisted ? 'waitlisted in' : 'registered in' }}
                          <b>{{ r.ageGroupDisplayName || r.ageGroupName }}</b></span>
                      </span>
                      <span class="row-meta">
                        <span class="meta-pair"><span class="meta-key">Grad</span>{{ row.team.clubTeamGradYear || '—' }}</span>
                        <span class="meta-pair"><span class="meta-key">LOP</span>{{ formatLop(row.team.clubTeamLevelOfPlay) || '—' }}</span>
                      </span>
                    </div>
                  </div>
                </div>
              }
            }
          }

          <!-- Opens on every load; a collapse lasts only while the rep is on this step. -->
          <button type="button" class="fold" [attr.aria-expanded]="showAvailable()"
                  (click)="showAvailable.set(!showAvailable())">
            <i class="bi" [class.bi-chevron-down]="showAvailable()" [class.bi-chevron-right]="!showAvailable()" aria-hidden="true"></i>
            Available Teams
            <span class="fold-count">{{ availableRows().length }}</span>
          </button>
          @if (showAvailable()) {
          <!-- A brand-new library only: a placeholder ROW with the one action, never a paragraph.
               "All registered" / "all archived" get no row — the group counts already say it
               (Todd 2026-09-27). -->
          @if (empty()) {
            <div class="lib-row row-placeholder">
              <div class="row-main">
                <div class="row-text">
                  <span class="row-name">No teams yet</span>
                </div>
                <button type="button" class="btn-add" [disabled]="actionInProgress()" (click)="addNew.emit()">
                  <i class="bi bi-plus-circle" aria-hidden="true"></i>Add your first team
                </button>
              </div>
            </div>
          }

          @for (row of availableRows(); track row.team.clubTeamId) {
            @let team = row.team;
            @let isOpen = openId() === team.clubTeamId;
            <div class="lib-row" [class.is-open]="isOpen || editId() === team.clubTeamId" [class.is-pending]="isPending(team.clubTeamId)">
              @if (editId() === team.clubTeamId) {
                <ng-container *ngTemplateOutlet="libEditor; context: { $implicit: row }" />
              } @else {
              <div class="row-main">
                <div class="row-text">
                  <span class="row-name" [attr.title]="team.clubTeamName">{{ team.clubTeamName }}</span>
                  <span class="row-meta">
                    <span class="meta-pair"><span class="meta-key">Grad</span>{{ team.clubTeamGradYear || '—' }}</span>
                    <span class="meta-pair"><span class="meta-key">LOP</span>{{ formatLop(team.clubTeamLevelOfPlay) || '—' }}</span>
                  </span>
                </div>
                <!-- Library housekeeping at the right, apart from the level it isn't about. -->
                <ng-container *ngTemplateOutlet="libActions; context: { $implicit: row }" />
                @if (canRegister() && !isOpen) {
                  <button type="button" class="btn-go" [disabled]="actionInProgress()"
                          [attr.aria-label]="'Register ' + team.clubTeamName + ' for ' + eventName()"
                          (click)="open(team)">
                    Register<i class="bi bi-arrow-right" aria-hidden="true"></i>
                  </button>
                }
              </div>

              @if (isOpen) {
                @let pick = currentPick();
                <!-- The register editor: both answers visible before the press (Todd 2026-09-26). -->
                <div class="reg-editor" role="group" [attr.aria-label]="'Register ' + team.clubTeamName">
                  <label class="field">
                    <span class="field-label">Level</span>
                    <select class="field-select" [class.is-blank]="!pick.lop"
                            [attr.aria-label]="'Level of play for ' + team.clubTeamName"
                            (change)="setPick('lop', $any($event.target).value)">
                      <option value="" [selected]="!pick.lop">Pick…</option>
                      @for (c of lopChoices; track c.value) {
                        <option [value]="c.value" [selected]="c.value === pick.lop">{{ c.label }}</option>
                      }
                    </select>
                  </label>
                  <label class="field field--ag">
                    <span class="field-label">Age group</span>
                    <select class="field-select" [class.is-blank]="!pick.ag"
                            [attr.aria-label]="'Age group for ' + team.clubTeamName"
                            (change)="setPick('ag', $any($event.target).value)">
                      <option value="" [selected]="!pick.ag">Pick an age group…</option>
                      @for (o of ageGroupOptions(); track o.id) {
                        <option [value]="o.id" [selected]="o.id === pick.ag">{{ o.text }}</option>
                      }
                    </select>
                  </label>
                  <span class="reg-note">
                    @if (!pick.ag) {
                      <i class="bi bi-exclamation-circle" aria-hidden="true"></i> Pick the age group this team plays in.
                    } @else {
                      @let price = pricingOf(pick.ag);
                      @switch (price.kind) {
                        @case ('waitlist') { Full &middot; joins the waitlist. No fees while on waitlist. }
                        @case ('free') { No fee. }
                        @case ('deposit') { {{ $any(price).total | currency }} &middot; {{ $any(price).now | currency }} due now (deposit). }
                        @case ('full') { {{ $any(price).total | currency }}. }
                      }
                    }
                  </span>
                  <div class="reg-actions">
                    <button type="button" class="btn-cancel" (click)="openId.set(null)">Cancel</button>
                    @let wl = pickWaitlists(pick.ag);
                    <button type="button" class="btn-reg" [class.btn-reg--wl]="wl"
                            [disabled]="actionInProgress() || !pick.lop || !pick.ag"
                            (click)="confirm(team, pick)">
                      <i class="bi" [class.bi-trophy-fill]="!wl" [class.bi-hourglass-split]="wl" aria-hidden="true"></i>
                      {{ wl ? 'Join waitlist' : 'Register' }}
                    </button>
                  </div>
                </div>
              }
              }
            </div>
          }

          } <!-- /Available Teams -->

          @if (archivedRows().length > 0) {
            <button type="button" class="fold" [attr.aria-expanded]="showArchived()"
                    (click)="showArchived.set(!showArchived())">
              <i class="bi" [class.bi-chevron-down]="showArchived()" [class.bi-chevron-right]="!showArchived()" aria-hidden="true"></i>
              Archived
              <span class="fold-count">{{ archivedRows().length }}</span>
            </button>
            @if (showArchived()) {
              @for (row of archivedRows(); track row.team.clubTeamId) {
                <div class="lib-row is-quiet is-archived">
                  <div class="row-main">
                    <div class="row-text">
                      <span class="row-name" [attr.title]="row.team.clubTeamName">{{ row.team.clubTeamName }}</span>
                      <span class="row-meta">
                        <span class="meta-pair"><span class="meta-key">Grad</span>{{ row.team.clubTeamGradYear || '—' }}</span>
                      </span>
                    </div>
                    <ng-container *ngTemplateOutlet="libActions; context: { $implicit: row }" />
                  </div>
                </div>
              }
            }
          }
        </div>
      </section>

      <!-- ═══ RIGHT: this event's Registered Teams ═══ -->
      <!-- The event is named in the page title right above — here the side only has to say what it holds. -->
      <header class="board-head board-head--reg">
        <div class="board-head-text">
          <h3 class="board-title" id="board-reg-title" [attr.title]="'Registered for ' + eventName()">
            <i class="bi bi-trophy-fill" aria-hidden="true"></i>Registered Teams
          </h3>
          <!-- Count only (Todd 2026-09-27): no money and no phase chip up here — "Final Balance Due"
               names a later stage and read as owed-now beside "nothing due now". Due now lives on
               the Continue card; each team's state lives on its row. -->
          <span class="board-sub">
            @if (registeredRows().length === 0) { None yet for this event } @else { {{ registeredRows().length }} for this event }
          </span>
        </div>
      </header>

      <section class="panel panel--reg" aria-labelledby="board-reg-title">
        <div class="panel-body" [class.is-short]="registeredRows().length <= 3">
          @for (t of registeredRows(); track t.teamId) {
            @let s = feeStatus(t);
            @let undoMin = undoMinutesLeft(t.teamId);
            @let removable = canRemove() && t.paidTotal === 0;
            <div class="reg-row" [class.is-wl]="t.isWaitlisted" [class.is-open]="renameId() === t.teamId">
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
              <div class="row-text">
                <span class="row-name-line">
                  <span class="row-name" [attr.title]="t.teamName">{{ t.teamName }}</span>
                  <button type="button" class="btn-icon" [class.is-locked]="!!renameLockReason()"
                          [disabled]="actionInProgress()"
                          [attr.aria-disabled]="!!renameLockReason()"
                          [attr.aria-label]="'Edit the registration of ' + t.teamName"
                          [attr.title]="renameLockReason() ?? 'Edit the registration of ' + t.teamName + ' for ' + eventName() + ' (name, level of play). Your Club Team Library is not changed.'"
                          (click)="renameLockReason() ? explainLock(renameLockReason()!) : startRename(t)">
                    <i class="bi bi-pencil" aria-hidden="true"></i>
                  </button>
                </span>
                <span class="row-meta">
                  <!-- The registered age group, always (Todd 2026-09-27): a team's name is not its placement. -->
                  <span class="meta-pair">
                    <span class="meta-key">AG</span>
                    @if (t.isWaitlisted) { <i class="bi bi-hourglass-split meta-wl" aria-hidden="true"></i>Waitlist &middot; }
                    {{ t.ageGroupDisplayName || t.ageGroupName }}
                  </span>
                  <span class="meta-pair"><span class="meta-key">LOP</span>{{ formatLop(t.levelOfPlay) || '—' }}</span>
                <span class="fee" [class]="'fee fee--' + s.kind">
                  @switch (s.kind) {
                    @case ('waitlist') { <i class="bi bi-hourglass-split" aria-hidden="true"></i>No fees while on waitlist }
                    @case ('scheduled') {
                      <i class="bi bi-calendar-event" aria-hidden="true"></i>Auto-pay {{ $any(s).owed | currency }}
                      @if ($any(s).nextChargeDate) { &middot; {{ $any(s).nextChargeDate | date:'mediumDate' }} }
                    }
                    @case ('free') { <i class="bi bi-dash-circle" aria-hidden="true"></i>No fee }
                    @case ('depositDue') { <i class="bi bi-cash-stack" aria-hidden="true"></i>{{ $any(s).owed | currency }} deposit due now &middot; {{ $any(s).later | currency }} balance later }
                    @case ('depositPaid') { <i class="bi bi-check-circle-fill" aria-hidden="true"></i>Deposit paid &middot; {{ $any(s).later | currency }} balance later }
                    @case ('balanceDue') { <i class="bi bi-cash-stack" aria-hidden="true"></i>{{ $any(s).owed | currency }} {{ $any(s).depositPaid ? 'balance ' : '' }}due now }
                    @case ('paid') { <i class="bi bi-check-circle-fill" aria-hidden="true"></i>Paid in full }
                  }
                </span>
                </span>
              </div>
              @if (!removable && undoMin > 0) {
                <button type="button" class="btn-undo" [disabled]="actionInProgress()"
                        [attr.aria-label]="'Undo registering ' + t.teamName + ', ' + undoMin + ' minutes left'"
                        [attr.title]="'Undo registering ' + t.teamName + ' — ' + undoMin + (undoMin === 1 ? ' minute' : ' minutes') + ' left'"
                        (click)="remove.emit(t)">
                  <!-- No countdown on the face (Todd 2026-09-27): the minutes are in the hover text. -->
                  <i class="bi bi-arrow-counterclockwise" aria-hidden="true"></i>Undo
                </button>
              } @else if (removable) {
                <button type="button" class="btn-icon btn-icon--danger" [disabled]="actionInProgress()"
                        [attr.aria-label]="'Remove ' + t.teamName + ' from ' + eventName()"
                        [attr.title]="'Remove ' + t.teamName + ' from ' + eventName()"
                        (click)="remove.emit(t)">
                  <i class="bi bi-trash" aria-hidden="true"></i>
                </button>
              }
              }
            </div>
          } @empty {
            <div class="reg-row row-placeholder">
              <span class="row-meta">
                @if (!canRegister()) {
                  <span>Team registration for <b class="ev-name">{{ eventName() }}</b> is closed.</span>
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
        </div>
      </section>
    </div>

    <!-- A library row in edit: the fields in place of the row (Todd 2026-09-27, inline, no modal). -->
    <ng-template #libEditor let-row>
      <app-library-team-inline-editor
        [team]="row.team"
        [clubName]="clubName()"
        [existingTeams]="clubTeams()"
        [archiveLockReason]="archiveLockReasonFor(row)"
        (saved)="onLibrarySaved()"
        (cancelled)="editId.set(null)" />
    </ng-template>

    <!-- Library housekeeping on a row: Edit / Archive-or-Delete, or Restore. -->
    <ng-template #libActions let-row>
      @let team = row.team;
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
                  [attr.title]="editLock ?? 'Edit this team in your Club Team Library — what future events start from. Nothing registered is changed.'"
                  [attr.aria-disabled]="!!editLock"
                  (click)="editLock ? explainLock(editLock) : startEdit(team)">
            <i class="bi bi-pencil" aria-hidden="true"></i><span class="visually-hidden">Edit</span>
          </button>
          @if (removal.kind === 'archive') {
            <button type="button" class="btn-lib" [class.is-locked]="!!removal.lockReason"
                    [disabled]="actionInProgress()"
                    [attr.title]="removal.lockReason ?? 'Archive: hide it from your Club Team Library, keep its history'"
                    [attr.aria-disabled]="!!removal.lockReason"
                    (click)="removal.lockReason ? explainLock(removal.lockReason) : archive.emit(team)">
              <i class="bi bi-box-arrow-in-down" aria-hidden="true"></i><span class="visually-hidden">Archive</span>
            </button>
          } @else {
            <button type="button" class="btn-lib btn-lib--danger" [class.is-locked]="!!removal.lockReason"
                    [disabled]="actionInProgress()"
                    [attr.title]="removal.lockReason ?? 'Delete from your Club Team Library'"
                    [attr.aria-disabled]="!!removal.lockReason"
                    (click)="removal.lockReason ? explainLock(removal.lockReason) : delete.emit(team)">
              <i class="bi bi-trash" aria-hidden="true"></i><span class="visually-hidden">Delete</span>
            </button>
          }
        }
      </span>
    </ng-template>
    `,
    styles: [`
      /* Headers in one row, panels in the next: the headers always match height, so the two
         panels start level whatever each header's text runs to. */
      .board {
        display: grid;
        grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
        grid-template-areas:
          "lib-head reg-head"
          "lib-panel reg-panel";
        column-gap: var(--space-3);
        row-gap: var(--space-2);
        align-items: start;
      }
      .board-head--lib { grid-area: lib-head; }
      .board-head--reg { grid-area: reg-head; }
      .panel--lib { grid-area: lib-panel; }
      .panel--reg { grid-area: reg-panel; }

      .panel {
        display: flex;
        flex-direction: column;
        align-self: stretch; /* both panels end on one line above Continue */
        min-width: 0;
        border: 1px solid var(--bs-border-color);
        border-radius: var(--radius-md);
        background: var(--brand-surface);
        box-shadow: var(--shadow-xs);
        overflow: hidden;
      }
      /* Each side keeps one color everywhere: library = primary, this event = success. */
      /* A faint wash of each side's color over the surface: mixed from theme variables, so it
         follows every palette and dark mode, and the two sides read apart at a glance. */
      .panel--lib { border-top: 3px solid var(--bs-primary); background: color-mix(in srgb, var(--bs-primary) 4%, var(--brand-surface)); }
      .panel--reg { border-top: 3px solid var(--bs-success); background: color-mix(in srgb, var(--bs-success) 5%, var(--brand-surface)); }

      /* ── Header above each panel ── */
      .board-head {
        display: flex;
        align-items: flex-end;
        gap: var(--space-2);
        align-self: stretch;
        min-width: 0;
        padding: 0 var(--space-1);
      }

      .board-head-text { flex: 1; display: flex; flex-direction: column; gap: 2px; min-width: 0; }

      .board-title {
        display: flex;
        align-items: baseline;
        gap: var(--space-2);
        margin: 0;
        font-size: var(--font-size-base);
        font-weight: var(--font-weight-bold);
        color: var(--brand-text);
        line-height: var(--line-height-tight);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
      }
      .board-head--lib .board-title .bi { color: var(--bs-primary); }
      .board-head--reg .board-title .bi { color: var(--bs-success); }

      .board-sub {
        font-size: var(--font-size-2xs);
        color: var(--brand-text-muted);
        font-variant-numeric: tabular-nums;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
      }

      /* Each list scrolls on its own under a fixed header, so both sides stay in view together and
         Continue stays in reach for a big club (Todd 2026-09-27). Uncapped once stacked on a phone. */
      .panel-body {
        display: flex;
        flex-direction: column;
        flex: 1 1 auto;
        max-height: 60vh;
        overflow-y: auto;
        overscroll-behavior: contain;

        /* A slim scrollbar in the side's own color, not the OS's 17px gutter. */
        scrollbar-width: thin;
        scrollbar-color: color-mix(in srgb, var(--bs-body-color) 22%, transparent) transparent;

        &::-webkit-scrollbar { width: 8px; }
        &::-webkit-scrollbar-track { background: transparent; }
        &::-webkit-scrollbar-thumb {
          border: 2px solid transparent;
          border-radius: var(--radius-full);
          background: color-mix(in srgb, var(--bs-body-color) 22%, transparent) padding-box;
        }
        &::-webkit-scrollbar-thumb:hover { background: color-mix(in srgb, var(--bs-body-color) 38%, transparent) padding-box; }
      }

      /* A row-shaped stand-in where teams will go: same padding as a team row, dashed edge. */
      .lib-row.row-placeholder, .reg-row.row-placeholder {
        margin: var(--space-2);
        border: 1px dashed color-mix(in srgb, var(--bs-body-color) 25%, transparent);
        border-radius: var(--radius-sm);

        &:last-child { border-bottom: 1px dashed color-mix(in srgb, var(--bs-body-color) 25%, transparent); }
        .row-name { color: var(--brand-text-muted); }
      }

      /* ── Rows ── */
      .lib-row, .reg-row {
        padding: var(--space-2) var(--space-3);
        border-bottom: 1px solid color-mix(in srgb, var(--bs-body-color) 6%, transparent);

        &:last-child { border-bottom: none; }
      }

      .lib-row {
        &.is-open { background: color-mix(in srgb, var(--bs-primary) 10%, transparent); }
        &.is-pending { box-shadow: inset 3px 0 0 var(--bs-warning); }
        &.is-quiet .row-name { font-weight: var(--font-weight-medium); }
        &.is-archived .row-name { font-style: italic; color: var(--brand-text-muted); }
      }

      .row-main { display: flex; align-items: center; gap: var(--space-2); }

      .reg-row {
        display: flex;
        align-items: flex-start;
        gap: var(--space-2);

        &.is-wl { background: color-mix(in srgb, var(--bs-warning) 6%, transparent); }
        &.is-open { background: color-mix(in srgb, var(--bs-success) 10%, transparent); }
      }

      .reg-editor-host { flex: 1; min-width: 0; }

      .row-text { flex: 1; display: flex; flex-direction: column; gap: 1px; min-width: 0; }

      .row-name-line { display: flex; align-items: center; gap: var(--space-1); min-width: 0; }
      /* The name gives way first; the age group never truncates. */
      .reg-in {
        flex-shrink: 0;
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
        white-space: nowrap;

        b { color: var(--brand-text); font-weight: var(--font-weight-semibold); }
      }

      .row-name {
        min-width: 0;
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
      }

      .row-meta {
        display: inline-flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--space-3);
        font-size: var(--font-size-2xs);
        color: var(--brand-text-muted);
        font-variant-numeric: tabular-nums;
      }

      .meta-pair { display: inline-flex; align-items: baseline; gap: var(--space-1); }
      .meta-key { text-transform: uppercase; letter-spacing: 0.06em; font-weight: var(--font-weight-semibold); opacity: 0.7; }
      .meta-wl { color: var(--bs-warning); }

      .fee {
        display: inline-flex;
        align-items: baseline;
        gap: var(--space-1);
        font-size: var(--font-size-xs);
        color: var(--brand-text);
        font-variant-numeric: tabular-nums;

        .bi { color: var(--brand-text-muted); }
      }
      .fee--depositDue .bi, .fee--balanceDue .bi { color: var(--bs-warning); }
      .fee--paid .bi, .fee--depositPaid .bi { color: var(--bs-success); }
      .fee--scheduled .bi { color: var(--bs-info); }
      .fee--free .bi { color: var(--brand-text-muted); }

      /* The event, accented in the empty Registered panel — this side's color, darkened toward the
         text color so it holds AA on the tinted panel in every palette. */
      .ev-name { color: color-mix(in srgb, var(--bs-success) 65%, var(--brand-text)); font-weight: var(--font-weight-bold); }

      /* ── Buttons ── */
      .btn-add {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
        flex-shrink: 0;
        padding: 3px var(--space-2);
        border: 1px solid var(--bs-primary);
        border-radius: var(--radius-sm);
        background: var(--brand-surface);
        color: var(--bs-primary);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        white-space: nowrap;
        cursor: pointer;

        &:hover:not(:disabled) { background: color-mix(in srgb, var(--bs-primary) 8%, var(--brand-surface)); }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        &:disabled { opacity: 0.45; cursor: default; }
      }

      .btn-go {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
        flex-shrink: 0;
        padding: 3px var(--space-2);
        /* Quiet at rest so a column of them doesn't outshout the team names; solid on hover/focus. */
        border: 1px solid color-mix(in srgb, var(--bs-success) 35%, transparent);
        border-radius: var(--radius-sm);
        background: transparent;
        color: var(--bs-success);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        white-space: nowrap;
        cursor: pointer;
        transition: background-color 0.12s ease, color 0.12s ease, border-color 0.12s ease;

        &:hover:not(:disabled), &:focus-visible {
          border-color: var(--bs-success);
          background: var(--bs-success);
          color: var(--neutral-0);
        }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        &:disabled { opacity: 0.45; cursor: default; }
      }

      /* Library housekeeping, right of the team and left of Register. */
      .lib-actions { display: inline-flex; flex-shrink: 0; gap: 2px; }

      .btn-lib {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        gap: 3px;
        min-width: 28px;
        height: 28px;
        padding: 0 var(--space-1);
        border: 1px solid transparent;
        border-radius: var(--radius-sm);
        background: transparent;
        color: var(--brand-text-muted);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-medium);
        cursor: pointer;

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
        &.is-locked { opacity: 0.5; cursor: help; font-style: italic; text-decoration: line-through; }
      }

      .btn-icon {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        flex-shrink: 0;
        width: 26px;
        height: 26px;
        padding: 0;
        border: 1px solid transparent;
        border-radius: var(--radius-sm);
        background: transparent;
        color: var(--brand-text-muted);
        font-size: var(--font-size-xs);
        cursor: pointer;

        &:hover:not(:disabled):not(.is-locked) { background: color-mix(in srgb, var(--bs-success) 10%, transparent); color: var(--bs-success); }
        &--danger:hover:not(:disabled) { background: color-mix(in srgb, var(--bs-danger) 10%, transparent); color: var(--bs-danger); }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        &:disabled { opacity: 0.3; cursor: default; }
        &.is-locked { opacity: 0.4; cursor: help; }
      }

      .btn-undo {
        display: inline-flex;
        align-items: center;
        gap: 3px;
        flex-shrink: 0;
        padding: 2px var(--space-2);
        border: 1px solid var(--bs-border-color);
        border-radius: var(--radius-sm);
        background: var(--brand-surface);
        color: var(--brand-text);
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-semibold);
        font-variant-numeric: tabular-nums;
        white-space: nowrap;
        cursor: pointer;

        &:hover:not(:disabled) { border-color: var(--bs-danger); color: var(--bs-danger); }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        &:disabled { opacity: 0.45; cursor: default; }
      }

      /* ── The register editor on a library row ── */
      .reg-editor {
        display: grid;
        grid-template-columns: 88px minmax(0, 1fr);
        grid-template-areas:
          "lop ag"
          "note note"
          "actions actions";
        gap: var(--space-2);
        margin-top: var(--space-2);
      }
      .reg-editor .field:first-child { grid-area: lop; }
      .field--ag { grid-area: ag; }
      .reg-note { grid-area: note; }
      .reg-actions { grid-area: actions; display: flex; justify-content: flex-end; gap: var(--space-2); }

      .field { display: flex; flex-direction: column; gap: 1px; min-width: 0; margin: 0; }

      .field-label {
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-semibold);
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--brand-text-muted);
      }

      .field-select {
        width: 100%;
        min-width: 0;
        padding: 3px var(--space-1);
        border: 1px solid var(--bs-border-color);
        border-radius: var(--radius-sm);
        background: var(--brand-surface);
        color: var(--brand-text);
        font-size: var(--font-size-xs);
        cursor: pointer;

        &:focus-visible { outline: none; border-color: var(--bs-primary); box-shadow: var(--shadow-focus); }
        &.is-blank { border-style: dashed; border-color: var(--bs-warning); color: var(--brand-text-muted); }
      }

      .reg-note {
        font-size: var(--font-size-2xs);
        color: var(--brand-text-muted);
        font-variant-numeric: tabular-nums;

        .bi { color: var(--bs-warning); }
      }

      .btn-cancel {
        padding: 3px var(--space-2);
        border: 1px solid var(--bs-border-color);
        border-radius: var(--radius-sm);
        background: var(--brand-surface);
        color: var(--brand-text);
        font-size: var(--font-size-xs);
        cursor: pointer;

        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
      }

      .btn-reg {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
        padding: 3px var(--space-3);
        border: 1px solid var(--bs-success);
        border-radius: var(--radius-sm);
        background: var(--bs-success);
        color: var(--neutral-0);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        white-space: nowrap;
        cursor: pointer;

        &:hover:not(:disabled) { filter: brightness(0.93); }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        &:disabled { opacity: 0.4; cursor: default; }

        &--wl { border-color: var(--bs-warning); background: var(--bs-warning); color: var(--bs-dark); }
      }

      /* ── Folds ── */
      .fold {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        width: 100%;
        padding: var(--space-2) var(--space-3);
        border: none;
        border-top: 1px solid var(--bs-border-color);
        background: color-mix(in srgb, var(--bs-body-color) 3%, transparent);
        color: var(--brand-text-muted);
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-bold);
        text-transform: uppercase;
        letter-spacing: 0.06em;
        text-align: left;
        cursor: pointer;

        &:first-child { border-top: none; }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
      }

      .fold-count {
        padding: 1px var(--space-2);
        border-radius: var(--radius-full);
        background: color-mix(in srgb, var(--bs-body-color) 8%, transparent);
        color: var(--brand-text);
      }

      /* Narrow: stack, library on top — the order the work goes in. */
      @media (max-width: 767.98px) {
        .board {
          grid-template-columns: minmax(0, 1fr);
          grid-template-areas: "lib-head" "lib-panel" "reg-head" "reg-panel";
        }
        .board-head--reg { margin-top: var(--space-3); }
        .panel { align-self: start; }
        /* Capped short on a phone so Registered Teams is one flick away, not 20 rows down
           (Todd 2026-09-27). Scroll CHAINS at the ends: a swipe past a list's end moves the page,
           so the thumb is never trapped in a box. */
        .panel-body {
          max-height: 45vh;
          max-height: 45svh;
          overscroll-behavior: auto;
        }
        .panel-body.is-short { max-height: none; }
        /* The library shorter still: Registered Teams' header lands on the first screen. */
        .panel--lib .panel-body {
          max-height: 32vh;
          max-height: 32svh;
        }
      }

      @media (prefers-reduced-motion: reduce) {
        .btn-go { transition: none !important; }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamsBoardComponent {
    private readonly toast = inject(ToastService);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
    private readonly injector = inject(Injector);

    readonly clubTeams = input.required<readonly ClubTeamDto[]>();
    readonly registeredTeams = input<readonly RegisteredTeamDto[]>([]);
    readonly ageGroups = input<readonly AgeGroupDto[]>([]);
    readonly eventName = input('this event');
    readonly clubName = input('');
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
    readonly lopChoices = LOP_CHOICES;

    readonly showRegisteredLib = signal(false);
    readonly showAvailable = signal(true);
    readonly showArchived = signal(false);
    /** The library row whose register editor is open — one at a time. */
    readonly openId = signal<number | null>(null);
    /** The library row being edited inline. One open row at a time: register and edit close each other. */
    readonly editId = signal<number | null>(null);
    /** The registered row being edited inline (teamId). */
    readonly renameId = signal<string | null>(null);
    private readonly pick = signal<RegPick>({ lop: '', ag: '' });
    readonly currentPick = this.pick.asReadonly();

    private readonly registeredByClubTeam = computed(() => {
        const map = new Map<number, RegisteredTeamDto>();
        for (const r of this.registeredTeams()) if (r.clubTeamId != null) map.set(r.clubTeamId, r);
        return map;
    });

    private readonly rows = computed<LibRow[]>(() => {
        const reg = this.registeredByClubTeam();
        return [...this.clubTeams()]
            .sort(byGradYearThenName)
            .map(team => ({ team, registered: reg.get(team.clubTeamId) ?? null }));
    });

    readonly availableRows = computed(() => this.rows().filter(r => !r.team.bArchived && !r.registered));
    readonly registeredLibRows = computed(() => this.rows().filter(r => !!r.registered));
    readonly archivedRows = computed(() => this.rows().filter(r => r.team.bArchived && !r.registered));
    readonly activeCount = computed(() => this.clubTeams().filter(t => !t.bArchived).length);
    readonly empty = computed(() => this.clubTeams().length === 0);

    /** This event's teams, by age group then name — the shape a director reads them in. */
    readonly registeredRows = computed(() => [...this.registeredTeams()].sort((a, b) =>
        (a.ageGroupName ?? '').localeCompare(b.ageGroupName ?? '') || a.teamName.localeCompare(b.teamName)));

    readonly ageGroupOptions = computed(() => this.ageGroups().map(ag => {
        const label = ageGroupLabel(ag);
        const price = pricingOfAgeGroup(ag);
        const money = (n: number) => `$${n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
        const text = ageGroupWaitlists(ag)
            ? `${label} waitlist · no fees while on waitlist`
            : price.kind === 'free' ? `${label} · no fee`
            // Two figures ONLY in the deposit stage of a deposit + balance group (Todd 2026-09-26).
            : price.kind === 'deposit' ? `${label} · ${money(price.total)} · ${money(price.now)} due now`
            : price.kind === 'full' ? `${label} · ${money(price.total)}`
            : label;
        return { id: ag.ageGroupId, text };
    }));

    feeStatus(t: RegisteredTeamDto): BoardFeeStatus { return boardFeeStatusOf(t); }

    isPending(clubTeamId: number): boolean { return this.pendingLibraryOnly().has(clubTeamId); }

    undoMinutesLeft(teamId: string): number {
        const deadline = this.undoDeadlines().get(teamId);
        if (deadline === undefined) return 0;
        const ms = deadline - this.now();
        return ms > 0 ? Math.ceil(ms / 60000) : 0;
    }

    // ── Locks: the shared rules, so the board and the library page never disagree ──
    editLockReason(team: ClubTeamDto): string | null {
        return clubTeamEditLockReason(team, { registeredHere: false, eventLabel: this.eventName() });
    }
    removalFor(row: LibRow): ClubTeamRemoval {
        return clubTeamRemoval(row.team, { registeredHere: !!row.registered, eventLabel: this.eventName() });
    }
    explainLock(reason: string): void { this.toast.show(reason, 'warning', 3500); }

    // ── Inline library edit ──
    startEdit(team: ClubTeamDto): void {
        this.openId.set(null);
        this.renameId.set(null);
        this.editId.set(team.clubTeamId);
    }

    // ── Inline registered-team edit (this event's name + level) ──
    startRename(t: RegisteredTeamDto): void {
        this.openId.set(null);
        this.editId.set(null);
        this.renameId.set(t.teamId);
    }

    onRenameSaved(message: string): void {
        this.renameId.set(null);
        this.renameSaved.emit(message);
    }

    onLibrarySaved(): void {
        this.editId.set(null);
        this.librarySaved.emit();
    }

    /** For the editor's add-as-new follow-up: may the OLD row be archived? */
    archiveLockReasonFor(row: LibRow): string | null {
        return clubTeamArchiveLockReason({ registeredHere: !!row.registered, eventLabel: 'this event' });
    }

    // ── Register editor ──
    /** Opens seeded with the best guess: the library's level, and the age group the grad year names. */
    open(team: ClubTeamDto): void {
        this.pick.set({
            lop: normalizeLop(team.clubTeamLevelOfPlay),
            ag: resolveRecommendedAgeGroupId(this.ageGroups(), team.clubTeamGradYear),
        });
        this.editId.set(null);
        this.renameId.set(null);
        this.openId.set(team.clubTeamId);
        // A row near the bottom of the scrolled list opens its editor out of sight — bring it in.
        afterNextRender(() => {
            const row = this.host.nativeElement.querySelector<HTMLElement>('.lib-row.is-open');
            const reduce = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
            row?.scrollIntoView({ block: 'nearest', behavior: reduce ? 'auto' : 'smooth' });
        }, { injector: this.injector });
    }

    setPick(field: keyof RegPick, value: string): void {
        this.pick.set({ ...this.pick(), [field]: value });
    }

    pickWaitlists(ageGroupId: string): boolean {
        const ag = this.ageGroups().find(a => a.ageGroupId === ageGroupId);
        return !!ag && ageGroupWaitlists(ag);
    }

    pricingOf(ageGroupId: string): SlotPricing {
        const ag = this.ageGroups().find(a => a.ageGroupId === ageGroupId);
        if (!ag) return { kind: 'free' };
        return ageGroupWaitlists(ag) ? { kind: 'waitlist' } : pricingOfAgeGroup(ag);
    }

    confirm(team: ClubTeamDto, pick: RegPick): void {
        if (this.actionInProgress() || !pick.lop || !pick.ag) return;
        this.openId.set(null);
        this.register.emit({ team, ageGroupId: pick.ag, levelOfPlay: pick.lop });
    }
}
