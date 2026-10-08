import { ChangeDetectionStrategy, Component, ElementRef, Injector, afterNextRender, computed, inject, input, output, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import type { AgeGroupDto, ClubTeamDto, RegisteredTeamDto } from '@core/api';
import { LOP_CHOICES, formatLop, normalizeLop } from '@shared/teams/lop-choices';
import { clubTeamArchiveLockReason, clubTeamEditLockReason, clubTeamRemoval, type ClubTeamRemoval } from '@shared/teams/club-team-locks';
import { ToastService } from '@shared-ui/toast.service';
import { resolveRecommendedAgeGroupId } from './event-age-group.util';
import { ageGroupLabel, type LibraryRegisterRequest } from './library-segment.types';
import { ageGroupWaitlists, byGradYearThenName } from './library-register-plan';
import { contrastText } from '../../../scheduling/shared/utils/scheduling-helpers';
import { LibraryTeamInlineEditorComponent } from './library-team-inline-editor.component';

/** The open register editor's two picks. '' = no pick. */
interface RegPick { lop: string; ag: string; }

interface LibRow {
    team: ClubTeamDto;
    registered: RegisteredTeamDto | null;
}

/**
 * The Club Team Library panel — ONE component for both places a rep sees the library, so the two
 * can never drift (Ann/Todd 2026-09-27: "look identical to the teams tab, familiarity is important").
 *
 *   mode 'board' — the Teams step's left side. Three piles: Registered (collapsed, read-only, top),
 *                  Available Teams (open, with Register), Archived (collapsed, bottom).
 *   mode 'page'  — the standalone library page. ONE list of every active team, no Register and no
 *                  Registered pile; Archived kept. No event notation on a row (AR-152, 2026-10-08):
 *                  a team registered for the signed-in event shows only a greyed Archive, whose
 *                  tooltip and tap toast name the event.
 *
 * Host is display: contents, so on the board the header and the panel land in the board's grid
 * (headers level across both sides). Owns no domain state: the parent runs every mutation.
 */
@Component({
    selector: 'app-library-panel',
    standalone: true,
    imports: [NgTemplateOutlet, LibraryTeamInlineEditorComponent],
    template: `
      <!-- Headers sit ABOVE their panels, in one grid row, so both are always the same height and
           the panels start level (Todd 2026-09-27). -->
      <header class="board-head board-head--lib">
        <div class="board-head-text">
          <h3 class="board-title" id="board-lib-title">
            <i class="bi bi-card-list" aria-hidden="true"></i>Club Team Library
          </h3>
          <!-- CRITICAL (Todd 2026-09-27): says what this list IS — never drop it for the group labels. -->
          <!-- The page says WHEN they are chosen (Todd 2026-09-27): no Register button there to show it. -->
          <span class="board-sub">Your teams to choose from@if (isPage()) { when registering for an event}</span>
        </div>
        <!-- One header for every club, new or established (Todd 2026-09-27): no empty-state variant. -->
        <button type="button" class="btn-add" [disabled]="actionInProgress()" (click)="addNew.emit()">
          <i class="bi bi-plus-circle" aria-hidden="true"></i>Add Library Team
        </button>
      </header>

      <section class="panel panel--lib" [class.is-page]="isPage()" aria-labelledby="board-lib-title">
        <div class="panel-body">
          <!-- Board: three labelled piles (Todd 2026-09-27): Registered (collapsed, TOP — says where a
               team went before anyone scrolls to look), Available Teams (open), Archived (collapsed,
               bottom). Registered and Archived hide when empty; Available always shows. The page has
               no Registered pile: every active team is in one list (Ann 2026-09-27). -->
          @if (!isPage() && registeredLibRows().length > 0) {
            <button type="button" class="fold" [attr.aria-expanded]="showRegisteredLib()"
                    (click)="showRegisteredLib.set(!showRegisteredLib())">
              <i class="bi" [class.bi-chevron-down]="showRegisteredLib()" [class.bi-chevron-right]="!showRegisteredLib()" aria-hidden="true"></i>
              <i class="bi bi-clipboard-check fold-icon fold-icon--reg" aria-hidden="true"></i>Registered
              <span class="fold-count">{{ registeredLibRows().length }}</span>
            </button>
            @if (showRegisteredLib()) {
              <!-- READ-ONLY (Todd 2026-09-27): no icons. A second pencil beside the registered team's
                   pencil invited the wrong one, and a library edit here changes nothing on the right.
                   The library entry is editable again under Available Teams in the next event. -->
              @for (row of registeredLibRows(); track row.team.clubTeamId) {
                <div class="lib-row is-quiet">
                  <div class="row-main">
                    <div class="row-text">
                      <ng-container *ngTemplateOutlet="regNameLine; context: { $implicit: row }" />
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

          <!-- Opens on every load; a collapse lasts only while the rep is on this screen. -->
          <button type="button" class="fold" [attr.aria-expanded]="showAvailable()"
                  (click)="showAvailable.set(!showAvailable())">
            <i class="bi" [class.bi-chevron-down]="showAvailable()" [class.bi-chevron-right]="!showAvailable()" aria-hidden="true"></i>
            <i class="bi bi-card-list fold-icon fold-icon--lib" aria-hidden="true"></i>Available Teams
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
                  <!-- No event notation on the page (Ann/Todd 2026-10-08, AR-152): the library is a
                       neutral resource. A greyed Archive names the event in its tooltip and toast. -->
                  <span class="row-name" [attr.title]="team.clubTeamName">{{ team.clubTeamName }}</span>
                  <span class="row-meta">
                    <span class="meta-pair"><span class="meta-key">Grad</span>{{ team.clubTeamGradYear || '—' }}</span>
                    <span class="meta-pair"><span class="meta-key">LOP</span>{{ formatLop(team.clubTeamLevelOfPlay) || '—' }}</span>
                  </span>
                </div>
                <!-- Library housekeeping at the right, apart from the level it isn't about. -->
                <ng-container *ngTemplateOutlet="libActions; context: { $implicit: row }" />
                @if (!isPage() && canRegister() && !isOpen) {
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
                    } @else if (pickWaitlists(pick.ag)) {
                      <!-- No money on this side (Todd 2026-09-27) — the Payment step carries it. -->
                      Full &middot; joins the waitlist.
                    }
                  </span>
                  <div class="reg-actions">
                    <button type="button" class="btn-cancel" (click)="openId.set(null)">Cancel</button>
                    @let wl = pickWaitlists(pick.ag);
                    <button type="button" class="btn-reg" [class.btn-reg--wl]="wl"
                            [disabled]="actionInProgress() || !pick.lop || !pick.ag"
                            (click)="confirm(team, pick)">
                      <i class="bi" [class.bi-clipboard-check-fill]="!wl" [class.bi-pause-circle-fill]="wl" aria-hidden="true"></i>
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
              <i class="bi bi-archive fold-icon" aria-hidden="true"></i>Archived
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

    <!-- "{name} registered in [age group]" (Todd 2026-09-27) — points across to the Registered Teams side. -->
    <ng-template #regNameLine let-row>
      @let r = row.registered;
      <span class="row-name-line">
        <span class="row-name" [attr.title]="row.team.clubTeamName">{{ row.team.clubTeamName }}</span>
        <span class="reg-in" [attr.title]="'Registered for ' + eventName() + ' in ' + r.ageGroupName">registered in
          <span class="ag-badge" [style.background]="agBg(r.ageGroupColor)" [style.color]="agText(r.ageGroupColor)">{{ r.ageGroupName }}</span></span>
      </span>
    </ng-template>

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
    styleUrl: './teams-board.shared.scss',
    styles: [`
      /* The header and panel join the parent's layout: the board's grid, or the page's column. */
      :host { display: contents; }

      /* On its own page the list is the page: no inner scroll box, at any width. */
      .panel.is-page .panel-body { max-height: none; overflow-y: visible; }
      @media (max-width: 767.98px) {
        .panel--lib.is-page .panel-body { max-height: none; }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LibraryPanelComponent {
    private readonly toast = inject(ToastService);
    private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
    private readonly injector = inject(Injector);

    readonly mode = input<'board' | 'page'>('board');
    readonly clubTeams = input.required<readonly ClubTeamDto[]>();
    /** This event's registrations — which library rows are registered here. */
    readonly registeredTeams = input<readonly RegisteredTeamDto[]>([]);
    readonly ageGroups = input<readonly AgeGroupDto[]>([]);
    readonly eventName = input('this event');
    readonly clubName = input('');
    /** Team registration open AND the director allows adds. Board only. */
    readonly canRegister = input(false);
    readonly actionInProgress = input(false);
    readonly pendingLibraryOnly = input<ReadonlySet<number>>(new Set());

    readonly register = output<LibraryRegisterRequest>();
    readonly addNew = output<void>();
    /** A library edit saved (inline) — the parent reloads. */
    readonly librarySaved = output<void>();
    readonly archive = output<ClubTeamDto>();
    readonly delete = output<ClubTeamDto>();
    readonly restore = output<ClubTeamDto>();
    /** A row editor opened here — the board closes its own (one open row at a time). */
    readonly editorOpened = output<void>();

    readonly formatLop = formatLop;
    readonly lopChoices = LOP_CHOICES;
    readonly isPage = computed(() => this.mode() === 'page');

    readonly showRegisteredLib = signal(false);
    readonly showAvailable = signal(true);
    readonly showArchived = signal(false);
    /** The library row whose register editor is open — one at a time. */
    readonly openId = signal<number | null>(null);
    /** The library row being edited inline. One open row at a time: register and edit close each other. */
    readonly editId = signal<number | null>(null);
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

    /** Board: active and not registered here. Page: every active team. */
    readonly availableRows = computed(() => this.isPage()
        ? this.rows().filter(r => !r.team.bArchived)
        : this.rows().filter(r => !r.team.bArchived && !r.registered));
    readonly registeredLibRows = computed(() => this.rows().filter(r => !!r.registered));
    readonly archivedRows = computed(() => this.isPage()
        ? this.rows().filter(r => r.team.bArchived)
        : this.rows().filter(r => r.team.bArchived && !r.registered));
    readonly empty = computed(() => this.clubTeams().length === 0);

    /** Age-group badge: the age group's own color, text picked for contrast (the scheduling helper). */
    agBg(color: string | null | undefined): string { return color || 'var(--bs-secondary-bg)'; }
    agText(color: string | null | undefined): string { return contrastText(color); }

    /** The age group's name, no money (Todd 2026-09-27) — the Payment step carries it. A full
     *  group still says it waitlists: that is capacity, not money, and it decides the button. */
    readonly ageGroupOptions = computed(() => this.ageGroups().map(ag => {
        const label = ageGroupLabel(ag);
        return { id: ag.ageGroupId, text: ageGroupWaitlists(ag) ? `${label} · waitlist` : label };
    }));

    isPending(clubTeamId: number): boolean { return this.pendingLibraryOnly().has(clubTeamId); }

    // ── Locks: the shared rules, so the board and the library page never disagree ──
    editLockReason(team: ClubTeamDto): string | null {
        return clubTeamEditLockReason(team, { registeredHere: false, eventLabel: this.eventName() });
    }
    removalFor(row: LibRow): ClubTeamRemoval {
        return clubTeamRemoval(row.team, { registeredHere: !!row.registered, eventLabel: this.eventName() });
    }
    explainLock(reason: string): void { this.toast.show(reason, 'warning', 3500); }

    /** For the editor's add-as-new follow-up: may the OLD row be archived? */
    archiveLockReasonFor(row: LibRow): string | null {
        return clubTeamArchiveLockReason({ registeredHere: !!row.registered, eventLabel: this.eventName() });
    }

    /** The board opened a row editor of its own — close ours. */
    closeEditors(): void {
        this.openId.set(null);
        this.editId.set(null);
    }

    // ── Inline library edit ──
    startEdit(team: ClubTeamDto): void {
        this.openId.set(null);
        this.editId.set(team.clubTeamId);
        this.editorOpened.emit();
    }

    onLibrarySaved(): void {
        this.editId.set(null);
        this.librarySaved.emit();
    }

    // ── Register editor ──
    /** Opens seeded with the best guess: the library's level, and the age group the grad year names. */
    open(team: ClubTeamDto): void {
        this.pick.set({
            lop: normalizeLop(team.clubTeamLevelOfPlay),
            ag: resolveRecommendedAgeGroupId(this.ageGroups(), team.clubTeamGradYear),
        });
        this.editId.set(null);
        this.openId.set(team.clubTeamId);
        this.editorOpened.emit();
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

    confirm(team: ClubTeamDto, pick: RegPick): void {
        if (this.actionInProgress() || !pick.lop || !pick.ag) return;
        this.openId.set(null);
        this.register.emit({ team, ageGroupId: pick.ag, levelOfPlay: pick.lop });
    }
}
