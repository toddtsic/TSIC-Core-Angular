import { ChangeDetectionStrategy, Component, ElementRef, Injector, afterNextRender, computed, inject, input, output, signal } from '@angular/core';
import { CurrencyPipe, DatePipe, NgTemplateOutlet } from '@angular/common';
import type { AgeGroupDto, ClubTeamDto, RegisteredTeamDto } from '@core/api';
import { LOP_CHOICES, formatLop, normalizeLop } from '@shared/teams/lop-choices';
import { clubTeamEditLockReason, clubTeamRemoval, type ClubTeamRemoval } from '@shared/teams/club-team-locks';
import { ToastService } from '@shared-ui/toast.service';
import { resolveRecommendedAgeGroupId, type SlotPricing } from './event-age-group.util';
import { ageGroupLabel, type LibraryRegisterRequest } from './library-segment.types';
import { ageGroupWaitlists, byGradYearThenName, pricingOfAgeGroup } from './library-register-plan';
import { sumDueNowOf, teamFeeStatusOf, type TeamFeeStatus } from './registered-teams-grid.component';

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
    imports: [CurrencyPipe, DatePipe, NgTemplateOutlet],
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
          <span class="board-sub">Edits here change future events</span>
        </div>
        <button type="button" class="btn-add" [class.btn-add--primary]="empty()"
                [disabled]="actionInProgress()" (click)="addNew.emit()">
          <i class="bi bi-plus-circle" aria-hidden="true"></i>{{ empty() ? 'Add Your First Team' : 'Add Team' }}
        </button>
      </header>

      <section class="panel panel--lib" aria-labelledby="board-lib-title">
        <div class="panel-body">
          @if (empty()) {
            <p class="panel-empty">
              Add every team you're bringing to {{ eventName() }} &mdash; once, for every event after.
            </p>
          } @else if (availableRows().length === 0 && activeCount() > 0) {
            <p class="panel-empty panel-empty--done">
              <i class="bi bi-check-circle-fill" aria-hidden="true"></i>
              Every library team is registered for {{ eventName() }}.
            </p>
          } @else if (activeCount() === 0) {
            <p class="panel-empty">Every library team is archived. Restore one below, or add a team.</p>
          }

          @for (row of availableRows(); track row.team.clubTeamId) {
            @let team = row.team;
            @let isOpen = openId() === team.clubTeamId;
            <div class="lib-row" [class.is-open]="isOpen" [class.is-pending]="isPending(team.clubTeamId)">
              <div class="row-main">
                <div class="row-text">
                  <span class="row-name" [attr.title]="team.clubTeamName">{{ team.clubTeamName }}</span>
                  <span class="row-meta">
                    <span class="meta-pair"><span class="meta-key">Grad</span>{{ team.clubTeamGradYear || '—' }}</span>
                    <span class="meta-pair"><span class="meta-key">LOP</span>{{ formatLop(team.clubTeamLevelOfPlay) || '—' }}</span>
                    <ng-container *ngTemplateOutlet="libActions; context: { $implicit: row }" />
                  </span>
                </div>
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
                        @case ('waitlist') { Full &middot; joins the waitlist, no fee until placed. }
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
            </div>
          }

          <!-- Library teams already registered here: folded, library Edit only. The registration
               itself is on the right. -->
          @if (registeredLibRows().length > 0) {
            <button type="button" class="fold" [attr.aria-expanded]="showRegisteredLib()"
                    (click)="showRegisteredLib.set(!showRegisteredLib())">
              <i class="bi" [class.bi-chevron-down]="showRegisteredLib()" [class.bi-chevron-right]="!showRegisteredLib()" aria-hidden="true"></i>
              Registered <i class="bi bi-arrow-right" aria-hidden="true"></i>
              <span class="fold-count">{{ registeredLibRows().length }}</span>
            </button>
            @if (showRegisteredLib()) {
              @for (row of registeredLibRows(); track row.team.clubTeamId) {
                <div class="lib-row is-quiet">
                  <div class="row-text">
                    <span class="row-name" [attr.title]="row.team.clubTeamName">{{ row.team.clubTeamName }}</span>
                    <span class="row-meta">
                      <span class="meta-pair"><span class="meta-key">Grad</span>{{ row.team.clubTeamGradYear || '—' }}</span>
                      <span class="meta-pair"><span class="meta-key">LOP</span>{{ formatLop(row.team.clubTeamLevelOfPlay) || '—' }}</span>
                      <ng-container *ngTemplateOutlet="libActions; context: { $implicit: row }" />
                    </span>
                  </div>
                </div>
              }
            }
          }

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
                  <div class="row-text">
                    <span class="row-name" [attr.title]="row.team.clubTeamName">{{ row.team.clubTeamName }}</span>
                    <span class="row-meta">
                      <span class="meta-pair"><span class="meta-key">Grad</span>{{ row.team.clubTeamGradYear || '—' }}</span>
                      <ng-container *ngTemplateOutlet="libActions; context: { $implicit: row }" />
                    </span>
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
          <span class="board-sub">
            @if (registeredRows().length === 0) {
              None yet for this event
            } @else {
              {{ registeredRows().length }} for this event &middot;
              @if (dueNow() > 0) { {{ dueNow() | currency }} due now } @else { nothing due now }
            }
          </span>
        </div>
        @if (phaseLabel()) {
          <span class="phase-chip" title="Payment phase">{{ phaseLabel() }}</span>
        }
      </header>

      <section class="panel panel--reg" aria-labelledby="board-reg-title">
        <div class="panel-body">
          @for (t of registeredRows(); track t.teamId) {
            @let s = feeStatus(t);
            @let undoMin = undoMinutesLeft(t.teamId);
            @let removable = canRemove() && t.paidTotal === 0;
            <div class="reg-row" [class.is-wl]="t.isWaitlisted">
              <div class="row-text">
                <span class="row-name-line">
                  <span class="row-name" [attr.title]="t.teamName">{{ t.teamName }}</span>
                  <button type="button" class="btn-icon" [class.is-locked]="!!renameLockReason()"
                          [disabled]="actionInProgress()"
                          [attr.aria-disabled]="!!renameLockReason()"
                          [attr.aria-label]="'Edit the registration of ' + t.teamName"
                          [attr.title]="renameLockReason() ?? 'Edit the registration of ' + t.teamName + ' for ' + eventName() + ' (name, level of play). Your Club Team Library is not changed.'"
                          (click)="renameLockReason() ? explainLock(renameLockReason()!) : rename.emit(t)">
                    <i class="bi bi-pencil" aria-hidden="true"></i>
                  </button>
                </span>
                <span class="row-meta">
                  <!-- The age group only when the name doesn't already say it ("2030 Blue" in 2030). -->
                  @if (showAgeGroup(t)) {
                    <span class="meta-pair">
                      @if (t.isWaitlisted) { <i class="bi bi-hourglass-split meta-wl" aria-hidden="true"></i>Waitlist &middot; }
                      {{ t.ageGroupDisplayName || t.ageGroupName }}
                    </span>
                  }
                  <span class="meta-pair"><span class="meta-key">LOP</span>{{ formatLop(t.levelOfPlay) || '—' }}</span>
                <span class="fee" [class]="'fee fee--' + s.kind">
                  @switch (s.kind) {
                    @case ('waitlist') { <i class="bi bi-hourglass-split" aria-hidden="true"></i>No fee until placed }
                    @case ('scheduled') {
                      <i class="bi bi-calendar-event" aria-hidden="true"></i>Auto-pay {{ $any(s).owed | currency }}
                      @if ($any(s).nextChargeDate) { &middot; {{ $any(s).nextChargeDate | date:'mediumDate' }} }
                    }
                    @case ('depositDue') { <i class="bi bi-cash-stack" aria-hidden="true"></i>Deposit {{ $any(s).owed | currency }} due now &middot; {{ $any(s).later | currency }} later }
                    @case ('depositPaid') { <i class="bi bi-check-circle-fill" aria-hidden="true"></i>Deposit paid &middot; {{ $any(s).later | currency }} later }
                    @case ('balanceDue') { <i class="bi bi-cash-stack" aria-hidden="true"></i>{{ $any(s).owed | currency }} {{ $any(s).depositPaid ? 'balance ' : '' }}due now }
                    @case ('paid') { <i class="bi bi-check-circle-fill" aria-hidden="true"></i>Paid in full }
                  }
                </span>
                </span>
              </div>
              @if (!removable && undoMin > 0) {
                <button type="button" class="btn-undo" [disabled]="actionInProgress()"
                        [attr.aria-label]="'Undo registering ' + t.teamName + ', ' + undoMin + ' minutes left'"
                        (click)="remove.emit(t)">
                  <i class="bi bi-arrow-counterclockwise" aria-hidden="true"></i>Undo &middot; {{ undoMin }}m
                </button>
              } @else if (removable) {
                <button type="button" class="btn-icon btn-icon--danger" [disabled]="actionInProgress()"
                        [attr.aria-label]="'Remove ' + t.teamName + ' from ' + eventName()"
                        [attr.title]="'Remove ' + t.teamName + ' from ' + eventName()"
                        (click)="remove.emit(t)">
                  <i class="bi bi-trash" aria-hidden="true"></i>
                </button>
              }
            </div>
          } @empty {
            <p class="panel-empty">
              @if (!canRegister()) {
                Team registration for {{ eventName() }} is closed.
              } @else if (empty()) {
                Once your library has a team, press <b>Register</b> on it to bring it here.
              } @else {
                Press <b>Register</b> on a library team to bring it here.
              }
            </p>
          }
        </div>

        <!-- Outside the scrolling list: always in view. -->
        @if (registeredRows().length > 0) {
          <p class="panel-foot">
            <i class="bi bi-info-circle" aria-hidden="true"></i>
            <span>Amounts are the fee itself &mdash; any processing fee is added at <b>Continue to Payment</b>.</span>
          </p>
        }
      </section>
    </div>

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
                  (click)="editLock ? explainLock(editLock) : edit.emit(team)">
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
        min-width: 0;
        border: 1px solid var(--bs-border-color);
        border-radius: var(--radius-md);
        background: var(--brand-surface);
        box-shadow: var(--shadow-xs);
        overflow: hidden;
      }
      /* Each side keeps one color everywhere: library = primary, this event = success. */
      .panel--lib { border-top: 3px solid var(--bs-primary); }
      .panel--reg { border-top: 3px solid var(--bs-success); }

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

      .phase-chip {
        flex-shrink: 0;
        padding: 1px var(--space-2);
        border-radius: var(--radius-full);
        background: color-mix(in srgb, var(--bs-success) 12%, transparent);
        color: var(--brand-text);
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-semibold);
        white-space: nowrap;
      }

      /* Each list scrolls on its own under a fixed header, so both sides stay in view together and
         Continue stays in reach for a big club (Todd 2026-09-27). Uncapped once stacked on a phone. */
      .panel-body {
        display: flex;
        flex-direction: column;
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

      .panel-empty {
        margin: 0;
        padding: var(--space-5) var(--space-3);
        text-align: center;
        font-size: var(--font-size-sm);
        color: var(--brand-text-muted);

        &--done .bi { color: var(--bs-success); }
      }

      .panel-foot {
        display: flex;
        align-items: baseline;
        gap: var(--space-1);
        margin: 0;
        padding: var(--space-2) var(--space-3);
        border-top: 1px solid var(--bs-border-color);
        font-size: var(--font-size-2xs);
        color: var(--brand-text-muted);
      }

      /* ── Rows ── */
      .lib-row, .reg-row {
        padding: var(--space-2) var(--space-3);
        border-bottom: 1px solid color-mix(in srgb, var(--bs-body-color) 6%, transparent);

        &:last-child { border-bottom: none; }
      }

      .lib-row {
        &.is-open { background: color-mix(in srgb, var(--bs-primary) 5%, transparent); }
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
      }

      .row-text { flex: 1; display: flex; flex-direction: column; gap: 1px; min-width: 0; }

      .row-name-line { display: flex; align-items: center; gap: var(--space-1); min-width: 0; }

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

        &--primary {
          background: var(--bs-primary);
          color: var(--neutral-0);
          &:hover:not(:disabled) { background: var(--bs-primary); filter: brightness(0.93); }
        }
      }

      .btn-go {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
        flex-shrink: 0;
        padding: 3px var(--space-2);
        border: 1px solid var(--bs-success);
        border-radius: var(--radius-sm);
        background: var(--brand-surface);
        color: var(--bs-success);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        white-space: nowrap;
        cursor: pointer;
        transition: background-color 0.12s ease;

        &:hover:not(:disabled) { background: color-mix(in srgb, var(--bs-success) 10%, var(--brand-surface)); }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        &:disabled { opacity: 0.45; cursor: default; }
      }

      /* Rides the meta line (name, then one line): no third line per row. */
      .lib-actions { display: inline-flex; flex-wrap: wrap; gap: 2px; margin-left: calc(-1 * var(--space-2)); }

      .btn-lib {
        display: inline-flex;
        align-items: center;
        gap: 3px;
        padding: 1px var(--space-1);
        border: 1px solid transparent;
        border-radius: var(--radius-sm);
        background: transparent;
        color: var(--brand-text-muted);
        font-size: var(--font-size-xs);
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
        .panel-body { max-height: none; overflow-y: visible; }
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
    readonly phaseLabel = input<string | null>(null);

    readonly register = output<LibraryRegisterRequest>();
    readonly remove = output<RegisteredTeamDto>();
    readonly rename = output<RegisteredTeamDto>();
    readonly addNew = output<void>();
    readonly edit = output<ClubTeamDto>();
    readonly archive = output<ClubTeamDto>();
    readonly delete = output<ClubTeamDto>();
    readonly restore = output<ClubTeamDto>();

    readonly formatLop = formatLop;
    readonly lopChoices = LOP_CHOICES;

    readonly showRegisteredLib = signal(false);
    readonly showArchived = signal(false);
    /** The library row whose register editor is open — one at a time. */
    readonly openId = signal<number | null>(null);
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

    readonly dueNow = computed(() => sumDueNowOf(this.registeredTeams()));

    readonly ageGroupOptions = computed(() => this.ageGroups().map(ag => {
        const label = ageGroupLabel(ag);
        const price = pricingOfAgeGroup(ag);
        const money = (n: number) => `$${n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
        const text = ageGroupWaitlists(ag)
            ? `${label} waitlist · no fee until placed`
            : price.kind === 'free' ? `${label} · no fee`
            // Two figures ONLY in the deposit stage of a deposit + balance group (Todd 2026-09-26).
            : price.kind === 'deposit' ? `${label} · ${money(price.total)} · ${money(price.now)} due now`
            : price.kind === 'full' ? `${label} · ${money(price.total)}`
            : label;
        return { id: ag.ageGroupId, text };
    }));

    feeStatus(t: RegisteredTeamDto): TeamFeeStatus { return teamFeeStatusOf(t); }

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

    // ── Register editor ──
    /** Opens seeded with the best guess: the library's level, and the age group the grad year names. */
    open(team: ClubTeamDto): void {
        this.pick.set({
            lop: normalizeLop(team.clubTeamLevelOfPlay),
            ag: resolveRecommendedAgeGroupId(this.ageGroups(), team.clubTeamGradYear),
        });
        this.openId.set(team.clubTeamId);
        // A row near the bottom of the scrolled list opens its editor out of sight — bring it in.
        afterNextRender(() => {
            const row = this.host.nativeElement.querySelector<HTMLElement>('.lib-row.is-open');
            const reduce = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
            row?.scrollIntoView({ block: 'nearest', behavior: reduce ? 'auto' : 'smooth' });
        }, { injector: this.injector });
    }

    /** The age group, unless the team's name already says it ("2030 Blue" in 2030). A waitlist always shows. */
    showAgeGroup(t: RegisteredTeamDto): boolean {
        if (t.isWaitlisted) return true;
        const label = (t.ageGroupDisplayName || t.ageGroupName || '').trim();
        return !label || !t.teamName.toLowerCase().includes(label.toLowerCase());
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
