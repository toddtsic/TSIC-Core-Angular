import { AfterViewChecked, ChangeDetectionStrategy, Component, ElementRef, OnChanges, SimpleChanges, computed, inject, input, output, signal } from '@angular/core';
import { CurrencyPipe, NgTemplateOutlet } from '@angular/common';
import type { AgeGroupDto, ClubTeamDto, RegisteredTeamDto } from '@core/api';
import { formatLop, normalizeLop } from '@shared/teams/lop-choices';
import { clubTeamEditLockReason, clubTeamRemoval, type ClubTeamLockContext, type ClubTeamRemoval } from '@shared/teams/club-team-locks';
import { ToastService } from '@shared-ui/toast.service';
import { LevelOfPlayPickerComponent } from '@shared/teams/level-of-play-picker.component';
import { EventAgeGroupPickerComponent } from './event-age-group-picker.component';
import { RegisterAllDialogComponent } from './register-all-dialog.component';
import { describeSlotPricing, isTeamOfferedAtEvent, resolveOldestOfferedGradYear, resolveRecommendedAgeGroupId, type SlotPricing } from './event-age-group.util';
import { ageGroupLabel, isWaitlistAgeGroup, type LibraryRegisterRequest } from './library-segment.types';

/**
 * What pressing this row's button does right now, resolved from the library team and this event's
 * age groups. `ready` is the one-press case: the library already knows the level of play and the
 * grad year names exactly one age group, so the button can say where the team is going.
 */
type RowPlan =
    | { kind: 'registered' }
    | { kind: 'ready'; req: LibraryRegisterRequest; ageGroupLabel: string; waitlist: boolean; pricing: SlotPricing }
    | { kind: 'choose'; needsLop: boolean }
    | { kind: 'closed' };

interface SegmentRow {
    team: ClubTeamDto;
    /** This event's copy, when registered or waitlisted here. */
    registered: RegisteredTeamDto | null;
    /** A director moved this team's registration here into a DROPPED age group. */
    dropped: boolean;
    plan: RowPlan;
}

/**
 * Club Team Library — one of the Teams step's two segments (the other is the event's Registered
 * Teams). Replaces the library fly-in (Todd 2026-09-26): the library is no longer a drawer opened
 * over the event, it is a view of its own, and a row's place on screen says which thing it is.
 *
 *   LEFT  — the library team: name, grad year, level of play, and the library housekeeping
 *           (Edit / Archive / Delete / Restore). Never an event write.
 *   RIGHT — this event: Register in {age group}, or where it is already registered.
 *
 * The reader is an experienced club rep who is new to the library (every other system made them
 * retype their teams for every event), so the segment says what the library is and why, then
 * makes registering from it one press per team, or one press for all of them.
 *
 * Owns no domain state: the Teams step feeds rows and flags and runs every mutation.
 */
@Component({
    selector: 'app-library-segment',
    standalone: true,
    imports: [CurrencyPipe, NgTemplateOutlet, LevelOfPlayPickerComponent, EventAgeGroupPickerComponent, RegisterAllDialogComponent],
    template: `
    <!-- ── What this is, and why (the rep has always retyped teams) ── -->
    <div class="seg-lib-head">
      <div class="lib-purpose">
        <i class="bi bi-collection-fill lib-purpose-icon" aria-hidden="true"></i>
        <div class="lib-purpose-text">
          <strong>Enter your teams once. Never retype a team for a registration again.</strong>
          @if (canRegister()) {
            <span>
              {{ clubPossessive() }} Club Team Library keeps every team you add, ready for
              {{ eventName() }} and every event after it. Press <b>Register</b> on a team to enter it here.
            </span>
          } @else {
            <span>
              {{ clubPossessive() }} Club Team Library keeps every team you add, ready for every event.
              Team registration for {{ eventName() }} is closed, but you can still add and edit library teams.
            </span>
          }
        </div>
      </div>
      <button type="button" class="btn-add-team" [disabled]="actionInProgress()" (click)="addNew.emit()">
        <i class="bi bi-plus-circle" aria-hidden="true"></i>
        {{ canRegister() ? 'Add a New Team' : 'Add Library Team' }}
      </button>
    </div>

    <!-- One row's cells. Shared by every group so a team looks the same wherever it sits. -->
    <ng-template #rowTpl let-row>
      @let team = row.team;
      @let plan = row.plan;
      @let editing = editingId() === team.clubTeamId;
      <div class="lib-row"
           [class.is-registered]="plan.kind === 'registered'"
           [class.is-archived]="team.bArchived"
           [class.is-pending]="isPending(team.clubTeamId)"
           [class.is-editing]="editing"
           [attr.data-club-team-id]="team.clubTeamId">

        <!-- LEFT: the library team + library housekeeping -->
        <div class="col-team">
          <span class="team-name" [attr.title]="team.clubTeamName">{{ team.clubTeamName }}</span>
          <span class="team-meta">
            <span class="meta-pair"><span class="meta-key">Grad</span>{{ team.clubTeamGradYear || '—' }}</span>
            <span class="meta-pair"><span class="meta-key">LOP</span>{{ formatLop(team.clubTeamLevelOfPlay) || '—' }}</span>
            @if (isPending(team.clubTeamId) && plan.kind !== 'registered') {
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
                      [attr.title]="editLock ?? 'Edit this team in your Club Team Library (name, grad year, level of play)'"
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

        <!-- RIGHT: this event -->
        <div class="col-event">
          @if (row.dropped && plan.kind !== 'registered') {
            <span class="ev-dropped"><i class="bi bi-x-circle" aria-hidden="true"></i>Dropped from {{ eventName() }} by the director</span>
          }
          @switch (plan.kind) {
            @case ('registered') {
              @let reg = row.registered!;
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
            }
            @case ('ready') {
              @let ready = $any(plan);
              <button type="button" class="btn-reg" [class.btn-reg--wl]="ready.waitlist"
                      [disabled]="actionInProgress() || (editingId() !== null && !editing)"
                      (click)="registerNow(ready.req)">
                <i class="bi" [class.bi-trophy-fill]="!ready.waitlist" [class.bi-hourglass-split]="ready.waitlist" aria-hidden="true"></i>
                {{ ready.waitlist ? 'Join the ' + ready.ageGroupLabel + ' waitlist' : 'Register in ' + ready.ageGroupLabel }}
              </button>
              <span class="ev-sub">
                @switch (ready.pricing.kind) {
                  @case ('waitlist') { {{ ready.ageGroupLabel }} is full &middot; no fee until placed }
                  @case ('free') { No fee }
                  @case ('deposit') { Deposit {{ ready.pricing.now | currency }} now &middot; {{ ready.pricing.total | currency }} total }
                  @case ('full') { {{ ready.pricing.total | currency }} }
                }
                &middot;
                <button type="button" class="btn-change" [disabled]="actionInProgress() || (editingId() !== null && !editing)"
                        (click)="openEditor(team)">change</button>
              </span>
            }
            @case ('choose') {
              <button type="button" class="btn-reg btn-reg--choose"
                      [disabled]="actionInProgress() || (editingId() !== null && !editing)"
                      (click)="openEditor(team)">
                <i class="bi bi-ui-checks-grid" aria-hidden="true"></i>
                {{ $any(plan).needsLop ? 'Choose level & age group' : 'Choose age group' }}
              </button>
              <span class="ev-sub">
                {{ $any(plan).needsLop ? 'No level of play saved on this team' : 'No age group matches grad ' + (team.clubTeamGradYear || '—') }}
              </span>
            }
            @case ('closed') {
              <span class="ev-closed"><i class="bi bi-lock-fill" aria-hidden="true"></i>Registration closed</span>
            }
          }
        </div>

        <!-- The register editor, full width under its row. Only when the rep asked to choose. -->
        @if (editing) {
          <div class="reg-editor" role="group" [attr.aria-label]="'Register ' + team.clubTeamName">
            <div class="reg-editor-head">
              Registering <strong>{{ team.clubTeamName }}</strong> for {{ eventName() }}
            </div>
            <div class="reg-editor-step">
              <span class="step-label"><span class="step-num">1</span>Level of play for {{ eventName() }}</span>
              <app-level-of-play-picker [selected]="pickLop()" (selectedChange)="onLopPicked($event)" />
              <span class="step-hint">Your library team keeps its own level of play.</span>
            </div>
            <div class="reg-editor-step">
              <span class="step-label"><span class="step-num">2</span>Age group</span>
              @if (!pickLop()) {
                <span class="step-gate"><i class="bi bi-arrow-up-circle-fill" aria-hidden="true"></i>Choose a level of play above, then the age groups unlock.</span>
              }
              <app-event-age-group-picker
                variant="chip"
                [ageGroups]="ageGroups()"
                [gradYear]="team.clubTeamGradYear"
                [disabled]="actionInProgress() || !pickLop()"
                [showSelectedFee]="true"
                [selected]="pickAg()"
                (selectedChange)="pickAg.set($event)" />
            </div>
            <div class="reg-editor-actions">
              <button type="button" class="btn-editor-cancel" (click)="closeEditor()">Cancel</button>
              <button type="button" class="btn-reg" [class.btn-reg--wl]="pickWaitlists()"
                      [disabled]="actionInProgress() || !pickLop() || !pickAg()"
                      (click)="commitEditor(team)">
                <i class="bi" [class.bi-trophy-fill]="!pickWaitlists()" [class.bi-hourglass-split]="pickWaitlists()" aria-hidden="true"></i>
                {{ editorSubmitLabel() }}
              </button>
            </div>
          </div>
        }
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
        <span>For {{ eventName() }}</span>
      </div>

      <!-- Ready to register: the working list -->
      @if (toRegisterRows().length > 0) {
        <div class="lib-group">
          <span class="lib-group-title">{{ canRegister() ? 'Ready to register' : 'Not registered' }}</span>
          <span class="lib-group-count">{{ toRegisterRows().length }}</span>
          <span class="lib-group-hint">fit an age group at {{ eventName() }}</span>
          @if (bulkCandidates().length >= 2) {
            <button type="button" class="btn-reg-all"
                    [disabled]="actionInProgress() || editingId() !== null"
                    (click)="showRegisterAll.set(true)">
              <i class="bi bi-lightning-charge-fill" aria-hidden="true"></i>Register all {{ bulkCandidates().length }}
            </button>
          }
        </div>
        @for (row of toRegisterRows(); track row.team.clubTeamId) {
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

      <!-- Outside the event's age groups: still registerable (ruling 08-06), folded by default -->
      @if (outsideRows().length > 0) {
        <button type="button" class="lib-group lib-group--toggle"
                [attr.aria-expanded]="showOutside()" (click)="showOutside.set(!showOutside())">
          <i class="bi" [class.bi-chevron-down]="showOutside()" [class.bi-chevron-right]="!showOutside()" aria-hidden="true"></i>
          <span class="lib-group-title">Outside {{ eventName() }}'s age groups</span>
          <span class="lib-group-count">{{ outsideRows().length }}</span>
          @if (oldestOffered() !== null) {
            <span class="lib-group-hint">oldest age group here is {{ oldestOffered() }}</span>
          }
        </button>
        @if (showOutside()) {
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

    @if (showRegisterAll()) {
      <app-register-all-dialog
        [candidates]="bulkCandidates()"
        [ageGroups]="ageGroups()"
        [eventName]="eventName()"
        (confirmed)="onRegisterAllConfirmed($event)"
        (cancelled)="showRegisterAll.set(false)" />
    }
    `,
    styles: [`
      :host { display: flex; flex-direction: column; gap: var(--space-3); }

      /* ── Purpose band + Add ── */
      .seg-lib-head {
        display: flex;
        align-items: center;
        gap: var(--space-3);
      }

      .lib-purpose {
        flex: 1;
        display: flex;
        align-items: flex-start;
        gap: var(--space-3);
        padding: var(--space-3);
        border: 1px solid color-mix(in srgb, var(--bs-primary) 25%, transparent);
        border-left: 3px solid var(--bs-primary);
        border-radius: var(--radius-md);
        background: color-mix(in srgb, var(--bs-primary) 6%, var(--brand-surface));
      }

      .lib-purpose-icon { font-size: var(--font-size-xl); color: var(--bs-primary); line-height: 1; }

      .lib-purpose-text {
        display: flex;
        flex-direction: column;
        gap: 2px;
        font-size: var(--font-size-sm);
        line-height: var(--line-height-normal);
        color: var(--brand-text);

        strong { font-size: var(--font-size-base); line-height: var(--line-height-tight); }
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
        &.is-editing { background: color-mix(in srgb, var(--bs-primary) 4%, transparent); }
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

      .btn-reg-all {
        margin-left: auto;
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
        padding: 3px var(--space-3);
        border: 1px solid var(--bs-success);
        border-radius: var(--radius-full);
        background: var(--bs-success);
        color: var(--neutral-0);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        white-space: nowrap;
        cursor: pointer;
        transition: filter 0.12s ease;

        &:hover:not(:disabled) { filter: brightness(0.93); }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        &:disabled { opacity: 0.45; cursor: default; }
      }

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

      /* RIGHT: this event */
      .col-event { display: flex; flex-direction: column; align-items: flex-start; gap: 3px; min-width: 0; }

      .btn-reg {
        display: inline-flex;
        align-items: center;
        gap: var(--space-2);
        max-width: 100%;
        padding: 5px var(--space-3);
        border: 1px solid var(--bs-success);
        border-radius: var(--radius-sm);
        background: var(--bs-success);
        color: var(--neutral-0);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        cursor: pointer;
        box-shadow: var(--shadow-xs);
        transition: filter 0.12s ease, transform 0.12s ease;

        &:hover:not(:disabled) { filter: brightness(0.93); transform: translateY(-1px); }
        &:active:not(:disabled) { transform: translateY(0); }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        &:disabled { opacity: 0.4; cursor: default; transform: none; }

        &--wl { border-color: var(--bs-warning); background: var(--bs-warning); color: var(--bs-dark); }
        &--choose {
          border-color: var(--bs-success);
          background: var(--brand-surface);
          color: var(--bs-success);
          box-shadow: none;
          &:hover:not(:disabled) { filter: none; background: color-mix(in srgb, var(--bs-success) 8%, var(--brand-surface)); }
        }
      }

      .ev-sub {
        font-size: var(--font-size-2xs);
        color: var(--brand-text-muted);
        font-variant-numeric: tabular-nums;
      }

      .btn-change {
        padding: 0;
        border: none;
        background: transparent;
        color: var(--bs-primary);
        font-size: inherit;
        font-weight: var(--font-weight-semibold);
        text-decoration: underline;
        cursor: pointer;

        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); border-radius: var(--radius-sm); }
        &:disabled { opacity: 0.4; cursor: default; }
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

      .ev-dropped, .ev-closed {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
      }

      /* ── Register editor (full row width) ── */
      .reg-editor {
        grid-column: 1 / -1;
        display: flex;
        flex-direction: column;
        gap: var(--space-3);
        margin-top: var(--space-1);
        padding: var(--space-3);
        border: 1px solid color-mix(in srgb, var(--bs-primary) 30%, transparent);
        border-radius: var(--radius-md);
        background: var(--brand-surface);
        box-shadow: var(--shadow-sm);
      }

      .reg-editor-head { font-size: var(--font-size-sm); color: var(--brand-text); }

      .reg-editor-step { display: flex; flex-direction: column; gap: var(--space-1); }

      .step-label {
        display: inline-flex;
        align-items: center;
        gap: var(--space-2);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-bold);
        color: var(--brand-text);
      }

      .step-num {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 18px;
        height: 18px;
        border-radius: 50%;
        background: var(--bs-primary);
        color: var(--neutral-0);
        font-size: 10px;
      }

      .step-hint { font-size: var(--font-size-2xs); color: var(--brand-text-muted); }

      .step-gate {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-medium);
        color: var(--bs-primary);
      }

      .reg-editor-actions { display: flex; justify-content: flex-end; gap: var(--space-2); }

      .btn-editor-cancel {
        padding: 5px var(--space-3);
        border: 1px solid var(--bs-border-color);
        border-radius: var(--radius-sm);
        background: transparent;
        color: var(--brand-text);
        font-size: var(--font-size-sm);
        cursor: pointer;

        &:hover { background: color-mix(in srgb, var(--bs-body-color) 5%, transparent); }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
      }

      /* ── Mobile ── */
      @media (max-width: 575.98px) {
        .seg-lib-head { flex-direction: column; align-items: stretch; }
        .btn-add-team { justify-content: center; }
        .lib-head { display: none; }
        .lib-row { grid-template-columns: 1fr; row-gap: var(--space-2); }
        .lib-group-hint { display: none; }
      }

      @media (prefers-reduced-motion: reduce) {
        .btn-add-team, .btn-reg, .btn-reg-all, .btn-lib, .lib-row { transition: none !important; }
        .btn-reg:hover:not(:disabled) { transform: none; }
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
    /** Saved to the library (not registered) during this session — tinted and scrolled to. */
    readonly pendingLibraryOnly = input<ReadonlySet<number>>(new Set());
    /** Bumped by the step's "Register it now": bring the first pending row into view again. */
    readonly revealPending = input(0);

    readonly register = output<LibraryRegisterRequest>();
    readonly registerMany = output<LibraryRegisterRequest[]>();
    readonly addNew = output<void>();
    readonly edit = output<ClubTeamDto>();
    readonly archive = output<ClubTeamDto>();
    readonly delete = output<ClubTeamDto>();
    readonly restore = output<ClubTeamDto>();

    readonly formatLop = formatLop;

    readonly showOutside = signal(false);
    readonly showArchived = signal(false);
    readonly showRegisterAll = signal(false);

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

    /** Every library team with its plan for this event, alphabetical. */
    private readonly rows = computed<SegmentRow[]>(() => {
        const registered = this.registeredByClubTeam();
        const dropped = this.droppedClubTeamIds();
        return [...this.clubTeams()]
            .sort((a, b) => a.clubTeamName.localeCompare(b.clubTeamName))
            .map(team => {
                const reg = registered.get(team.clubTeamId) ?? null;
                return { team, registered: reg, dropped: dropped.has(team.clubTeamId), plan: this.planFor(team, reg) };
            });
    });

    private fits(team: ClubTeamDto): boolean {
        return isTeamOfferedAtEvent(this.oldestOffered(), team.clubTeamGradYear);
    }

    readonly toRegisterRows = computed(() =>
        this.rows().filter(r => !r.team.bArchived && !r.registered && this.fits(r.team)));
    /** Registered here — including a team whose library row was archived since, so no registration ever drops out of sight. */
    readonly registeredRows = computed(() => this.rows().filter(r => !!r.registered));
    readonly outsideRows = computed(() =>
        this.rows().filter(r => !r.team.bArchived && !r.registered && !this.fits(r.team)));
    readonly archivedRows = computed(() => this.rows().filter(r => r.team.bArchived && !r.registered));
    readonly activeCount = computed(() => this.clubTeams().filter(t => !t.bArchived).length);

    /** Rows whose answer is obvious — the ones "Register all" can take without asking anything. */
    readonly bulkCandidates = computed<LibraryRegisterRequest[]>(() => {
        const out: LibraryRegisterRequest[] = [];
        for (const r of this.toRegisterRows()) if (r.plan.kind === 'ready') out.push(r.plan.req);
        return out;
    });

    /**
     * The one-press plan: level of play from the library row (must be on the 1–5 scale), age group
     * from the grad year (redirected to the WAITLIST twin when full, same rule the fly-in seeded).
     * Anything short of both is a choice for the rep, not a guess on their behalf.
     */
    private planFor(team: ClubTeamDto, reg: RegisteredTeamDto | null): RowPlan {
        if (reg) return { kind: 'registered' };
        if (!this.canRegister() || team.bArchived) return { kind: 'closed' };
        const lop = normalizeLop(team.clubTeamLevelOfPlay);
        if (!lop) return { kind: 'choose', needsLop: true };
        const ageGroupId = this.fits(team) ? resolveRecommendedAgeGroupId(this.ageGroups(), team.clubTeamGradYear) : '';
        const ag = ageGroupId ? this.ageGroups().find(a => a.ageGroupId === ageGroupId) : undefined;
        if (!ag) return { kind: 'choose', needsLop: false };
        const waitlist = this.waitlists(ag);
        return {
            kind: 'ready',
            req: { team, ageGroupId: ag.ageGroupId, levelOfPlay: lop },
            ageGroupLabel: ageGroupLabel(ag),
            waitlist,
            pricing: waitlist ? { kind: 'waitlist' } : this.pricingOf(ag),
        };
    }

    private waitlists(ag: AgeGroupDto): boolean {
        return isWaitlistAgeGroup(ag) || ag.registeredCount >= ag.maxTeams;
    }

    private pricingOf(ag: AgeGroupDto): SlotPricing {
        return describeSlotPricing({
            isFull: false,
            fee: (ag.deposit || 0) + (ag.balanceDue || 0),
            deposit: ag.deposit || 0,
            balanceDue: ag.balanceDue || 0,
            fullPaymentRequired: !!ag.fullPaymentRequired,
        });
    }

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

    registerNow(req: LibraryRegisterRequest): void {
        if (this.actionInProgress()) return;
        this.register.emit(req);
    }

    onRegisterAllConfirmed(reqs: LibraryRegisterRequest[]): void {
        this.showRegisterAll.set(false);
        if (reqs.length) this.registerMany.emit(reqs);
    }

    // ── The register editor (a real choice: no LOP saved, no age-group match, or "change") ──
    readonly editingId = signal<number | null>(null);
    readonly pickLop = signal('');
    readonly pickAg = signal('');

    readonly pickWaitlists = computed(() => {
        const ag = this.ageGroups().find(a => a.ageGroupId === this.pickAg());
        return !!ag && this.waitlists(ag);
    });

    /** Names the AGE GROUP, never the team (ruling 2026-09-24): the editor's head already names the team. */
    readonly editorSubmitLabel = computed(() => {
        const ag = this.ageGroups().find(a => a.ageGroupId === this.pickAg());
        if (!ag) return 'Register';
        const label = ageGroupLabel(ag);
        return this.pickWaitlists() ? `Join the ${label} waitlist` : `Register in ${label}`;
    });

    openEditor(team: ClubTeamDto): void {
        if (this.editingId() === team.clubTeamId) { this.closeEditor(); return; }
        const lop = normalizeLop(team.clubTeamLevelOfPlay);
        this.pickLop.set(lop);
        this.pickAg.set(lop ? resolveRecommendedAgeGroupId(this.ageGroups(), team.clubTeamGradYear) : '');
        this.editingId.set(team.clubTeamId);
    }

    /** First LOP pick lays the grad-year seed; never re-seeds over a pick the rep made. */
    onLopPicked(lop: string): void {
        this.pickLop.set(lop);
        if (!lop || this.pickAg()) return;
        const team = this.clubTeams().find(t => t.clubTeamId === this.editingId());
        if (team) this.pickAg.set(resolveRecommendedAgeGroupId(this.ageGroups(), team.clubTeamGradYear));
    }

    closeEditor(): void {
        this.editingId.set(null);
        this.pickLop.set('');
        this.pickAg.set('');
    }

    commitEditor(team: ClubTeamDto): void {
        const ageGroupId = this.pickAg();
        const levelOfPlay = this.pickLop();
        if (!ageGroupId || !levelOfPlay || this.actionInProgress()) return;
        this.register.emit({ team, ageGroupId, levelOfPlay });
        this.closeEditor();
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
                if (team && !team.bArchived && !this.fits(team)) this.showOutside.set(true);
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
