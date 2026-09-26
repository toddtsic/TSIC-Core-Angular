import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { CurrencyPipe, NgTemplateOutlet } from '@angular/common';
import type { AgeGroupDto, ClubTeamDto, RegisteredTeamDto } from '@core/api';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { formatLop, normalizeLop } from '@shared/teams/lop-choices';
import { LevelOfPlayPickerComponent } from '@shared/teams/level-of-play-picker.component';
import { EventAgeGroupPickerComponent } from './event-age-group-picker.component';
import { resolveOldestOfferedGradYear, resolveRecommendedAgeGroupId } from './event-age-group.util';
import { ageGroupLabel, type LibraryRegisterRequest } from './library-segment.types';
import { ageGroupWaitlists, byGradYearThenName, fitsEvent, planRegistration, type RegisterPlan } from './library-register-plan';

interface ModalRow {
    team: ClubTeamDto;
    registered: RegisteredTeamDto | null;
    plan: RegisterPlan;
}

/**
 * Register a team — THE one place a club rep registers a team for this event (Todd 2026-09-26:
 * registering FROM the Club Team Library blurred the line between the club's list and an event
 * act; "restrict to our canonical add team modal"). The library tab is list-only; every Register
 * lives here.
 *
 * Covers the bases:
 *   - pick from the Club Team Library — one press when the answer is obvious ("Register in 2030"),
 *     an inline level-of-play + age-group choice when it is not;
 *   - the library doesn't cover it — "Add a New Team" (the add-and-register modal), or "Open Club
 *     Team Library" to fix a name / grad year / archive, then come back.
 *
 * Stays open across registrations: a registered row turns "Registered in 2030" IN PLACE (it does
 * not jump to the Already-registered fold until the modal is reopened), so a rep can bring three
 * teams in one sitting. Owns no domain state; the Teams step runs every registration.
 */
@Component({
    selector: 'app-register-team-modal',
    standalone: true,
    imports: [TsicDialogComponent, CurrencyPipe, NgTemplateOutlet, LevelOfPlayPickerComponent, EventAgeGroupPickerComponent],
    template: `
    <tsic-dialog [open]="true" size="lg" (requestClose)="closed.emit()">
      <div class="modal-content">
        <div class="modal-header">
          <h5 class="modal-title">
            <i class="bi bi-trophy-fill me-2 rtm-title-icon" aria-hidden="true"></i>Register a team for {{ eventName() }}
          </h5>
          <button type="button" class="btn-close" aria-label="Close" (click)="closed.emit()"></button>
        </div>

        <div class="modal-body rtm-body">
          <p class="rtm-lede">
            Pick from {{ clubPossessive() }} Club Team Library. Press <b>Register</b> on each team you're bringing;
            this stays open so you can register several.
          </p>

          @if (searchable()) {
            <div class="rtm-search">
              <i class="bi bi-search" aria-hidden="true"></i>
              <input type="search" class="rtm-search-input" placeholder="Find a team by name or grad year"
                     aria-label="Find a team by name or grad year"
                     [value]="query()" (input)="query.set($any($event.target).value)" />
            </div>
          }

          <ng-template #rowTpl let-row>
            @let team = row.team;
            @let plan = row.plan;
            @let editing = editingId() === team.clubTeamId;
            <div class="rtm-row" [class.is-done]="plan.kind === 'registered'" [class.is-editing]="editing">
              <div class="rtm-team">
                <span class="rtm-name" [attr.title]="team.clubTeamName">{{ team.clubTeamName }}</span>
                <span class="rtm-meta">
                  <span class="meta-pair"><span class="meta-key">Grad</span>{{ team.clubTeamGradYear || '—' }}</span>
                  <span class="meta-pair"><span class="meta-key">LOP</span>{{ formatLop(team.clubTeamLevelOfPlay) || '—' }}</span>
                </span>
              </div>

              <div class="rtm-action">
                @switch (plan.kind) {
                  @case ('registered') {
                    @let reg = row.registered!;
                    <span class="rtm-done" [class.rtm-done--wl]="reg?.isWaitlisted">
                      @if (reg?.isWaitlisted) {
                        <i class="bi bi-hourglass-split" aria-hidden="true"></i>On the {{ reg.ageGroupDisplayName || reg.ageGroupName }} waitlist
                      } @else {
                        <i class="bi bi-check-circle-fill" aria-hidden="true"></i>Registered in {{ reg?.ageGroupDisplayName || reg?.ageGroupName }}
                      }
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
                    <span class="rtm-sub">
                      @switch (ready.pricing.kind) {
                        @case ('waitlist') { {{ ready.ageGroupLabel }} is full &middot; no fee until placed }
                        @case ('free') { No fee }
                        @case ('deposit') { Deposit {{ ready.pricing.now | currency }} now &middot; {{ ready.pricing.total | currency }} total }
                        @case ('full') { {{ ready.pricing.total | currency }} }
                      }
                      &middot;
                      <button type="button" class="btn-change"
                              [disabled]="actionInProgress() || (editingId() !== null && !editing)"
                              (click)="openEditor(team)">change</button>
                    </span>
                  }
                  @case ('choose') {
                    <button type="button" class="btn-reg btn-reg--choose"
                            [disabled]="actionInProgress() || (editingId() !== null && !editing)"
                            (click)="openEditor(team)">
                      <i class="bi bi-ui-checks-grid" aria-hidden="true"></i>
                      {{ $any(plan).why === 'lop' ? 'Choose level & age group' : 'Choose age group' }}
                    </button>
                    <span class="rtm-sub">
                      @switch ($any(plan).why) {
                        @case ('lop') { No level of play saved on this team }
                        @case ('playUp') { No {{ team.clubTeamGradYear || 'matching' }} age group here &middot; pick one to play up in }
                        @case ('outside') { Older than every age group here }
                      }
                    </span>
                  }
                  @case ('closed') {
                    <span class="rtm-sub"><i class="bi bi-lock-fill" aria-hidden="true"></i> Registration closed</span>
                  }
                }
              </div>

              @if (editing) {
                <div class="reg-editor" role="group" [attr.aria-label]="'Register ' + team.clubTeamName">
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

          <div class="rtm-list">
            @for (row of availableRows(); track row.team.clubTeamId) {
              <ng-container *ngTemplateOutlet="rowTpl; context: { $implicit: row }" />
            } @empty {
              <p class="rtm-empty">
                @if (query().trim()) {
                  No team in your Club Team Library matches &ldquo;{{ query().trim() }}&rdquo;.
                } @else {
                  Every team in your Club Team Library that fits {{ eventName() }} is registered.
                }
              </p>
            }

            @if (outsideRows().length > 0) {
              <button type="button" class="rtm-fold" [attr.aria-expanded]="showOutside()" (click)="showOutside.set(!showOutside())">
                <i class="bi" [class.bi-chevron-down]="showOutside()" [class.bi-chevron-right]="!showOutside()" aria-hidden="true"></i>
                Older than every age group here
                <span class="rtm-fold-count">{{ outsideRows().length }}</span>
              </button>
              @if (showOutside()) {
                @for (row of outsideRows(); track row.team.clubTeamId) {
                  <ng-container *ngTemplateOutlet="rowTpl; context: { $implicit: row }" />
                }
              }
            }

            @if (alreadyRows().length > 0) {
              <button type="button" class="rtm-fold" [attr.aria-expanded]="showAlready()" (click)="showAlready.set(!showAlready())">
                <i class="bi" [class.bi-chevron-down]="showAlready()" [class.bi-chevron-right]="!showAlready()" aria-hidden="true"></i>
                Already registered for {{ eventName() }}
                <span class="rtm-fold-count">{{ alreadyRows().length }}</span>
              </button>
              @if (showAlready()) {
                @for (row of alreadyRows(); track row.team.clubTeamId) {
                  <ng-container *ngTemplateOutlet="rowTpl; context: { $implicit: row }" />
                }
              }
            }
          </div>
        </div>

        <!-- When the library doesn't cover it: two named ways out, then Done. -->
        <div class="modal-footer rtm-footer">
          <div class="rtm-escapes">
            <span class="rtm-escape">
              Not in your library?
              <button type="button" class="btn-link-inline" [disabled]="actionInProgress()" (click)="addNew.emit()">
                <i class="bi bi-plus-circle" aria-hidden="true"></i>Add a New Team
              </button>
            </span>
            <span class="rtm-escape">
              Name or grad year wrong, or a team to archive?
              <button type="button" class="btn-link-inline" (click)="openLibrary.emit()">
                <i class="bi bi-collection" aria-hidden="true"></i>Open Club Team Library
              </button>
            </span>
          </div>
          <button type="button" class="btn btn-primary btn-sm rtm-done-btn" (click)="closed.emit()">Done</button>
        </div>
      </div>
    </tsic-dialog>
    `,
    styles: [`
      .rtm-title-icon { color: var(--bs-success); }

      .rtm-body { display: flex; flex-direction: column; gap: var(--space-3); }

      .rtm-lede { margin: 0; font-size: var(--font-size-sm); color: var(--brand-text); }

      .rtm-search {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: 0 var(--space-3);
        border: 1px solid var(--bs-border-color);
        border-radius: var(--radius-md);
        background: var(--brand-surface);
        color: var(--brand-text-muted);

        &:focus-within { border-color: var(--bs-primary); box-shadow: var(--shadow-focus); }
      }

      .rtm-search-input {
        flex: 1;
        min-width: 0;
        padding: var(--space-2) 0;
        border: none;
        outline: none;
        background: transparent;
        color: var(--brand-text);
        font-size: var(--font-size-sm);
      }

      .rtm-list {
        border: 1px solid var(--bs-border-color);
        border-radius: var(--radius-md);
        max-height: 55vh;
        overflow-y: auto;
      }

      .rtm-row {
        display: grid;
        grid-template-columns: minmax(0, 1fr) minmax(0, 1.2fr);
        gap: var(--space-3);
        align-items: center;
        padding: var(--space-2) var(--space-3);
        border-bottom: 1px solid color-mix(in srgb, var(--bs-body-color) 6%, transparent);

        &:last-child { border-bottom: none; }
        &.is-done { background: color-mix(in srgb, var(--bs-success) 6%, transparent); }
        &.is-editing { background: color-mix(in srgb, var(--bs-primary) 4%, transparent); }
      }

      .rtm-team { display: flex; flex-direction: column; gap: 2px; min-width: 0; }

      .rtm-name {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
      }

      .rtm-meta {
        display: inline-flex;
        gap: var(--space-3);
        font-size: var(--font-size-2xs);
        color: var(--brand-text-muted);
        font-variant-numeric: tabular-nums;
      }

      .meta-pair { display: inline-flex; align-items: baseline; gap: var(--space-1); }
      .meta-key { text-transform: uppercase; letter-spacing: 0.06em; font-weight: var(--font-weight-semibold); opacity: 0.7; }

      .rtm-action { display: flex; flex-direction: column; align-items: flex-start; gap: 3px; min-width: 0; }

      .rtm-sub { font-size: var(--font-size-2xs); color: var(--brand-text-muted); font-variant-numeric: tabular-nums; }

      .rtm-done {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--bs-success);

        &--wl { color: var(--brand-text); .bi { color: var(--bs-warning); } }
      }

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

      .rtm-empty {
        margin: 0;
        padding: var(--space-4) var(--space-3);
        text-align: center;
        font-size: var(--font-size-sm);
        color: var(--brand-text-muted);
      }

      .rtm-fold {
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

      .rtm-fold-count {
        padding: 1px var(--space-2);
        border-radius: var(--radius-full);
        background: color-mix(in srgb, var(--bs-body-color) 8%, transparent);
        color: var(--brand-text);
      }

      /* Inline register editor */
      .reg-editor {
        grid-column: 1 / -1;
        display: flex;
        flex-direction: column;
        gap: var(--space-3);
        padding: var(--space-3);
        border: 1px solid color-mix(in srgb, var(--bs-primary) 30%, transparent);
        border-radius: var(--radius-md);
        background: var(--brand-surface);
      }

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

      /* Footer: the ways out when the library doesn't cover it, then Done */
      .rtm-footer { display: flex; align-items: flex-end; gap: var(--space-3); }

      .rtm-escapes { flex: 1; display: flex; flex-direction: column; gap: 2px; min-width: 0; }

      .rtm-escape { font-size: var(--font-size-xs); color: var(--brand-text-muted); }

      .btn-link-inline {
        display: inline-flex;
        align-items: center;
        gap: 4px;
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

      .rtm-done-btn { flex-shrink: 0; min-width: 88px; }

      @media (max-width: 575.98px) {
        .rtm-row { grid-template-columns: 1fr; row-gap: var(--space-2); }
        .rtm-footer { flex-direction: column; align-items: stretch; }
        .rtm-done-btn { width: 100%; }
      }

      @media (prefers-reduced-motion: reduce) {
        .btn-reg { transition: none !important; }
        .btn-reg:hover:not(:disabled) { transform: none; }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegisterTeamModalComponent {
    readonly clubTeams = input.required<readonly ClubTeamDto[]>();
    readonly registeredTeams = input<readonly RegisteredTeamDto[]>([]);
    readonly ageGroups = input<readonly AgeGroupDto[]>([]);
    readonly clubName = input('');
    readonly eventName = input('this event');
    readonly canRegister = input(false);
    readonly actionInProgress = input(false);

    readonly register = output<LibraryRegisterRequest>();
    /** "Add a New Team": the step closes this and opens the add-and-register modal. */
    readonly addNew = output<void>();
    /** "Open Club Team Library": the step closes this and switches to that segment. */
    readonly openLibrary = output<void>();
    readonly closed = output<void>();

    readonly formatLop = formatLop;

    readonly query = signal('');
    readonly showOutside = signal(false);
    readonly showAlready = signal(false);

    /** Teams registered while THIS modal is open — they stay in place, marked, instead of jumping to the fold. */
    private readonly registeredThisVisit = signal<ReadonlySet<number>>(new Set());

    readonly clubPossessive = computed(() => {
        const club = this.clubName().trim();
        return club && club !== 'your club' ? `${club}'s` : "your club's";
    });

    private readonly planContext = computed(() => ({
        ageGroups: this.ageGroups(),
        oldestOffered: resolveOldestOfferedGradYear(this.ageGroups()),
        canRegister: this.canRegister(),
    }));

    private readonly rows = computed<ModalRow[]>(() => {
        const byClubTeam = new Map<number, RegisteredTeamDto>();
        for (const r of this.registeredTeams()) if (r.clubTeamId != null) byClubTeam.set(r.clubTeamId, r);
        const ctx = this.planContext();
        return this.clubTeams()
            .filter(t => !t.bArchived)
            .slice()
            .sort(byGradYearThenName)
            .map(team => {
                const reg = byClubTeam.get(team.clubTeamId) ?? null;
                return { team, registered: reg, plan: planRegistration(team, reg, ctx) };
            });
    });

    /** The search only earns its place on a list long enough to need it. */
    readonly searchable = computed(() => this.rows().length > 8);

    private matches(row: ModalRow): boolean {
        const q = this.query().trim().toLowerCase();
        if (!q) return true;
        return row.team.clubTeamName.toLowerCase().includes(q) || (row.team.clubTeamGradYear ?? '').includes(q);
    }

    private readonly visibleRows = computed(() => this.rows().filter(r => this.matches(r)));

    /** Not registered and fits here — plus anything registered during this visit, kept in place. */
    readonly availableRows = computed(() => {
        const justDone = this.registeredThisVisit();
        const ctx = this.planContext();
        return this.visibleRows().filter(r =>
            justDone.has(r.team.clubTeamId) || (!r.registered && fitsEvent(r.team, ctx)));
    });

    readonly outsideRows = computed(() => {
        const justDone = this.registeredThisVisit();
        const ctx = this.planContext();
        return this.visibleRows().filter(r => !justDone.has(r.team.clubTeamId) && !r.registered && !fitsEvent(r.team, ctx));
    });

    readonly alreadyRows = computed(() => {
        const justDone = this.registeredThisVisit();
        return this.visibleRows().filter(r => !!r.registered && !justDone.has(r.team.clubTeamId));
    });

    private markRegistering(clubTeamId: number): void {
        this.registeredThisVisit.set(new Set([...this.registeredThisVisit(), clubTeamId]));
    }

    registerNow(req: LibraryRegisterRequest): void {
        if (this.actionInProgress()) return;
        this.markRegistering(req.team.clubTeamId);
        this.register.emit(req);
    }

    // ── The inline choice (no LOP saved, no exact age group, or "change") ──
    readonly editingId = signal<number | null>(null);
    readonly pickLop = signal('');
    readonly pickAg = signal('');

    readonly pickWaitlists = computed(() => {
        const ag = this.ageGroups().find(a => a.ageGroupId === this.pickAg());
        return !!ag && ageGroupWaitlists(ag);
    });

    /** Names the AGE GROUP, never the team (ruling 2026-09-24). */
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
        this.markRegistering(team.clubTeamId);
        this.register.emit({ team, ageGroupId, levelOfPlay });
        this.closeEditor();
    }
}
