import { ChangeDetectionStrategy, Component, OnInit, inject, output, signal, computed, DestroyRef } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { EMPTY, catchError, concatMap, defer, from, interval, map, of } from 'rxjs';
import { RegisteredTeamsGridComponent, sumDueNowOf } from '../components/registered-teams-grid.component';
import { TeamWizardStateService } from '../state/team-wizard-state.service';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { ToastService } from '@shared-ui/toast.service';
import { cartPhaseBadgeLabel, resolveCartPhase } from '@shared-ui/fees/cart-phase';
import { JobService } from '@infrastructure/services/job.service';
import { TeamFormModalComponent } from './team-form-modal.component';
import { AddAndRegisterTeamModalComponent } from './add-and-register-team-modal.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
// Library fly-in RETIRED from this step 2026-09-26 (Todd: the Club Team Library is now a segment of
// the step, not a drawer over it). Kept, commented, in case we return to it — the component file
// itself is untouched. Re-enable = this import, the imports[] entry, the <app-library-flyin> block
// in the template, and the fly-in methods/signal in the class (all marked RETIRED FLY-IN).
// import { LibraryFlyinComponent, type RegisterRequest, type RegisteredInfo } from '../components/library-flyin.component';
import { LibrarySegmentComponent } from '../components/library-segment.component';
import { RegisterTeamModalComponent } from '../components/register-team-modal.component';
import { TeamsBoardComponent } from '../components/teams-board.component';
import type { LibraryRegisterRequest } from '../components/library-segment.types';
import { TeamRenameConfirmComponent, type TeamRenameConfirmation } from '@shared/teams/team-rename-confirm.component';
import { clubTeamArchiveLockReason, clubTeamDeleteLockReason, clubTeamEditLockReason, type ClubTeamLockContext } from '@shared/teams/club-team-locks';
import type { TeamsMetadataResponse, AgeGroupDto, RegisteredTeamDto, ClubTeamDto } from '@core/api';
import { extractHttpErrorMessage } from '@infrastructure/interceptors/http-error-utils';

/**
 * The registered team whose EVENT copy is being renamed. Event origin only: the library has no
 * Rename of its own any more (Todd 2026-09-24) — its name is one of the details in Edit, library
 * only, never propagated here. This dialog may carry an event rename back into the library.
 */
type PendingRename = { origin: 'event'; team: RegisteredTeamDto };

/** The step's two segments. */
type TeamsSegment = 'library' | 'registered';

/**
 * Teams step — two segments on one card (Todd 2026-09-26):
 *
 *   Club Team Library             the club's teams, entered once; Register from here.
 *   {event} Registered Teams      what this event holds, and what it owes.
 *
 * Which one opens is decided ONCE, when the step's data first lands (see pickOpeningSegment), from
 * the rep's state: nothing in the library, nothing registered, money due now, or teams still to
 * bring. It never flips on its own after that — registering a team mid-visit must not yank the
 * rep off the list they are working. Assigning an age group IS the registration act.
 */
@Component({
    selector: 'app-trw-teams-step',
    standalone: true,
    imports: [CurrencyPipe, RegisteredTeamsGridComponent, TeamFormModalComponent, AddAndRegisterTeamModalComponent, ConfirmDialogComponent, /* RETIRED FLY-IN: LibraryFlyinComponent, */ LibrarySegmentComponent, RegisterTeamModalComponent, TeamsBoardComponent, TeamRenameConfirmComponent],
    template: `
    @if (loading()) {
      <div class="text-center py-4">
        <div class="spinner-border text-primary" role="status">
          <span class="visually-hidden">Loading teams...</span>
        </div>
      </div>
    } @else if (error()) {
      <div class="alert alert-danger">{{ error() }}</div>
    } @else {

      <!-- ── One card, two segments (Todd 2026-09-26). The green edge means teams are in. ── -->
      <div class="step-card" [class.step-card-registered]="enteredTeams().length > 0">

        @if (layout() === 'board') {
        <!-- The board (Todd 2026-09-27): library left, this event right — a team is on one side
             only, so which list an edit touches answers itself. The tabs below are kept for a return. -->
        <div class="seg-panel-body">
          <app-teams-board
            [clubTeams]="allLibraryTeams()"
            [registeredTeams]="enteredTeams()"
            [ageGroups]="ageGroups()"
            [eventName]="eventName()"
            [clubName]="clubName()"
            [canRegister]="canRegisterTeam()"
            [canRemove]="canRemoveTeam()"
            [renameLockReason]="canEditTeam() ? null : 'Editing closed by the director'"
            [actionInProgress]="actionInProgress()"
            [undoDeadlines]="gridUndoDeadlines()"
            [now]="clock()"
            [pendingLibraryOnly]="pendingLibraryOnly()"
            [phaseLabel]="enteredTeams().length > 0 ? phaseBadgeLabel() : null"
            (register)="onLibraryRegister($event)"
            (remove)="onRemoveTeam($event)"
            (rename)="onRenameTeam($event)"
            (addNew)="onAddLibraryTeam()"
            (librarySaved)="onTeamEdited()"
            (archive)="askArchiveTeam($event)"
            (delete)="askDeleteTeam($event)"
            (restore)="askRestoreTeam($event)" />
        </div>
        } @else {

        <!-- No tabs for a brand-new club (empty library, nothing registered): two empty views are
             nothing to switch between. Both come back with the first team (Todd 2026-09-26). -->
        @if (showSegments()) {
        <div class="seg-bar" role="tablist" aria-label="Registered Teams or Club Team Library">
          <button type="button" role="tab" id="teams-seg-tab-registered"
                  class="seg-tab seg-tab--registered"
                  [class.is-active]="segment() === 'registered'"
                  [attr.aria-selected]="segment() === 'registered'"
                  aria-controls="teams-seg-panel"
                  [attr.tabindex]="segment() === 'registered' ? 0 : -1"
                  (click)="selectSegment('registered')"
                  (keydown)="onSegmentKey($event)">
            <i class="bi seg-tab-radio" aria-hidden="true"
               [class.bi-record-circle-fill]="segment() === 'registered'"
               [class.bi-circle]="segment() !== 'registered'"></i>
            <i class="bi bi-trophy-fill seg-tab-icon" aria-hidden="true"></i>
            <span class="seg-tab-text">
              <span class="seg-tab-title">{{ eventName() }} Registered Teams</span>
              <span class="seg-tab-sub">
                @if (enteredTeams().length === 0) {
                  None yet
                } @else {
                  {{ enteredTeams().length }} registered &middot;
                  @if (dueNow() > 0) { {{ dueNow() | currency }} due now } @else { nothing due now }
                }
              </span>
            </span>
            @if (segment() !== 'registered') {
              <span class="seg-tab-switch" aria-hidden="true">Switch<i class="bi bi-chevron-right"></i></span>
            }
          </button>
          <button type="button" role="tab" id="teams-seg-tab-library"
                  class="seg-tab seg-tab--library"
                  [class.is-active]="segment() === 'library'"
                  [attr.aria-selected]="segment() === 'library'"
                  aria-controls="teams-seg-panel"
                  [attr.tabindex]="segment() === 'library' ? 0 : -1"
                  (click)="selectSegment('library')"
                  (keydown)="onSegmentKey($event)">
            <i class="bi seg-tab-radio" aria-hidden="true"
               [class.bi-record-circle-fill]="segment() === 'library'"
               [class.bi-circle]="segment() !== 'library'"></i>
            <i class="bi bi-collection-fill seg-tab-icon" aria-hidden="true"></i>
            <span class="seg-tab-text">
              <span class="seg-tab-title">Club Team Library</span>
              <span class="seg-tab-sub">{{ librarySegmentSub() }}</span>
            </span>
            @if (segment() !== 'library') {
              <span class="seg-tab-switch" aria-hidden="true">Switch<i class="bi bi-chevron-right"></i></span>
            }
          </button>
        </div>
        }

        <div class="seg-panel" id="teams-seg-panel"
             [attr.role]="showSegments() ? 'tabpanel' : null"
             [attr.aria-labelledby]="!showSegments() ? null : segment() === 'library' ? 'teams-seg-tab-library' : 'teams-seg-tab-registered'">

          @if (segment() === 'library') {
            <!-- ONE library view for every rep (Todd 2026-09-26, "KEEP IT DRY"): an empty library is the
                 same segment with its empty head, not a separate hero. -->
            <div class="seg-panel-body">
              <app-library-segment
                [clubTeams]="allLibraryTeams()"
                [registeredTeams]="enteredTeams()"
                [droppedTeams]="droppedTeams()"
                [clubName]="clubName()"
                [eventName]="eventName()"
                [canRegister]="canRegisterTeam()"
                [actionInProgress]="actionInProgress()"
                [pendingLibraryOnly]="pendingLibraryOnly()"
                [revealPending]="revealPending()"
                [listFirst]="enteredTeams().length === 0"
                (goRegistered)="selectSegment('registered')"
                (addNew)="onAddLibraryTeam()"
                (edit)="openEditModal($event)"
                (archive)="askArchiveTeam($event)"
                (delete)="askDeleteTeam($event)"
                (restore)="askRestoreTeam($event)" />
            </div>
          } @else {
            @if (enteredTeams().length === 0) {
              <!-- Nothing registered yet: neutral, no check. Green and a check mean done (Todd 2026-09-24). -->
              <div class="wizard-empty-state cta-empty-wrap" style="padding: var(--space-6) var(--space-4)">
                <i class="bi bi-clipboard"></i>
                <strong>No teams registered for {{ eventName() }} yet</strong>
                <span>
                  @if (!canRegisterTeam()) {
                    Team registration for {{ eventName() }} is closed.
                  } @else if (unregisteredTeams().length > 0) {
                    Pick the teams you're bringing from your Club Team Library &mdash;
                    {{ unregisteredTeams().length }} not registered yet.
                  } @else {
                    Your Club Team Library has no active teams. Add or restore one there first.
                  }
                </span>
                @if (canRegisterTeam()) {
                  @if (unregisteredTeams().length > 0) {
                    <button type="button" class="btn btn-success btn-lg cta-empty cta-empty-event"
                            (click)="openRegisterModal()">
                      <i class="bi bi-trophy-fill me-2"></i>
                      Register Your First Team
                      <i class="bi bi-arrow-right ms-2 cta-empty-arrow"></i>
                    </button>
                  } @else {
                    <button type="button" class="btn btn-primary btn-lg cta-empty"
                            (click)="selectSegment('library')">
                      <i class="bi bi-collection-fill me-2"></i>
                      Go to Club Team Library
                      <i class="bi bi-arrow-right ms-2 cta-empty-arrow"></i>
                    </button>
                  }
                }
              </div>
            } @else {
              <div class="seg-panel-body">
                <div class="registered-head">
                  <span class="phase-badge">
                    <span class="phase-badge__label">Payment Phase</span>
                    <span class="phase-badge__value">{{ phaseBadgeLabel() }}</span>
                  </span>
                </div>
                <!-- AR-095 item 5. Ann filed "delete the summary line" because club reps (and,
                     she reports 09-18, directors) read this card as their accounting statement.
                     The totals are NOT wrong — every cell is an honest sum of the column above
                     it — so deleting them removes the evidence, not the misreading, which the
                     rows carry just as strongly. What was missing is a statement of which
                     document this is. It sits ABOVE the numbers so nobody reaches $41,400
                     without having been told. Teams step ONLY: on the director's club-rep
                     accounting grid the totals genuinely ARE the statement. -->
                <!-- What the pencil means HERE (Todd 2026-09-26): this event's record, not the library. -->
                <p class="edit-scope">
                  <i class="bi bi-pencil" aria-hidden="true"></i>
                  <span>The <b>pencil</b> edits a team's registration for {{ eventName() }} &mdash; its name and level of
                    play at this event. Your Club Team Library, what future events start from, is not changed.</span>
                </p>
                <p class="pricing-notice">
                  <i class="bi bi-info-circle" aria-hidden="true"></i>
                  <span>Each team's fee status is keyed to <strong>now</strong>, <strong>later</strong> or
                    <strong>paid</strong>. Amounts are the fee itself &mdash; any processing fee is added when you
                    <strong>Continue to Payment</strong> below.</span>
                </p>
                <app-registered-teams-grid
                  [teams]="enteredTeams()"
                  [showStructure]="true"
                  [showTotalFee]="false"
                  [showDeposit]="false"
                  [showBalance]="false"
                  [showOwed]="false"
                  [showPaid]="false"
                  [showProcessing]="false"
                  [showCcOwed]="false"
                  [showCkOwed]="false"
                  [showRegDate]="false"
                  [showLop]="true"
                  [showRemove]="canRemoveTeam()"
                  [showRename]="true"
                  [renameLockReason]="canEditTeam() ? null : 'Editing closed by the director'"
                  [actionInProgress]="actionInProgress()"
                  [frozenTeamCol]="false"
                  [teamColWidth]="120"
                  [gridHeight]="'auto'"
                  [undoDeadlines]="gridUndoDeadlines()"
                  [now]="clock()"
                  (removeTeam)="onRemoveTeam($event)"
                  (renameTeam)="onRenameTeam($event)" />
              </div>
            }
          }
        </div>
        }

        <!-- Continue lives OUTSIDE the segments: either view can move on. Full weight only when
             money is due now; with nothing due it is the quiet way to finish. -->
        @if (enteredTeams().length > 0) {
          <div class="step-card-footer">
            @if (confirmingContinue()) {
              <!-- The P0 guard the fly-in's Done used to carry: a team saved to the library this
                   session and still not registered here. The rep picks one of two named outcomes. -->
              <div class="continue-interstitial" role="alertdialog"
                   aria-labelledby="continue-interstitial-title" aria-describedby="continue-interstitial-names">
                <div class="ci-head">
                  <i class="bi bi-exclamation-triangle-fill" aria-hidden="true"></i>
                  <span id="continue-interstitial-title">
                    {{ pendingTeams().length === 1 ? '1 team is' : pendingTeams().length + ' teams are' }}
                    in your Club Team Library but <strong>not registered for {{ eventName() }}</strong>
                  </span>
                </div>
                <ul class="ci-names" id="continue-interstitial-names">
                  @for (t of pendingTeams(); track t.clubTeamId) {
                    <li>{{ t.clubTeamName }}<span class="ci-grad">Grad {{ t.clubTeamGradYear || '—' }}</span></li>
                  }
                </ul>
                <div class="ci-actions">
                  <button type="button" class="btn-ci-leave" (click)="continueWithoutPending()">
                    Continue without {{ pendingTeams().length === 1 ? 'it' : 'them' }}
                  </button>
                  <button type="button" class="btn-ci-register" (click)="registerPendingNow()">
                    <i class="bi bi-trophy-fill" aria-hidden="true"></i>
                    Register {{ pendingTeams().length === 1 ? 'it' : 'them' }} now
                  </button>
                </div>
              </div>
            } @else if (canRegisterTeam() && layout() === 'segments') {
              <!-- The way to add a team is a VERB on the screen, not the library tab's noun (Todd
                   2026-09-26: "not clear at all how I would add a team"). Register Another Team opens
                   the Register-a-team modal — the ONE register path — from either segment. -->
              <div class="action-segments" role="group" aria-label="What's next?">
                <button type="button" class="action-segment action-segment-stay"
                        [disabled]="actionInProgress()"
                        (click)="openRegisterModal()">
                  <i class="bi bi-plus-circle-fill action-segment-icon" aria-hidden="true"></i>
                  <span class="action-segment-content">
                    <span class="action-segment-title">Register Another Team</span>
                    <span class="action-segment-sub">
                      @if (unregisteredTeams().length > 0) {
                        {{ unregisteredTeams().length }} library {{ unregisteredTeams().length === 1 ? 'team' : 'teams' }} not registered yet
                      } @else {
                        From your Club Team Library
                      }
                    </span>
                  </span>
                </button>
                <button type="button" class="action-segment action-segment-advance"
                        [disabled]="actionInProgress()"
                        (click)="onContinue()">
                  <span class="action-segment-content">
                    <span class="action-segment-title">Continue to Payment</span>
                    <span class="action-segment-sub">
                      @if (dueNow() > 0) { {{ dueNow() | currency }} due now } @else { Nothing due now &middot; review and finish }
                    </span>
                  </span>
                  <i class="bi bi-currency-dollar action-segment-icon" aria-hidden="true"></i>
                </button>
              </div>
            } @else {
              <button type="button" class="continue-btn" [class.continue-btn--due]="dueNow() > 0"
                      [disabled]="actionInProgress()"
                      (click)="onContinue()">
                <span class="continue-text">
                  <span class="continue-title">Continue to Payment</span>
                  <span class="continue-sub">
                    @if (dueNow() > 0) { {{ dueNow() | currency }} due now } @else { Nothing due now &middot; review and finish }
                  </span>
                </span>
                <i class="bi bi-arrow-right-circle-fill continue-icon" aria-hidden="true"></i>
              </button>
            }
          </div>
        }
      </div>

      <!-- RETIRED FLY-IN (2026-09-26) — the Club Team Library segment above replaces it. Kept for a return.
      <app-library-flyin
        [isOpen]="showLibraryFlyin()"
        [clubTeams]="allLibraryTeams()"
        [clubName]="clubName()"
        [canRegister]="canRegisterTeam()"
        [canRemove]="canRemoveTeam()"
        [actionInProgress]="actionInProgress()"
        [ageGroups]="ageGroups()"
        [enteredTeams]="enteredTeamsMap()"
        [droppedTeams]="droppedTeams()"
        [pendingLibraryOnly]="pendingLibraryOnly()"
        (closed)="closeLibraryFlyin()"
        (manageLibrary)="goToLibraryPage()"
        (leavePending)="clearPendingLibraryOnly()"
        (register)="onFlyinRegister($event)"
        (unregister)="onFlyinUnregister($event)"
        (addNew)="onAddNew()"
        (edit)="openEditModal($event)"
        (archive)="askArchiveTeam($event)"
        (delete)="askDeleteTeam($event)"
        (restore)="askRestoreTeam($event)" />
      -->
    }

    <!-- ═══ MODALS ═══ -->
    @if (showAddModal()) {
      <app-team-form-modal
        [clubName]="clubName()"
        [existingTeams]="allLibraryTeams()"
        (saved)="onTeamAdded()"
        (closed)="showAddModal.set(false)" />
    }

    <!-- Register a team — the ONE register path (Todd 2026-09-26). Stays open across registrations. -->
    @if (showRegisterModal()) {
      <app-register-team-modal
        [clubTeams]="allLibraryTeams()"
        [registeredTeams]="enteredTeams()"
        [ageGroups]="ageGroups()"
        [clubName]="clubName()"
        [eventName]="eventName()"
        [canRegister]="canRegisterTeam()"
        [actionInProgress]="actionInProgress()"
        [undoDeadlines]="gridUndoDeadlines()"
        [now]="clock()"
        (register)="onLibraryRegister($event)"
        (undo)="onRemoveTeam($event)"
        (addNew)="onRegisterModalAddNew()"
        (openLibrary)="onRegisterModalOpenLibrary()"
        (closed)="showRegisterModal.set(false)" />
    }

    @if (showAddAndRegisterModal()) {
      <app-add-and-register-team-modal
        [clubName]="clubName()"
        [eventName]="eventName()"
        [ageGroups]="ageGroups()"
        [firstTeam]="allLibraryTeams().length === 0"
        [existingTeams]="allLibraryTeams()"
        (saved)="onAddAndRegisterSaved()"
        (savedLibraryOnly)="onSavedLibraryOnly($event)"
        (closed)="showAddAndRegisterModal.set(false)" />
    }

    <!-- Library edit — name / grad year / LOP on the library row, never gated, never an event write.
         A registered team's EVENT copy is renamed with the pencil above, never here. -->
    @if (editingTeam(); as editing) {
      <app-team-form-modal
        [clubName]="clubName()"
        [editingTeam]="editing"
        [archiveLockReason]="archiveLockReasonFor(editing)"
        [existingTeams]="allLibraryTeams()"
        (saved)="onTeamEdited()"
        (closed)="editingTeam.set(null)" />
    }

    <!-- The Registered Teams pencil: rename THIS EVENT's copy. The rep may tick "use it in the
         library too"; nothing sweeps on its own. The library never opens this dialog. -->
    @if (pendingRename(); as renaming) {
      <team-rename-confirm
        [editable]="true"
        audience="rep"
        [origin]="renaming.origin"
        [eventLabel]="eventName()"
        [currentName]="renameEventName(renaming)"
        [newName]="renameSeed(renaming)"
        [libraryName]="renameLibraryName(renaming)"
        [registeredHere]="renameRegisteredHere(renaming)"
        [canRenameInEvent]="canEditTeam()"
        [showLevelOfPlay]="renaming.origin === 'event'"
        [levelOfPlay]="renameLevelOfPlay(renaming)"
        [errorMessage]="renameError()"
        [busy]="actionInProgress()"
        (confirmed)="confirmRename($event)"
        (cancelled)="closeRename()" />
    }

@if (pendingRemove()) {
      <confirm-dialog
        title="Remove Team"
        [message]="removeMessage(pendingRemove()!)"
        confirmLabel="Remove"
        confirmVariant="danger"
        (confirmed)="confirmRemove()"
        (cancelled)="cancelRemove()" />
    }

    @if (pendingDelete()) {
      <confirm-dialog
        title="Delete Team"
        [message]="'Permanently delete <strong>' + pendingDelete()!.clubTeamName + '</strong> from your library? This cannot be undone.'"
        confirmLabel="Delete"
        confirmVariant="danger"
        (confirmed)="confirmDelete()"
        (cancelled)="cancelDelete()" />
    }

    @if (pendingArchive()) {
      <confirm-dialog
        title="Archive Team"
        [message]="'Archive <strong>' + pendingArchive()!.clubTeamName + '</strong>? It will disappear from your library but keep its event history. You can restore it from the Archived section.'"
        confirmLabel="Archive"
        confirmVariant="primary"
        (confirmed)="confirmArchive()"
        (cancelled)="cancelArchive()" />
    }

    @if (pendingRestore()) {
      <confirm-dialog
        title="Restore Team"
        [message]="'Restore <strong>' + pendingRestore()!.clubTeamName + '</strong> to your active library?'"
        confirmLabel="Restore"
        confirmVariant="primary"
        (confirmed)="confirmRestore()"
        (cancelled)="cancelRestore()" />
    }

  `,
    styles: [`
      :host { display: flex; flex-direction: column; gap: var(--space-4); }

      /* .step-card / .step-card-registered live in styles/_wizard-globals.scss
         so payment-step shares the same outer chrome. */

      /* AR-095 item 5 — the "what this table is / is not" notice above the grid.
         ONE line: the positive half names what the figures are, the negative half is
         what actually breaks the misread, and the Continue to Payment button below
         names where the real figure lives. Deliberately NOT .tsic-callout--info: a
         filled blue panel under this card's green titlebar reads as a second banner
         and competes with the PAYMENT PHASE badge for the same job. A caption sitting
         directly on the table it describes does not need a box to be read — the
         AM-064 "notes ship too faint" failure was about notes floating alone, which
         this one never does. Non-interactive and unanimated: nothing needed for focus
         or reduced motion. */
      .pricing-notice {
        display: flex;
        align-items: baseline;
        gap: var(--space-2);
        margin: 0 0 var(--space-3);
        padding: 0 var(--space-1);
        font-size: var(--font-size-sm);
        line-height: 1.45;
        color: var(--brand-text);

        > i {
          flex-shrink: 0;
          color: var(--bs-primary);
        }
      }


      /* ── Segmented control — the step's two views (Todd 2026-09-26). It must read as a SWITCH at a
         glance, not as a card header: a recessed track holding two raised pills, the selected pill
         FILLED in its own color with light text, the other flat and muted inside the track.
         Library = primary, Registered = success, so "library, then event" reads the same everywhere.
         Never color alone: fill vs flat, raised vs sunk, and each pill carries its own icon and words. ── */
      .seg-bar {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: var(--space-1);
        margin: var(--space-3) var(--space-3) 0;
        padding: var(--space-1);
        border: 1px solid var(--border-color);
        border-radius: var(--radius-lg);
        background: color-mix(in srgb, var(--bs-body-color) 7%, var(--brand-surface));
        box-shadow: inset 0 1px 3px color-mix(in srgb, var(--bs-body-color) 12%, transparent);
      }

      /* Each pill carries its own color in --seg-color (Library = primary, Registered = success).
         UNSELECTED = an obvious button: raised surface, colored border + title, radio circle, and a
         "Switch >" cue; hover lifts it. SELECTED = filled in its color, light text, filled radio. */
      .seg-tab {
        --seg-color: var(--bs-primary);
        display: flex;
        align-items: center;
        gap: var(--space-3);
        min-width: 0;
        padding: var(--space-2) var(--space-3);
        border: 1.5px solid color-mix(in srgb, var(--seg-color) 45%, transparent);
        border-radius: var(--radius-md);
        background: var(--brand-surface);
        box-shadow: var(--shadow-xs);
        font-family: inherit;
        text-align: left;
        color: var(--brand-text);
        cursor: pointer;
        transition: background-color 0.15s ease, border-color 0.15s ease, box-shadow 0.15s ease, transform 0.15s ease;

        .seg-tab-title { color: var(--seg-color); }
        .seg-tab-icon { color: var(--seg-color); }
        .seg-tab-radio { color: var(--seg-color); }

        &:hover:not(.is-active) {
          border-color: var(--seg-color);
          background: color-mix(in srgb, var(--seg-color) 7%, var(--brand-surface));
          box-shadow: var(--shadow-md);
          transform: translateY(-1px);

          .seg-tab-switch { opacity: 1; }
        }
        &:active:not(.is-active) { transform: translateY(0); }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }

        &.is-active {
          border-color: var(--seg-color);
          background: var(--seg-color);
          color: var(--neutral-0);
          box-shadow: var(--shadow-md);
          cursor: default;

          .seg-tab-title, .seg-tab-icon, .seg-tab-radio { color: inherit; }
          .seg-tab-sub { color: inherit; opacity: 0.9; }
          &:focus-visible { box-shadow: var(--shadow-md), var(--shadow-focus); }
        }
      }

      .seg-tab--library { --seg-color: var(--bs-primary); }
      .seg-tab--registered { --seg-color: var(--bs-success); }

      .seg-tab-radio { font-size: 1rem; line-height: 1; flex-shrink: 0; }
      .seg-tab-icon { font-size: 1.5rem; line-height: 1; flex-shrink: 0; }

      .seg-tab-text { display: flex; flex-direction: column; gap: 2px; min-width: 0; flex: 1; }

      .seg-tab-title {
        font-size: var(--font-size-base);
        font-weight: var(--font-weight-bold);
        line-height: var(--line-height-tight);
      }

      .seg-tab-sub {
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
        font-variant-numeric: tabular-nums;
      }

      .seg-tab-switch {
        display: inline-flex;
        align-items: center;
        gap: 2px;
        flex-shrink: 0;
        padding: 2px var(--space-2);
        border-radius: var(--radius-full);
        background: color-mix(in srgb, var(--seg-color) 12%, transparent);
        color: var(--seg-color);
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-bold);
        text-transform: uppercase;
        letter-spacing: 0.05em;
        opacity: 0.85;
        transition: opacity 0.15s ease;
      }

      .seg-panel-body { padding: var(--space-3); }

      /* What the pencil means on Registered Teams — this event's record; never the library */
      .edit-scope {
        display: flex;
        align-items: baseline;
        gap: var(--space-2);
        margin: 0 0 var(--space-2);
        padding: var(--space-2) var(--space-3);
        border-radius: var(--radius-sm);
        background: color-mix(in srgb, var(--bs-success) 6%, transparent);
        font-size: var(--font-size-xs);
        color: var(--brand-text);

        .bi { color: var(--bs-success); flex-shrink: 0; }
      }

      .registered-head {
        display: flex;
        align-items: center;
        margin-bottom: var(--space-2);
      }

      /* ── Footer: Continue sits outside the segments so either view can move on. Full weight
         (filled tint + shadow) only when money is due now; with nothing due it is the quiet way
         to finish, so it never competes with the Register buttons for a paid-up rep. ── */
      .step-card-footer {
        padding: var(--space-3);
        border-top: 1px solid var(--border-color);
        background: rgba(var(--bs-dark-rgb), 0.015);
      }

      .continue-btn {
        display: flex;
        align-items: center;
        gap: var(--space-3);
        width: 100%;
        padding: var(--space-3) var(--space-4);
        border: 1px solid color-mix(in srgb, var(--emerald-600) 35%, transparent);
        border-radius: var(--radius-md);
        background: var(--brand-surface);
        color: var(--emerald-600);
        font-family: inherit;
        text-align: left;
        cursor: pointer;
        transition: background-color 0.15s ease, box-shadow 0.15s ease;

        &:hover:not(:disabled) { background: color-mix(in srgb, var(--emerald-600) 8%, var(--brand-surface)); }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        &:disabled { opacity: 0.5; cursor: default; }

        &--due {
          border-color: var(--emerald-600);
          background: color-mix(in srgb, var(--emerald-600) 14%, var(--brand-surface));
          box-shadow: var(--shadow-sm);

          &:hover:not(:disabled) { background: color-mix(in srgb, var(--emerald-600) 24%, var(--brand-surface)); }
        }
      }

      /* Registered Teams footer: the two-way fork (Register Another Team | Continue to Payment).
         Two tinted halves sharing one outer edge; each owns its color zone from rest. */
      .action-segments {
        display: grid;
        grid-template-columns: 1fr 1fr;
        border-radius: var(--radius-md);
        overflow: hidden;
        box-shadow: var(--shadow-sm);
      }

      .action-segment {
        display: flex;
        align-items: center;
        gap: var(--space-3);
        padding: var(--space-4);
        border: none;
        border-radius: 0;
        font-family: inherit;
        text-align: left;
        cursor: pointer;
        transition: background 0.15s ease, box-shadow 0.15s ease, transform 0.15s ease;

        &:focus-visible { outline: none; box-shadow: inset 0 0 0 2px currentColor; }
        &:active:not(:disabled) { transform: translateY(1px); }
        &:disabled { opacity: 0.5; cursor: default; }
      }

      .action-segment-icon { font-size: 2rem; flex-shrink: 0; line-height: 1; }

      .action-segment-content { display: flex; flex-direction: column; gap: 2px; min-width: 0; flex: 1; }

      .action-segment-title {
        font-size: var(--font-size-base);
        font-weight: var(--font-weight-bold);
        line-height: var(--line-height-tight);
      }

      .action-segment-sub {
        font-size: var(--font-size-xs);
        font-style: italic;
        line-height: var(--line-height-normal);
        color: color-mix(in srgb, currentColor 80%, var(--brand-text));
      }

      .action-segment-stay {
        color: var(--amber-700);
        background: color-mix(in srgb, var(--amber-500) 18%, transparent);

        &:hover { background: color-mix(in srgb, var(--amber-500) 32%, transparent); box-shadow: inset 0 -2px 0 var(--amber-700); }
      }

      .action-segment-advance {
        color: var(--emerald-600);
        background: color-mix(in srgb, var(--emerald-600) 14%, transparent);

        &:hover:not(:disabled) { background: color-mix(in srgb, var(--emerald-600) 26%, transparent); box-shadow: inset 0 -2px 0 var(--emerald-600); }
      }

      @media (prefers-reduced-motion: reduce) {
        .action-segment { transition: none !important; }
        .action-segment:active { transform: none; }
      }

      .continue-text { flex: 1; display: flex; flex-direction: column; gap: 2px; min-width: 0; }
      .continue-title { font-size: var(--font-size-base); font-weight: var(--font-weight-bold); line-height: var(--line-height-tight); }
      .continue-sub { font-size: var(--font-size-xs); color: color-mix(in srgb, currentColor 75%, var(--brand-text)); }
      .continue-icon { font-size: 1.75rem; line-height: 1; flex-shrink: 0; }

      /* The save-only guard on Continue */
      .continue-interstitial {
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
        padding: var(--space-3);
        border: 1px solid color-mix(in srgb, var(--bs-warning) 45%, transparent);
        border-left: 3px solid var(--bs-warning);
        border-radius: var(--radius-md);
        background: color-mix(in srgb, var(--bs-warning) 8%, var(--brand-surface));
      }

      .ci-head {
        display: flex;
        align-items: baseline;
        gap: var(--space-2);
        font-size: var(--font-size-sm);
        color: var(--brand-text);

        .bi { color: var(--bs-warning); }
      }

      .ci-names { margin: 0; padding-left: var(--space-6); font-size: var(--font-size-sm); color: var(--brand-text); }
      .ci-grad { margin-left: var(--space-2); font-size: var(--font-size-2xs); color: var(--brand-text-muted); }
      .ci-actions { display: flex; justify-content: flex-end; flex-wrap: wrap; gap: var(--space-2); }

      .btn-ci-leave,
      .btn-ci-register {
        display: inline-flex;
        align-items: center;
        gap: var(--space-2);
        padding: 5px var(--space-3);
        border-radius: var(--radius-sm);
        font-size: var(--font-size-sm);
        cursor: pointer;

        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
      }

      .btn-ci-leave {
        border: 1px solid var(--bs-border-color);
        background: transparent;
        color: var(--brand-text);

        &:hover { background: color-mix(in srgb, var(--bs-body-color) 5%, transparent); }
      }

      .btn-ci-register {
        border: 1px solid var(--bs-success);
        background: var(--bs-success);
        color: var(--neutral-0);
        font-weight: var(--font-weight-semibold);

        &:hover { filter: brightness(0.93); }
      }

      @media (prefers-reduced-motion: reduce) {
        .seg-tab, .continue-btn { transition: none !important; }
        .seg-tab:hover:not(.is-active) { transform: none; }
      }

      /* ── Section banner header ── */
      .section-header {
        display: flex;
        align-items: center;
        gap: var(--space-1);
        padding: var(--space-2) var(--space-3);
        font-size: 11px;
        font-weight: var(--font-weight-bold);
        letter-spacing: 0.04em;
        text-transform: uppercase;
      }

      .section-registered {
        color: var(--bs-success);
        background: rgba(var(--bs-success-rgb), 0.06);
        border-bottom: 1px solid rgba(var(--bs-success-rgb), 0.12);
      }

      /* Empty state: the section exists but nothing is done yet. */
      .section-pending {
        color: var(--brand-text-muted);
        background: color-mix(in srgb, var(--bs-body-color) 3%, transparent);
        border-bottom: 1px solid var(--bs-border-color);
      }

      /* .section-titlebar / .section-titlebar-* / .phase-badge live in
         styles/_wizard-globals.scss — see those files for shared chrome. */

      /* Compact action button nested inside a .section-header banner */
      .section-action {
        margin-left: auto;
        padding: 2px var(--space-2);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        text-transform: none;
        letter-spacing: 0;
      }

      /* ── Empty-state primary CTA ────────────────────────────────────
         Replaces the "tap above" pointer. Lives where the eye is. */
      .cta-empty {
        display: inline-flex;
        align-items: center;
        margin-top: var(--space-3);
        padding: var(--space-2) var(--space-5);
        font-weight: var(--font-weight-semibold);
        font-size: var(--font-size-base);
        box-shadow: var(--shadow-md);
        transition: transform 0.12s ease, box-shadow 0.12s ease;

        &:hover { transform: translateY(-1px); box-shadow: var(--shadow-lg); }
        &:active { transform: translateY(0); }
      }

      .cta-empty-arrow { transition: transform 0.18s ease; }
      .cta-empty:hover .cta-empty-arrow { transform: translateX(3px); }

      .cta-empty-wrap {
        align-items: center;
      }

      @media (prefers-reduced-motion: reduce) {
        .cta-empty,
        .cta-empty-arrow { transition: none !important; }
        .cta-empty:hover { transform: none; }
      }

      /* ── Mobile ── */
      @media (max-width: 575.98px) {
        .step-card-footer { padding: var(--space-2); }

        /* Two tabs stay side by side; the long event name wraps instead of the icons crowding it. */
        .seg-bar { margin: var(--space-2) var(--space-2) 0; }
        .seg-tab { gap: var(--space-2); padding: var(--space-2); }
        .seg-tab-icon { display: none; }
        .seg-tab-switch { display: none; }
        .seg-tab-title { font-size: var(--font-size-sm); }
        .seg-panel-body { padding: var(--space-2); }
        .continue-btn { padding: var(--space-3); }
        .action-segments { grid-template-columns: 1fr; }
        .action-segment + .action-segment { border-top: 1px solid var(--border-color); }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamTeamsStepComponent implements OnInit {
    readonly proceedToPayment = output<void>();

    private readonly state = inject(TeamWizardStateService);
    private readonly teamReg = inject(TeamRegistrationService);
    private readonly toast = inject(ToastService);
    private readonly jobService = inject(JobService);
    private readonly destroyRef = inject(DestroyRef);
    private readonly router = inject(Router);

    /** Clean event name with the org-prefix and colon stripped — same split as
        team.component.ts page hero, so child components see only the headline
        portion ("Carolina Clash 2026" not "Top Threat Tournaments:Carolina Clash 2026"). */
    readonly eventName = computed(() => {
        const raw = this.jobService.currentJob()?.jobName ?? 'this event';
        const idx = raw.indexOf(':');
        return idx > 0 ? raw.substring(idx + 1).trim() : raw;
    });

    /** Capability flags from job pulse — gate the Register/Remove/Edit UI controls. */
    readonly canRegisterTeam = this.state.canRegisterTeam;
    readonly canRemoveTeam = this.state.canRemoveTeam;
    readonly canEditTeam = this.state.canEditTeam;

    /** Job-config flag — the phase baseline (default before any team is entered). */
    readonly fullPaymentRequired = this.state.fullPaymentRequired;

    /**
     * Derived PER-ROW from the entered teams' server-resolved fullPaymentRequired, never from
     * the job flag alone. Rules (waitlist exclusion, deposit-less carts, the no-"Mixed" rule)
     * live in `@shared-ui/fees/cart-phase` — the director's accounting card reads the same
     * ones. The job baseline is passed because this screen knows it, so the label here is
     * never null.
     */
    readonly phaseBadgeLabel = computed(() => cartPhaseBadgeLabel(
        resolveCartPhase(this._registeredTeams()),
        this.state.fullPaymentRequired(),
    ));

    readonly loading = signal(true);
    readonly error = signal<string | null>(null);
    readonly clubName = signal('your club');
    readonly ageGroups = signal<AgeGroupDto[]>([]);
    readonly actionInProgress = signal(false);
    /** Plain library add — the Club Team Library tab's Add, open or closed (the tab is list-only: it never registers). */
    readonly showAddModal = signal(false);
    /** Combined add+register modal — the register modal's "Not in your library? Add a New Team" (a register context). */
    readonly showAddAndRegisterModal = signal(false);
    /** The Register-a-team modal — every registration of a library team goes through it. */
    readonly showRegisterModal = signal(false);
    /**
     * clubTeamIds saved to the library WITHOUT registering during this wizard session.
     * Pruned by `pendingLibraryOnly` as each one registers; cleared when the rep tells
     * the flyin's interstitial to leave them. Session-only on purpose — the strip under
     * the grid carries the durable, data-driven version of the same fact.
     */
    private readonly _pendingLibraryOnly = signal<ReadonlySet<number>>(new Set());
    /** Rep dismissed the unregistered-library strip for this session. */
    /** When set, the library edit modal is open for this (unscheduled) team. */
    readonly editingTeam = signal<ClubTeamDto | null>(null);
    /** When set, the shared name dialog is open — carrying which side it was opened from. */
    readonly pendingRename = signal<PendingRename | null>(null);
    /** Server refusal from the last rename attempt — shown inside the dialog, which stays open. */
    readonly renameError = signal<string | null>(null);
    /** When set, the delete-confirm dialog is open for this team. */
    readonly pendingDelete = signal<ClubTeamDto | null>(null);
    /** When set, the archive-confirm dialog is open for this team. */
    readonly pendingArchive = signal<ClubTeamDto | null>(null);
    /** When set, the restore-confirm dialog is open for this team. */
    readonly pendingRestore = signal<ClubTeamDto | null>(null);
    // RETIRED FLY-IN: open state. Opened only on explicit user action — never auto-opened.
    // readonly showLibraryFlyin = signal(false);

    /**
     * 'board' = library and Registered Teams side by side (Todd 2026-09-27). 'segments' = the two
     * tabs + Register-a-team modal, kept whole for a return — flip this one line.
     */
    readonly layout = signal<'board' | 'segments'>('board');

    // ── Segments ───────────────────────────────────────────────────────
    /** Which view is showing. Set ONCE by pickOpeningSegment when the first load lands, then only by the rep. */
    readonly segment = signal<TeamsSegment>('library');
    private openingSegmentChosen = false;
    /** Continue was pressed with library-only saves still unregistered — the footer asks first. */
    readonly confirmingContinue = signal(false);
    /** Bumped by "Register it now" so the Library segment scrolls to the pending rows even when already showing. */
    readonly revealPending = signal(0);

    private readonly _registeredTeams = signal<RegisteredTeamDto[]>([]);
    /** Teams a director moved into a "DROPPED" age group — read-only history fed
     *  straight to the library fly-in's muted Dropped section. */
    private readonly _droppedTeams = signal<RegisteredTeamDto[]>([]);
    private readonly _clubTeams = signal<ClubTeamDto[]>([]);

    readonly droppedTeams = computed(() => this._droppedTeams());

    /** All library teams. Backend now returns the full library in `clubTeams`
     *  (previously filtered to "not yet registered"); the registration map for
     *  this event is exposed separately via `enteredTeams`. */
    readonly allLibraryTeams = computed<ClubTeamDto[]>(() => this._clubTeams());

    /** Entered teams (registered for this event). */
    readonly enteredTeams = computed(() => this._registeredTeams());

    /**
     * Map of clubTeamId → this event's registration. (The retired fly-in took a slimmer
     * RegisteredInfo projection of this; re-enabling it means restoring that shape.)
     */
    readonly enteredTeamsMap = computed(() => {
        const map = new Map<number, RegisteredTeamDto>();
        for (const r of this._registeredTeams()) {
            if (r.clubTeamId != null) map.set(r.clubTeamId, r);
        }
        return map;
    });

    readonly anyPaid = computed(() => this._registeredTeams().some(t => t.paidTotal > 0));
    readonly anyOwed = computed(() => this._registeredTeams().some(t => t.owedTotal > 0));

    /** Session set minus anything that has since registered (or left the library). */
    readonly pendingLibraryOnly = computed<ReadonlySet<number>>(() => {
        const raw = this._pendingLibraryOnly();
        if (raw.size === 0) return raw;
        const entered = this.enteredTeamsMap();
        const inLibrary = new Set(this._clubTeams().map(t => t.clubTeamId));
        return new Set([...raw].filter(id => inLibrary.has(id) && !entered.has(id)));
    });

    /**
     * Active library teams not registered here. No age-group eligibility test: which age group a team
     * plays in is the rep's call (Todd 2026-09-27, "let the club reps decide").
     */
    readonly unregisteredTeams = computed<ClubTeamDto[]>(() => {
        const entered = this.enteredTeamsMap();
        return this._clubTeams()
            .filter(t => !t.bArchived && !entered.has(t.clubTeamId))
            .sort((a, b) => a.clubTeamName.localeCompare(b.clubTeamName));
    });

    /** The segment bar shows once there is anything to switch between: a library team or a registration. */
    readonly showSegments = computed(() => this._clubTeams().length > 0 || this._registeredTeams().length > 0);

    /** Active (not archived) library teams. */
    readonly activeLibraryCount = computed(() => this._clubTeams().filter(t => !t.bArchived).length);

    /** What the rep owes today — the grid footer's own rule (sumDueNowOf), never a second copy. */
    readonly dueNow = computed(() => sumDueNowOf(this._registeredTeams()));

    /** The Club Team Library tab's second line. */
    readonly librarySegmentSub = computed(() => {
        const active = this.activeLibraryCount();
        if (active === 0) return 'Start here: add your first team';
        const teams = `${active} ${active === 1 ? 'team' : 'teams'}`;
        const ready = this.unregisteredTeams().length;
        return this.canRegisterTeam() && ready > 0 ? `${teams} · ${ready} ready to register` : teams;
    });

    /** Library-only saves from this session, resolved to rows — what the Continue guard names. */
    readonly pendingTeams = computed<ClubTeamDto[]>(() => {
        const pending = this.pendingLibraryOnly();
        if (pending.size === 0) return [];
        return this._clubTeams()
            .filter(t => pending.has(t.clubTeamId))
            .sort((a, b) => a.clubTeamName.localeCompare(b.clubTeamName));
    });

    ngOnInit(): void {
        this.loadTeamsMetadata(true);
        // The undo clock: re-read every 15s so "undo · N min" counts down and the trash can
        // disappears when the window closes. Only ticks while there is a window to watch.
        interval(15000).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
            if (this.undoDeadlines().size > 0) this.clock.set(Date.now());
        });
    }

    // ── Mistake-undo (TeamRegistrationUndo, 2026-09-26) ─────────────────

    /** teamId → deadline (epoch ms). Server seconds-left + the local receipt time: never the rep's clock alone. */
    readonly undoDeadlines = signal<ReadonlyMap<string, number>>(new Map());
    readonly clock = signal(Date.now());

    /** Undo is registration's own undo: only while the event takes team registrations (server: CanAddTeam). */
    readonly gridUndoDeadlines = computed<ReadonlyMap<string, number>>(() =>
        this.canRegisterTeam() ? this.undoDeadlines() : new Map());

    private stampUndoDeadlines(teams: readonly RegisteredTeamDto[]): void {
        const received = Date.now();
        const map = new Map<string, number>();
        for (const t of teams) if (t.undoSecondsLeft > 0) map.set(t.teamId, received + t.undoSecondsLeft * 1000);
        this.undoDeadlines.set(map);
        this.clock.set(received);
    }

    // ── Segments ───────────────────────────────────────────────────────

    /**
     * The segment that opens — a SIMPLE BINARY (Todd 2026-09-26):
     *
     *   no teams registered here → Library. Getting the Club Team Library right is priority #1;
     *                              an empty library is just what that tab shows, not a third state.
     *   teams registered here    → Registered Teams. Assume they don't need the library; it is one
     *                              tab away "prn".
     *
     * There is no reliable way to tell a legacy veteran new to the library from anyone else (the
     * population script back-filled every library and history signal), so the rule keys on the
     * one hard fact this event has.
     */
    private pickOpeningSegment(): TeamsSegment {
        return this._registeredTeams().length === 0 ? 'library' : 'registered';
    }

    selectSegment(segment: TeamsSegment): void {
        this.confirmingContinue.set(false);
        this.segment.set(segment);
    }

    /** Arrow keys move between the two tabs (WAI-ARIA tabs pattern). */
    onSegmentKey(event: KeyboardEvent): void {
        if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight' && event.key !== 'Home' && event.key !== 'End') return;
        event.preventDefault();
        // Tab order: Registered Teams first, Club Team Library second (Todd 2026-09-26).
        const next: TeamsSegment = event.key === 'Home' ? 'registered'
            : event.key === 'End' ? 'library'
            : this.segment() === 'library' ? 'registered' : 'library';
        this.selectSegment(next);
        document.getElementById(next === 'library' ? 'teams-seg-tab-library' : 'teams-seg-tab-registered')?.focus();
    }

    // ── Continue (outside the segments) ────────────────────────────────

    /**
     * The P0 guard the fly-in's Done carried: a team saved to the library this session and still
     * not registered here stops Continue once, by name. Otherwise straight on.
     */
    onContinue(): void {
        if (this.pendingTeams().length > 0) {
            this.confirmingContinue.set(true);
            return;
        }
        this.proceedToPayment.emit();
    }

    /** "Continue without it": the rep has been told and chose; stop asking this session. */
    continueWithoutPending(): void {
        this.confirmingContinue.set(false);
        this.clearPendingLibraryOnly();
        this.proceedToPayment.emit();
    }

    /** "Register it now": the Register-a-team modal, where the pending teams are waiting with their Register buttons. */
    registerPendingNow(): void {
        this.confirmingContinue.set(false);
        // On the board the pending teams are already in view, edged amber, each with its Register.
        if (this.layout() === 'segments') this.openRegisterModal();
    }

    // ── Register a team (the modal — the one register path) ──────────────

    openRegisterModal(): void {
        if (!this.canRegisterTeam()) return;
        this.showRegisterModal.set(true);
    }

    /** "Not in your library? Add a New Team": one modal at a time. */
    onRegisterModalAddNew(): void {
        this.showRegisterModal.set(false);
        this.onAddNew();
    }

    /** "Open Club Team Library": fix a name / grad year / archive there, come back via Register Another Team. */
    onRegisterModalOpenLibrary(): void {
        this.showRegisterModal.set(false);
        this.selectSegment('library');
    }

    isEnteredTeam(clubTeamId: number): boolean {
        return this._registeredTeams().some(r => r.clubTeamId === clubTeamId);
    }

    getEnteredInfo(clubTeamId: number): RegisteredTeamDto | null {
        return this._registeredTeams().find(r => r.clubTeamId === clubTeamId) ?? null;
    }

    // RETIRED FLY-IN: open/close, and its header door to the standalone library page.
    // openLibraryFlyin(): void { this.showLibraryFlyin.set(true); }
    // closeLibraryFlyin(): void { this.showLibraryFlyin.set(false); }
    // goToLibraryPage(): void {
    //     const jobPath = this.state.jobPath();
    //     if (!jobPath) return;
    //     this.showLibraryFlyin.set(false);
    //     this.router.navigateByUrl(`/${jobPath}/club/library`);
    // }

    /**
     * One team from the Club Team Library segment — its one-press Register, or its editor.
     * The LOP rides as the EVENT's level of play; the library row keeps its own.
     */
    onLibraryRegister(req: LibraryRegisterRequest): void {
        // A ClubTeamDto-compatible object carrying the picked LOP, so registerTeamForEvent gets the
        // rep's value rather than the library team's stored default.
        const team: ClubTeamDto = {
            ...req.team,
            clubTeamLevelOfPlay: req.levelOfPlay || req.team.clubTeamLevelOfPlay,
        };
        this.onSelectAgeGroup(team, req.ageGroupId);
    }

    /**
     * "Register all": one POST per team, strictly in order (each one can fill an age group the
     * next is heading for, and the server decides the waitlist per call). The first refusal
     * stops the run — a closed event or a director toggle would refuse every call after it, and
     * the interceptor has already toasted the reason once. One reload and one summary at the end.
     */
    onLibraryRegisterMany(reqs: LibraryRegisterRequest[]): void {
        if (reqs.length === 0 || this.actionInProgress()) return;
        this.actionInProgress.set(true);

        let registered = 0;
        let waitlisted = 0;
        let stopped = false;

        from(reqs).pipe(
            concatMap(req => defer(() => stopped
                ? EMPTY
                : this.teamReg.registerTeamForEvent({
                    clubTeamId: req.team.clubTeamId,
                    ageGroupId: req.ageGroupId,
                    teamName: req.team.clubTeamName,
                    clubTeamGradYear: req.team.clubTeamGradYear,
                    levelOfPlay: req.levelOfPlay || req.team.clubTeamLevelOfPlay || undefined,
                }).pipe(
                    map(resp => (resp.isWaitlisted ? 'waitlisted' : 'registered') as 'waitlisted' | 'registered'),
                    catchError(() => { stopped = true; return of('failed' as const); }),
                ))),
            takeUntilDestroyed(this.destroyRef),
        ).subscribe({
            next: outcome => {
                if (outcome === 'registered') registered++;
                else if (outcome === 'waitlisted') waitlisted++;
            },
            complete: () => {
                const done = registered + waitlisted;
                const notTried = reqs.length - done - (stopped ? 1 : 0);
                const parts: string[] = [];
                if (registered) parts.push(`${registered} ${registered === 1 ? 'team' : 'teams'} registered for ${this.eventName()}`);
                if (waitlisted) parts.push(`${waitlisted} waitlisted`);
                if (stopped) parts.push(`stopped after a refusal${notTried > 0 ? `, ${notTried} not tried` : ''}`);
                const msg = parts.length ? parts.join(' · ') + '.' : 'No teams were registered.';
                const tone = stopped || done === 0 ? 'warning' : waitlisted ? 'warning' : 'success';
                this.loadTeamsMetadata(false, () => this.toast.show(msg, tone, 5000));
            },
        });
    }

    /** Register (or re-register) a team with the selected age group. */
    onSelectAgeGroup(team: ClubTeamDto, ageGroupId: string): void {
        const existing = this.getEnteredInfo(team.clubTeamId);

        // If already registered with the SAME age group AND the same LOP, no-op.
        // (Compare LOP too — an edit that changes only the Level of Play keeps the
        // age group, and must still go through the unregister/re-register path.)
        if (existing
            && existing.ageGroupId === ageGroupId
            && (existing.levelOfPlay ?? '') === (team.clubTeamLevelOfPlay ?? '')) {
            return;
        }

        this.actionInProgress.set(true);

        const doRegister = () => {
            this.teamReg.registerTeamForEvent({
                clubTeamId: team.clubTeamId,
                ageGroupId,
                teamName: team.clubTeamName,
                clubTeamGradYear: team.clubTeamGradYear,
                levelOfPlay: team.clubTeamLevelOfPlay || undefined,
            })
                .pipe(takeUntilDestroyed(this.destroyRef))
                .subscribe({
                    next: (resp) => {
                        // A rejected registration never lands here: the API answers it with
                        // HTTP 400, which HttpClient routes to `error:` below.
                        const msg = resp.isWaitlisted
                            ? `${team.clubTeamName} waitlisted for ${this.stripWaitlistPrefix(resp.waitlistAgegroupName)}`
                            : `${team.clubTeamName} registered for ${this.eventName()}.`;
                        // The segment stays put: the row turns "Registered in {age group}" in place and
                        // the Registered Teams tab's count ticks, so the rep can keep working down the list.
                        this.loadTeamsMetadata(false, () =>
                            this.toast.show(msg, resp.isWaitlisted ? 'warning' : 'success', 3000));
                    },
                    error: () => {
                        // The global interceptor has already toasted the server's reason for any
                        // 4xx/5xx; a second generic toast here would only bury it. Reload so the
                        // fly-in reflects whatever state the server actually landed on.
                        this.loadTeamsMetadata();
                    },
                });
        };

        // If changing age group, unregister first then re-register
        if (existing) {
            this.teamReg.unregisterTeamFromEvent(existing.teamId)
                .pipe(takeUntilDestroyed(this.destroyRef))
                .subscribe({ next: doRegister, error: (err: unknown) => {
                    // Surface the server's reason, as the delete/archive/restore
                    // branches do. The refusals here are specific and actionable
                    // ("contact support for refunds", "event is closed") and the old
                    // hardcoded string named the age group even on an edit that only
                    // touched Level of Play.
                    const httpErr = err as { error?: { message?: string } };
                    this.toast.show(
                        httpErr?.error?.message || 'Failed to update this registration.',
                        'danger', 5000);
                    this.loadTeamsMetadata();
                }});
        } else {
            doRegister();
        }
    }

    /**
     * The register modal's "Not in your library? Add a New Team" → the combined modal whenever there
     * is an event to register into (the rep is registering). The plain library form only when
     * registration is closed. The Library tab's own Add is onAddLibraryTeam, never this.
     */
    onAddNew(): void {
        if (this.canRegisterTeam()) this.showAddAndRegisterModal.set(true);
        else this.showAddModal.set(true);
    }

    /**
     * The Club Team Library tab's "Add a New Team" / "Add Your First Team": a LIBRARY add, never a
     * registration (Todd 2026-09-26). A new club builds its list first, then registers from the
     * Register-a-team modal — the same order as every other rep.
     */
    onAddLibraryTeam(): void {
        this.showAddModal.set(true);
    }

    onTeamAdded(): void {
        this.showAddModal.set(false);
        this.loadTeamsMetadata();
    }

    /** Combined add+register modal succeeded — close it and refresh state. */
    onAddAndRegisterSaved(): void {
        this.showAddAndRegisterModal.set(false);
        this.loadTeamsMetadata();
    }

    /**
     * The modal's secondary path. The team is in the library and NOT in the event;
     * track it so the flyin marks the row and holds the door on Done. The modal has
     * already toasted (warning-toned, naming the event it did not register for).
     */
    onSavedLibraryOnly(team: ClubTeamDto): void {
        this.showAddAndRegisterModal.set(false);
        this._pendingLibraryOnly.set(new Set([...this._pendingLibraryOnly(), team.clubTeamId]));
        this.loadTeamsMetadata();
    }

    /** Interstitial "leave it": the rep has been told and chose; stop asking this session. */
    clearPendingLibraryOnly(): void {
        this._pendingLibraryOnly.set(new Set());
    }

    /** Last-line guard behind the fly-in's edit emit: the SAME shared rule the fly-in's menu applied. */
    openEditModal(team: ClubTeamDto): void {
        if (clubTeamEditLockReason(team, this.lockContext(team))) return;
        this.editingTeam.set(team);
    }

    private lockContext(team: ClubTeamDto): ClubTeamLockContext {
        return { registeredHere: this.isEnteredTeam(team.clubTeamId), eventLabel: 'this event' };
    }
    /** For the edit modal's "add as a new team instead" follow-up: may the OLD row be archived? */
    archiveLockReasonFor(team: ClubTeamDto): string | null { return clubTeamArchiveLockReason(this.lockContext(team)); }

    onTeamEdited(): void {
        this.editingTeam.set(null);
        this.loadTeamsMetadata();
    }

    // ── This-event rename (Registered Teams pencil) ─────────────────
    // A registered team's name is renamed HERE, for this event's copy only — never in the
    // library. The write goes to the canonical name writer (ThisJob): this job's Teams row +
    // WAITLIST twin + this job's schedule. The library and every other event keep their name.

    onRenameTeam(team: RegisteredTeamDto): void {
        this.renameError.set(null);
        this.pendingRename.set({ origin: 'event', team });
    }

    /** Close the dialog and drop any refusal it was showing. */
    closeRename(): void {
        this.pendingRename.set(null);
        this.renameError.set(null);
    }

    /** This event's copy name. */
    renameEventName(p: PendingRename): string {
        return p.team.teamName;
    }

    /** What seeds the editable field. */
    renameSeed(p: PendingRename): string {
        return p.team.teamName;
    }

    /** This event's stored LOP for the dialog (AR-030) — the registered row's, never the library's. */
    renameLevelOfPlay(p: PendingRename): string | null {
        return p.team.levelOfPlay ?? null;
    }

    /** Library name — looked up from the library the step already holds; null for an orphan. */
    renameLibraryName(p: PendingRename): string | null {
        if (p.team.clubTeamId == null) return null;
        return this._clubTeams().find(c => c.clubTeamId === p.team.clubTeamId)?.clubTeamName ?? null;
    }

    /** Always: the pencil only exists on a registered row. */
    renameRegisteredHere(_p: PendingRename): boolean {
        return true;
    }

    confirmRename(c: TeamRenameConfirmation): void {
        const pending = this.pendingRename();
        if (!pending) return;
        // The dialog stays open across the call. A refusal — a library-name collision is the
        // realistic one — comes back into it, so the rep fixes the name they typed instead of
        // watching it vanish behind a toast. Only success closes it.
        this.renameError.set(null);
        this.actionInProgress.set(true);

        // LOP rides with the event rename (AR-030, THIS EVENT ONLY). alsoPropagate carries the new
        // name back into the library — the one direction that exists (Todd 2026-09-24).
        const call$ = this.teamReg.renameRegisteredTeam(pending.team.teamId, c.name, c.alsoPropagate, c.levelOfPlay);

        const oldName = pending.team.teamName;
        const where = c.alsoPropagate ? 'in this event and your Club Team Library' : 'in this event';

        // AR-030: an LOP-only edit leaves the name alone, and "X is now X in this event." reads as a
        // no-op the rep will not trust. Report what actually moved.
        const nameChanged = oldName !== c.name;
        const message = nameChanged
            ? `${oldName} is now ${c.name} ${where}.`
                + (c.levelOfPlay ? ` Level of play set to ${c.levelOfPlay} for this event.` : '')
            : `${oldName}: level of play set to ${c.levelOfPlay} for this event.`;

        call$.pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.closeRename();
                    this.loadTeamsMetadata(false, () =>
                        this.toast.show(message, 'success', 3000));
                },
                error: (err: unknown) => {
                    this.actionInProgress.set(false);
                    this.renameError.set(extractHttpErrorMessage(err, 'Failed to rename team.'));
                },
            });
    }

    /** Last-line guard behind the fly-in's delete emit: the SAME shared rule (any event reference blocks it). */
    askDeleteTeam(team: ClubTeamDto): void {
        if (clubTeamDeleteLockReason(team, this.lockContext(team))) return;
        this.pendingDelete.set(team);
    }

    confirmDelete(): void {
        const team = this.pendingDelete();
        if (!team) return;
        this.pendingDelete.set(null);
        this.actionInProgress.set(true);

        this.teamReg.deleteClubTeam(team.clubTeamId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.loadTeamsMetadata(false, () =>
                        this.toast.show(`${team.clubTeamName} deleted from library.`, 'success', 3000));
                },
                error: (err: unknown) => {
                    this.actionInProgress.set(false);
                    const httpErr = err as { error?: { message?: string } };
                    this.toast.show(httpErr?.error?.message || 'Failed to delete team.', 'danger', 5000);
                },
            });
    }

    cancelDelete(): void {
        this.pendingDelete.set(null);
    }

    /** Archive is a visibility flag — the shared rule (registered for THIS event) is the only lock. */
    askArchiveTeam(team: ClubTeamDto): void {
        if (clubTeamArchiveLockReason(this.lockContext(team))) return;
        this.pendingArchive.set(team);
    }

    confirmArchive(): void {
        const team = this.pendingArchive();
        if (!team) return;
        this.pendingArchive.set(null);
        this.actionInProgress.set(true);

        this.teamReg.archiveClubTeam(team.clubTeamId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.loadTeamsMetadata(false, () =>
                        this.toast.show(`${team.clubTeamName} archived from library.`, 'success', 3000));
                },
                error: (err: unknown) => {
                    this.actionInProgress.set(false);
                    const httpErr = err as { error?: { message?: string } };
                    this.toast.show(httpErr?.error?.message || 'Failed to archive team.', 'danger', 5000);
                },
            });
    }

    cancelArchive(): void {
        this.pendingArchive.set(null);
    }

    askRestoreTeam(team: ClubTeamDto): void {
        if (!team.bArchived) return;
        this.pendingRestore.set(team);
    }

    confirmRestore(): void {
        const team = this.pendingRestore();
        if (!team) return;
        this.pendingRestore.set(null);
        this.actionInProgress.set(true);

        this.teamReg.unarchiveClubTeam(team.clubTeamId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.loadTeamsMetadata(false, () =>
                        this.toast.show(`${team.clubTeamName} restored to library.`, 'success', 3000));
                },
                error: (err: unknown) => {
                    this.actionInProgress.set(false);
                    const httpErr = err as { error?: { message?: string } };
                    this.toast.show(httpErr?.error?.message || 'Failed to restore team.', 'danger', 5000);
                },
            });
    }

    cancelRestore(): void {
        this.pendingRestore.set(null);
    }

    pendingRemove = signal<RegisteredTeamDto | null>(null);

    onRemoveTeam(team: RegisteredTeamDto): void {
        if (team.paidTotal > 0) return;
        this.pendingRemove.set(team);
    }

    /**
     * Remove trash can on the flyin's Registered strip. The flyin emits a
     * clubTeamId only; the registration DTO is resolved here so the paid-total
     * guard and the confirm dialog stay on the single path the teams grid already
     * uses — the flyin never gets its own removal route.
     */
    // RETIRED FLY-IN: its Registered strip's trash can.
    // onFlyinUnregister(clubTeamId: number): void {
    //     const team = this.getEnteredInfo(clubTeamId);
    //     if (team) this.onRemoveTeam(team);
    // }

    confirmRemove(): void {
        const team = this.pendingRemove();
        if (!team) return;
        this.pendingRemove.set(null);
        this.unregisterTeam(team.teamId, team.teamName);
    }

    cancelRemove(): void {
        this.pendingRemove.set(null);
    }

    /** Names what the removal gives back: the age-group place, or the waitlist spot. */
    removeMessage(team: RegisteredTeamDto): string {
        const ag = this.stripWaitlistPrefix(team.ageGroupDisplayName || team.ageGroupName);
        const frees = team.isWaitlisted
            ? `It comes off the ${ag} waitlist.`
            : `Its place in ${ag} opens up for another team.`;
        return `Remove <strong>${team.teamName}</strong> from ${this.eventName()}? ${frees}`;
    }

    // ── Private ─────────────────────────────────────────────────────

    /**
     * Drop the WAITLIST twin's name prefix for display. The server returns the
     * twin's stored name ("WAITLIST - 2029/2030") and the toast already says the
     * team was "waitlisted", so the raw name read "waitlisted for WAITLIST -
     * 2029/2030". Display-only — the stored name is never rewritten.
     *
     * The name is optional on the response, so a missing/prefix-only value falls
     * back to a phrase that still reads as a sentence.
     */
    private stripWaitlistPrefix(name: string | null | undefined): string {
        return (name ?? '').replace(/^\s*WAITLIST\s*-\s*/i, '').trim() || 'the waitlist';
    }

    private unregisterTeam(teamId: string, teamName: string): void {
        this.actionInProgress.set(true);

        this.teamReg.unregisterTeamFromEvent(teamId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    // actionInProgress is released by loadTeamsMetadata, not here —
                    // see its comment. Clearing it now re-enables the trash can while
                    // the removed team is still on screen. The toast rides along with
                    // the reload for the same reason: it asserts a state change, so it
                    // must not appear while the old state is still rendered.
                    this.loadTeamsMetadata(false, () =>
                        this.toast.show(`${teamName} removed from event.`, 'success', 3000));
                },
                error: () => {
                    // The global interceptor has already toasted the server's reason (e.g. the undo
                    // window closed); a generic second toast here would bury it.
                    this.loadTeamsMetadata();
                },
            });
    }

    /**
     * Reload the step's data. ALSO releases actionInProgress, in both branches.
     *
     * Mutations (register / remove / archive / delete / restore) used to clear the
     * flag the instant their own call returned and THEN kick this refetch, which
     * left a window where the success toast was already up, the removed team was
     * still on screen, and its controls were live again — clicking the same trash
     * can twice fired a second unregister against a registration that was already
     * gone, surfacing "Failed to remove team." on an action that had succeeded.
     * Holding the flag until the data lands closes that window.
     *
     * The three mutation ERROR branches that do not refetch still clear the flag
     * themselves — do not remove those, or the panel locks up.
     *
     * `onLoaded` runs after the new data is in the signals, for the same reason:
     * a success toast asserts a state change, so it must not fire while the old
     * state is still rendered. It is deliberately NOT called on the error branch.
     */
    private loadTeamsMetadata(showSpinner = false, onLoaded?: () => void): void {
        if (showSpinner) this.loading.set(true);
        this.error.set(null);

        this.teamReg.getTeamsMetadata()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (meta: TeamsMetadataResponse) => {
                    this.loading.set(false);
                    this.actionInProgress.set(false);
                    this.clubName.set(meta.clubName || 'your club');
                    this._registeredTeams.set(meta.registeredTeams || []);
                    this.stampUndoDeadlines(meta.registeredTeams || []);
                    this._droppedTeams.set(meta.droppedTeams || []);
                    this._clubTeams.set(meta.clubTeams || []);
                    this.ageGroups.set(meta.ageGroups || []);
                    this.state.applyTeamsMetadata(meta);
                    // Once, on the first landing — never again, or a registration mid-visit would
                    // move the rep off the list they are working.
                    if (!this.openingSegmentChosen) {
                        this.openingSegmentChosen = true;
                        this.segment.set(this.pickOpeningSegment());
                    }
                    onLoaded?.();
                },
                error: () => {
                    this.loading.set(false);
                    this.actionInProgress.set(false);
                    this.error.set('Failed to load team registration data.');
                },
            });
    }
}
