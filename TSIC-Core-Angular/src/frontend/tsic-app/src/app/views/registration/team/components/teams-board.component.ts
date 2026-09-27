import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import type { AgeGroupDto, ClubTeamDto, RegisteredTeamDto } from '@core/api';
import { formatLop } from '@shared/teams/lop-choices';
import { ToastService } from '@shared-ui/toast.service';
import { type LibraryRegisterRequest } from './library-segment.types';
import { boardFeeStatusOf, sumFeeDueNowOf, sumPaidOf, type BoardFeeStatus } from './board-money';
import { contrastText } from '../../../scheduling/shared/utils/scheduling-helpers';
import { LibraryPanelComponent } from './library-panel.component';
import { RegisteredTeamInlineEditorComponent } from './registered-team-inline-editor.component';

/**
 * The Teams step as ONE board, two panels side by side (Todd 2026-09-27 — "like configure pools
 * and roster swapper … NO chance for confusion there"):
 *
 *   LEFT  — Club Team Library: the shared LibraryPanelComponent (the library page shows the same
 *           one). Every write on this side is to the LIBRARY (future events). Register → opens a
 *           small editor on the row (level + age group, preselected); confirming moves the team across.
 *   RIGHT — {event} Registered Teams: what this event holds and what each team owes. The pencil
 *           edits THIS EVENT's record; Undo / remove take a team back off.
 *
 * A team is on exactly one side, so "which list am I editing?" answers itself. One open row editor
 * across both sides. Owns no domain state: the Teams step feeds rows and runs every mutation.
 */
@Component({
    selector: 'app-teams-board',
    standalone: true,
    imports: [CurrencyPipe, DatePipe, LibraryPanelComponent, RegisteredTeamInlineEditorComponent],
    template: `
    <div class="board">

      <!-- ═══ LEFT: Club Team Library — the shared panel, the same one the library page shows ═══ -->
      <app-library-panel
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

      <!-- ═══ RIGHT: this event's Registered Teams ═══ -->
      <!-- The event is named in the page title right above — here the side only has to say what it holds. -->
      <header class="board-head board-head--reg">
        <div class="board-head-text">
          <h3 class="board-title" id="board-reg-title" [attr.title]="'Registered for ' + eventName()">
            <i class="bi bi-clipboard-check-fill" aria-hidden="true"></i>Registered Teams
          </h3>
          <!-- A stat strip (Todd 2026-09-27): Teams · Paid · Due now, read at a glance. No phase chip —
               "Final Balance Due" names a later stage and read as owed-now. -->
          @if (registeredRows().length === 0) {
            <span class="board-sub">None yet for this event</span>
          } @else {
            <span class="board-sub board-stats">
              <span class="stat"><span class="stat-key">Teams</span><span class="stat-val">{{ registeredRows().length }}</span></span>
              <!-- Money RECEIVED, honestly: what left the club's account, card fees included — the
                   treasurer's number. Rows show the fee; this says "paid", not "fees". -->
              <span class="stat"><span class="stat-key">Paid</span><span class="stat-val">{{ paidTotal() | currency }}</span></span>
              <!-- The SAME sum as the Continue card (fee only). Always shown — $0.00 is an answer. -->
              <span class="stat" [class.is-owed]="dueNow() > 0">
                <span class="stat-key">Due now</span><span class="stat-val">{{ dueNow() | currency }}</span>
              </span>
            </span>
          }
        </div>
      </header>

      <section class="panel panel--reg" aria-labelledby="board-reg-title">
        <div class="panel-body" [class.is-short]="registeredRows().length <= 3">
          @for (t of registeredRows(); track t.teamId) {
            @let s = feeStatus(t);
            @let undoMin = undoMinutesLeft(t.teamId);
            @let removable = canRemove() && t.paidTotal === 0;
            <div class="reg-row" [class.is-open]="renameId() === t.teamId">
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
                <!-- Name and registered age group on ONE line (Todd 2026-09-27) — a team's name is not
                     its placement, so they are never read apart. Same words as the library side. -->
                <span class="row-name-line">
                  <span class="row-name" [attr.title]="t.teamName">{{ t.teamName }}</span>
                  <!-- The age group as a badge in ITS color (Todd 2026-09-27) — the same chip the director's
                       screens use. A WAITLIST age group is an age group like any other: its own name, its
                       own color, no special casing. -->
                  <span class="reg-in">registered in
                    <span class="ag-badge" [style.background]="agBg(t.ageGroupColor)" [style.color]="agText(t.ageGroupColor)">{{ t.ageGroupName }}</span>
                  </span>
                </span>
                <!-- Line 2: facts left, actions flush right (Todd 2026-09-27) — line 1 keeps the whole
                     width for the name and its age group, so it doesn't wrap. -->
                <span class="row-line2">
                <span class="row-meta">
                  <span class="meta-pair"><span class="meta-key">LOP</span>{{ formatLop(t.levelOfPlay) || '—' }}</span>
                <span class="fee" [class]="'fee fee--' + s.kind">
                  @switch (s.kind) {
                    @case ('waitlist') { <i class="bi bi-dash-circle" aria-hidden="true"></i>No fees while on waitlist }
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
                <span class="row-actions">
                  <button type="button" class="btn-icon" [class.is-locked]="!!renameLockReason()"
                          [disabled]="actionInProgress()"
                          [attr.aria-disabled]="!!renameLockReason()"
                          [attr.aria-label]="'Edit the registration of ' + t.teamName"
                          [attr.title]="renameLockReason() ?? 'Edit the registration of ' + t.teamName + ' for ' + eventName() + ' (name, level of play). Your Club Team Library is not changed.'"
                          (click)="renameLockReason() ? explainLock(renameLockReason()!) : startRename(t)">
                    <i class="bi bi-pencil" aria-hidden="true"></i>
                  </button>
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
                </span>
                </span>
              </div>
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
    `,
    styleUrl: './teams-board.shared.scss',
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamsBoardComponent {
    private readonly toast = inject(ToastService);
    private readonly libraryPanel = viewChild(LibraryPanelComponent);

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

    /** The registered row being edited inline (teamId). */
    readonly renameId = signal<string | null>(null);

    readonly activeCount = computed(() => this.clubTeams().filter(t => !t.bArchived).length);
    readonly empty = computed(() => this.clubTeams().length === 0);

    /** This event's teams, by age group then name — the shape a director reads them in. */
    readonly registeredRows = computed(() => [...this.registeredTeams()].sort((a, b) =>
        (a.ageGroupName ?? '').localeCompare(b.ageGroupName ?? '') || a.teamName.localeCompare(b.teamName)));

    /** Age-group badge: the age group's own color, text picked for contrast (the scheduling helper). */
    agBg(color: string | null | undefined): string { return color || 'var(--bs-secondary-bg)'; }
    agText(color: string | null | undefined): string { return contrastText(color); }

    readonly paidTotal = computed(() => sumPaidOf(this.registeredTeams()));
    readonly dueNow = computed(() => sumFeeDueNowOf(this.registeredTeams()));

    feeStatus(t: RegisteredTeamDto): BoardFeeStatus { return boardFeeStatusOf(t); }

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
