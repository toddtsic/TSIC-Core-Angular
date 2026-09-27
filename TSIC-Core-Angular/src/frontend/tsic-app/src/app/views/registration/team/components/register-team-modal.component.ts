import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { CurrencyPipe, NgTemplateOutlet } from '@angular/common';
import type { AgeGroupDto, ClubTeamDto, RegisteredTeamDto } from '@core/api';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { LOP_CHOICES, formatLop, normalizeLop } from '@shared/teams/lop-choices';
import { resolveRecommendedAgeGroupId, type SlotPricing } from './event-age-group.util';
import { ageGroupLabel, type LibraryRegisterRequest } from './library-segment.types';
import { ageGroupWaitlists, byGradYearThenName, pricingOfAgeGroup } from './library-register-plan';

/** A row's two picks. '' = no pick (no confident guess, or the rep cleared it). */
interface RowPick { lop: string; ag: string; }

interface ModalRow {
    team: ClubTeamDto;
    registered: RegisteredTeamDto | null;
    pick: RowPick;
}

/**
 * Register a team — THE one place a club rep registers a team for this event (Todd 2026-09-26:
 * "restrict to our canonical add team modal"). The Club Team Library tab is list-only.
 *
 * A form, one row per library team (Todd 2026-09-26): level of play and age group are DROPDOWNS
 * preselected to the best guess — the library team's level, and the age group its grad year names
 * (the WAITLIST twin when that group is full) — with a Register button per row. The rep sees both
 * answers before pressing and changes either in place.
 *
 * Guide, don't intrude: nothing is pre-ticked and there is no "Register all" — which teams are
 * coming is the rep's call, one press each. A dropdown with no confident guess starts BLANK and
 * that row's Register stays off until the rep picks: a wrong preselection is worse than an empty
 * one.
 *
 * Stays open across registrations: a registered row locks to "Registered in 2030" IN PLACE, so a
 * rep can bring several teams in one sitting, and carries the mistake-undo while it applies
 * (TeamRegistrationUndo — same deadlines the Registered Teams grid reads). Owns no domain state;
 * the Teams step registers and removes.
 */
@Component({
    selector: 'app-register-team-modal',
    standalone: true,
    imports: [TsicDialogComponent, CurrencyPipe, NgTemplateOutlet],
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
            Each team from {{ clubPossessive() }} Club Team Library has its level and age group filled in from the
            library. Check them, then press <b>Register</b> on each team you're bringing. This stays open so you
            can register several.
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
            @let reg = row.registered;
            @let pick = row.pick;
            <div class="rtm-row" [class.is-done]="!!reg">
              <div class="rtm-team">
                <span class="rtm-name" [attr.title]="team.clubTeamName">{{ team.clubTeamName }}</span>
                <span class="rtm-meta">
                  <span class="meta-pair"><span class="meta-key">Grad</span>{{ team.clubTeamGradYear || '—' }}</span>
                  @if (reg) {
                    <span class="meta-pair"><span class="meta-key">LOP</span>{{ formatLop(reg.levelOfPlay) || '—' }}</span>
                  }
                </span>
              </div>

              @if (reg) {
                @let undoMin = undoMinutesLeft(reg.teamId);
                <div class="rtm-done-cell">
                  <span class="rtm-done" [class.rtm-done--wl]="reg.isWaitlisted">
                    @if (reg.isWaitlisted) {
                      <i class="bi bi-hourglass-split" aria-hidden="true"></i>On the {{ reg.ageGroupDisplayName || reg.ageGroupName }} waitlist
                    } @else {
                      <i class="bi bi-check-circle-fill" aria-hidden="true"></i>Registered in {{ reg.ageGroupDisplayName || reg.ageGroupName }}
                    }
                  </span>
                  @if (undoMin > 0) {
                    <button type="button" class="btn-undo" [disabled]="actionInProgress()"
                            [attr.aria-label]="'Undo registering ' + team.clubTeamName + ', ' + undoMin + ' minutes left'"
                            (click)="undo.emit(reg)">
                      <i class="bi bi-arrow-counterclockwise" aria-hidden="true"></i>Undo &middot; {{ undoMin }} min
                    </button>
                  }
                </div>
              } @else if (!canRegister()) {
                <div class="rtm-done-cell">
                  <span class="rtm-sub"><i class="bi bi-lock-fill" aria-hidden="true"></i> Registration closed</span>
                </div>
              } @else {
                <label class="rtm-field rtm-field--lop">
                  <span class="rtm-field-label">Level</span>
                  <select class="rtm-select" [class.is-blank]="!pick.lop"
                          [attr.aria-label]="'Level of play for ' + team.clubTeamName"
                          [disabled]="actionInProgress()"
                          (change)="setPick(team.clubTeamId, pick, 'lop', $any($event.target).value)">
                    <option value="" [selected]="!pick.lop">Pick…</option>
                    @for (c of lopChoices; track c.value) {
                      <option [value]="c.value" [selected]="c.value === pick.lop">{{ c.label }}</option>
                    }
                  </select>
                </label>
                <label class="rtm-field rtm-field--ag">
                  <span class="rtm-field-label">Age group</span>
                  <select class="rtm-select" [class.is-blank]="!pick.ag"
                          [attr.aria-label]="'Age group for ' + team.clubTeamName"
                          [disabled]="actionInProgress()"
                          (change)="setPick(team.clubTeamId, pick, 'ag', $any($event.target).value)">
                    <option value="" [selected]="!pick.ag">Pick an age group…</option>
                    @for (o of ageGroupOptions(); track o.id) {
                      <option [value]="o.id" [selected]="o.id === pick.ag">{{ o.text }}</option>
                    }
                  </select>
                </label>
                <div class="rtm-go">
                  @let wl = pickWaitlists(pick.ag);
                  <button type="button" class="btn-reg" [class.btn-reg--wl]="wl"
                          [disabled]="actionInProgress() || !pick.lop || !pick.ag"
                          (click)="registerRow(team, pick)">
                    <i class="bi" [class.bi-trophy-fill]="!wl" [class.bi-hourglass-split]="wl" aria-hidden="true"></i>
                    {{ wl ? 'Join waitlist' : 'Register' }}
                  </button>
                </div>
                <!-- Why a dropdown is blank, or what the pick costs. Words, never color alone. -->
                <span class="rtm-note">
                  @if (!pick.lop) {
                    <i class="bi bi-exclamation-circle" aria-hidden="true"></i> No level of play saved on this team &middot; pick one.
                  }
                  @if (!pick.ag) {
                    <i class="bi bi-exclamation-circle" aria-hidden="true"></i>
                    No {{ team.clubTeamGradYear || 'matching' }} age group here &middot; pick the one this team plays in.
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
                  Every team in your Club Team Library is registered for {{ eventName() }}.
                }
              </p>
            }

            @if (alreadyRows().length > 0) {
              <button type="button" class="rtm-fold" [attr.aria-expanded]="showAlready()" (click)="showAlready.set(!showAlready())">
                <i class="bi" [class.bi-chevron-down]="showAlready()" [class.bi-chevron-right]="!showAlready()" aria-hidden="true"></i>
                Already registered for {{ eventName() }}
                <span class="rtm-fold-count">{{ alreadyRows().length }}</span>
              </button>
              @if (showAlready()) {
                <!-- Read-only here by design (Todd 2026-09-26): the event record is edited in ONE place. -->
                <p class="rtm-fold-note">
                  <i class="bi bi-pencil" aria-hidden="true"></i>
                  To change a registered team's name or level, use the pencil on <b>{{ eventName() }} Registered Teams</b>.
                </p>
                @for (row of alreadyRows(); track row.team.clubTeamId) {
                  <ng-container *ngTemplateOutlet="rowTpl; context: { $implicit: row }" />
                }
              }
            }
          </div>

          <p class="rtm-hint">
            <i class="bi bi-info-circle" aria-hidden="true"></i>
            The level you pick here is for {{ eventName() }} only &mdash; your library team keeps its own.
          </p>
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

      .rtm-hint {
        display: flex;
        align-items: baseline;
        gap: var(--space-2);
        margin: 0;
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
      }

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

      /* One row: team | level | age group | Register, the note under the pickers */
      .rtm-row {
        display: grid;
        /* Every track fixed or fr, never auto: each row is its own grid, so a content-sized
           track ("Join waitlist" vs "Register") shifted the dropdowns row to row. */
        grid-template-columns: minmax(0, 1.2fr) 88px minmax(0, 1.5fr) 136px;
        grid-template-areas:
          "team lop ag go"
          "team note note note";
        column-gap: var(--space-3);
        row-gap: 2px;
        align-items: center;
        padding: var(--space-2) var(--space-3);
        border-bottom: 1px solid color-mix(in srgb, var(--bs-body-color) 6%, transparent);

        &:last-child { border-bottom: none; }
        &.is-done {
          grid-template-areas: "team done done done";
          background: color-mix(in srgb, var(--bs-success) 6%, transparent);
        }
      }

      .rtm-team { grid-area: team; display: flex; flex-direction: column; gap: 2px; min-width: 0; }
      .rtm-field--lop { grid-area: lop; }
      .rtm-field--ag { grid-area: ag; }
      .rtm-go { grid-area: go; .btn-reg { width: 100%; justify-content: center; } }
      .rtm-note { grid-area: note; }
      .rtm-done-cell {
        grid-area: done;
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--space-3);
      }

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

      .rtm-field { display: flex; flex-direction: column; gap: 1px; min-width: 0; margin: 0; }

      .rtm-field-label {
        font-size: var(--font-size-2xs);
        font-weight: var(--font-weight-semibold);
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--brand-text-muted);
      }

      .rtm-select {
        width: 100%;
        min-width: 0;
        padding: 4px var(--space-2);
        border: 1px solid var(--bs-border-color);
        border-radius: var(--radius-sm);
        background: var(--brand-surface);
        color: var(--brand-text);
        font-size: var(--font-size-sm);
        cursor: pointer;

        &:focus-visible { outline: none; border-color: var(--bs-primary); box-shadow: var(--shadow-focus); }
        &:disabled { opacity: 0.6; cursor: default; }
        /* A blank pick asks for attention: dashed amber edge + the note's words below. */
        &.is-blank { border-style: dashed; border-color: var(--bs-warning); color: var(--brand-text-muted); }
      }

      .rtm-note {
        font-size: var(--font-size-2xs);
        color: var(--brand-text-muted);
        font-variant-numeric: tabular-nums;

        .bi { color: var(--bs-warning); }
      }

      .rtm-sub { font-size: var(--font-size-2xs); color: var(--brand-text-muted); }

      .rtm-done {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--bs-success);

        &--wl { color: var(--brand-text); .bi { color: var(--bs-warning); } }
      }

      .btn-undo {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        padding: 2px var(--space-2);
        border: 1px solid var(--bs-border-color);
        border-radius: var(--radius-sm);
        background: var(--brand-surface);
        color: var(--brand-text);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        font-variant-numeric: tabular-nums;
        cursor: pointer;

        &:hover:not(:disabled) { border-color: var(--bs-danger); color: var(--bs-danger); }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        &:disabled { opacity: 0.45; cursor: default; }
      }

      .btn-reg {
        display: inline-flex;
        align-items: center;
        gap: var(--space-2);
        margin-top: 14px; /* sits on the selects' baseline, under their labels */
        padding: 5px var(--space-3);
        border: 1px solid var(--bs-success);
        border-radius: var(--radius-sm);
        background: var(--bs-success);
        color: var(--neutral-0);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        white-space: nowrap;
        cursor: pointer;
        box-shadow: var(--shadow-xs);
        transition: filter 0.12s ease, transform 0.12s ease;

        &:hover:not(:disabled) { filter: brightness(0.93); transform: translateY(-1px); }
        &:active:not(:disabled) { transform: translateY(0); }
        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        &:disabled { opacity: 0.4; cursor: default; transform: none; }

        &--wl { border-color: var(--bs-warning); background: var(--bs-warning); color: var(--bs-dark); }
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

      .rtm-fold-note {
        display: flex;
        align-items: baseline;
        gap: var(--space-2);
        margin: 0;
        padding: var(--space-2) var(--space-3);
        border-bottom: 1px solid color-mix(in srgb, var(--bs-body-color) 6%, transparent);
        font-size: var(--font-size-xs);
        color: var(--brand-text);

        .bi { color: var(--bs-success); flex-shrink: 0; }
      }

      .rtm-fold-count {
        padding: 1px var(--space-2);
        border-radius: var(--radius-full);
        background: color-mix(in srgb, var(--bs-body-color) 8%, transparent);
        color: var(--brand-text);
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

      /* Phone: name, then the two pickers side by side, then the note, then Register full width */
      @media (max-width: 575.98px) {
        .rtm-row {
          grid-template-columns: 88px minmax(0, 1fr);
          grid-template-areas:
            "team team"
            "lop ag"
            "note note"
            "go go";
          row-gap: var(--space-2);

          &.is-done { grid-template-areas: "team team" "done done"; }
        }
        .btn-reg { width: 100%; justify-content: center; margin-top: 0; }
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
    /** The mistake-undo: teamId → deadline (epoch ms) and the step's ticking clock — same as the grid's. */
    readonly undoDeadlines = input<ReadonlyMap<string, number>>(new Map());
    readonly now = input(0);

    readonly register = output<LibraryRegisterRequest>();
    /** Undo a team registered by mistake: the step confirms and removes (the grid's own path). */
    readonly undo = output<RegisteredTeamDto>();
    /** "Add a New Team": the step closes this and opens the add-and-register modal. */
    readonly addNew = output<void>();
    /** "Open Club Team Library": the step closes this and switches to that segment. */
    readonly openLibrary = output<void>();
    readonly closed = output<void>();

    readonly formatLop = formatLop;
    readonly lopChoices = LOP_CHOICES;

    readonly query = signal('');
    readonly showAlready = signal(false);

    /** Teams registered while THIS modal is open — they stay in place, locked, instead of jumping to the fold. */
    private readonly registeredThisVisit = signal<ReadonlySet<number>>(new Set());
    /** The rep's own changes to a row's preselected picks. A row without one shows its seed. */
    private readonly picks = signal<ReadonlyMap<number, RowPick>>(new Map());

    readonly clubPossessive = computed(() => {
        const club = this.clubName().trim();
        return club && club !== 'your club' ? `${club}'s` : "your club's";
    });

    /**
     * The age-group dropdown's options, each carrying what choosing it means: price, or that it is
     * full and joins the waitlist. Listed in the event's own order.
     */
    readonly ageGroupOptions = computed(() => this.ageGroups().map(ag => {
        const label = ageGroupLabel(ag);
        const price = pricingOfAgeGroup(ag);
        const money = (n: number) => `$${n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
        const text = ageGroupWaitlists(ag)
            ? `${label} waitlist · no fee until placed`
            : price.kind === 'free' ? `${label} · no fee`
            // Two figures ONLY when the age group splits deposit + balance and is still in the
            // deposit stage (Todd 2026-09-26): full amount, then what is due now. Otherwise one.
            : price.kind === 'deposit' ? `${label} · ${money(price.total)} · ${money(price.now)} due now`
            : price.kind === 'full' ? `${label} · ${money(price.total)}`
            : label;
        return { id: ag.ageGroupId, text };
    }));

    /** The best guess: the library's level (on the 1–5 scale) and the age group the grad year names. */
    private seedFor(team: ClubTeamDto): RowPick {
        return {
            lop: normalizeLop(team.clubTeamLevelOfPlay),
            ag: resolveRecommendedAgeGroupId(this.ageGroups(), team.clubTeamGradYear),
        };
    }

    private readonly rows = computed<ModalRow[]>(() => {
        const byClubTeam = new Map<number, RegisteredTeamDto>();
        for (const r of this.registeredTeams()) if (r.clubTeamId != null) byClubTeam.set(r.clubTeamId, r);
        const picks = this.picks();
        return this.clubTeams()
            .filter(t => !t.bArchived)
            .slice()
            .sort(byGradYearThenName)
            .map(team => ({
                team,
                registered: byClubTeam.get(team.clubTeamId) ?? null,
                pick: picks.get(team.clubTeamId) ?? this.seedFor(team),
            }));
    });

    /** The search only earns its place on a list long enough to need it. */
    readonly searchable = computed(() => this.rows().length > 8);

    private matches(row: ModalRow): boolean {
        const q = this.query().trim().toLowerCase();
        if (!q) return true;
        return row.team.clubTeamName.toLowerCase().includes(q) || (row.team.clubTeamGradYear ?? '').includes(q);
    }

    private readonly visibleRows = computed(() => this.rows().filter(r => this.matches(r)));

    /** Not registered — plus anything registered during this visit, kept in place. */
    readonly availableRows = computed(() => {
        const justDone = this.registeredThisVisit();
        return this.visibleRows().filter(r => justDone.has(r.team.clubTeamId) || !r.registered);
    });

    readonly alreadyRows = computed(() => {
        const justDone = this.registeredThisVisit();
        return this.visibleRows().filter(r => !!r.registered && !justDone.has(r.team.clubTeamId));
    });

    /** Whole minutes left to undo this team (rounded up); 0 = no undo now. */
    undoMinutesLeft(teamId: string): number {
        const deadline = this.undoDeadlines().get(teamId);
        if (deadline === undefined) return 0;
        const ms = deadline - this.now();
        return ms > 0 ? Math.ceil(ms / 60000) : 0;
    }

    setPick(clubTeamId: number, current: RowPick, field: keyof RowPick, value: string): void {
        const next = new Map(this.picks());
        next.set(clubTeamId, { ...current, [field]: value });
        this.picks.set(next);
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

    registerRow(team: ClubTeamDto, pick: RowPick): void {
        if (this.actionInProgress() || !pick.lop || !pick.ag) return;
        this.registeredThisVisit.set(new Set([...this.registeredThisVisit(), team.clubTeamId]));
        this.register.emit({ team, ageGroupId: pick.ag, levelOfPlay: pick.lop });
    }
}
