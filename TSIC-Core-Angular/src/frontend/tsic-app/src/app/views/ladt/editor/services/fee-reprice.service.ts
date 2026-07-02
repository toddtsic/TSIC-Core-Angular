import { Injectable, inject } from '@angular/core';
import { Observable, forkJoin, of } from 'rxjs';
import { map } from 'rxjs/operators';
import { LadtService } from './ladt.service';
import { RoleIds } from '@infrastructure/constants/roles.constants';

/** A fee scope (most-specific id wins on the server: team → agegroup → league). */
export interface FeeScope {
  leagueId?: string;
  agegroupId?: string;
  teamId?: string;
}

/** The role-split blast area: how many of each entity a reprice would touch. */
export interface BlastArea {
  playerCount: number;
  teamCount: number;
}

const PLAYER_ROLE = RoleIds.Player;
const CLUBREP_ROLE = RoleIds.ClubRep;

/**
 * Computes and renders the "blast area" for a per-scope fee/phase change — the count of
 * existing registrations a save would reprice — so the admin is informed before confirming.
 * Player and Club Rep are different entities (player registrations vs teams) with separate
 * counts and copy; this fetches each only for the role that actually changed.
 */
@Injectable({ providedIn: 'root' })
export class FeeRepriceService {
  private readonly ladt = inject(LadtService);

  /** Fetches the affected-registration counts for whichever roles changed (0 for the rest). */
  getBlastArea(scope: FeeScope, roles: { player: boolean; clubRep: boolean }): Observable<BlastArea> {
    const player$ = roles.player ? this.ladt.getAffectedCount(PLAYER_ROLE, scope) : of({ count: 0 });
    const clubRep$ = roles.clubRep ? this.ladt.getAffectedCount(CLUBREP_ROLE, scope) : of({ count: 0 });
    return forkJoin({ p: player$, c: clubRep$ }).pipe(
      map(({ p, c }) => ({ playerCount: p.count, teamCount: c.count }))
    );
  }

  /**
   * Builds the HTML prompt body. Phase flips are always retroactive (a confirm); amount/
   * modifier changes offer future-only vs update-all. Role-aware: "N player registrations"
   * and/or "M teams".
   */
  buildMessage(blast: BlastArea, scopeLabel: string, isPhaseFlip: boolean): string {
    const who = this.describe(blast);
    if (isPhaseFlip) {
      return `Converting <strong>${scopeLabel}</strong> will reprice ${who}. `
           + `Phase changes always apply to existing registrations.`;
    }
    return `This fee change affects ${who} in <strong>${scopeLabel}</strong>. `
         + `Update them now, or apply only to future registrations?`;
  }

  private describe(blast: BlastArea): string {
    const parts: string[] = [];
    if (blast.playerCount > 0) {
      parts.push(`<strong>${blast.playerCount}</strong> player registration${blast.playerCount === 1 ? '' : 's'}`);
    }
    if (blast.teamCount > 0) {
      parts.push(`<strong>${blast.teamCount}</strong> team${blast.teamCount === 1 ? '' : 's'}`);
    }
    return parts.length ? parts.join(' and ') : 'no existing registrations';
  }

  /** Total existing registrations a save's reprice touched, summed from the save results
   *  (each fee save returns `registrationsRepriced`). */
  repricedCount(results: unknown[]): number {
    return results.reduce<number>(
      (sum, r) => sum + (r && typeof r === 'object' && 'registrationsRepriced' in r
        ? (r as { registrationsRepriced: number }).registrationsRepriced : 0), 0);
  }

  /** Repriced counts split by role from the save results — player registrations vs teams
   *  (a ClubRep fee row reprices teams). Each fee save returns `{ fee, registrationsRepriced }`;
   *  the role lives on `fee.roleId`. Non-fee saves (entity update/delete) carry neither and skip. */
  repricedBreakdown(results: unknown[]): { players: number; teams: number } {
    let players = 0;
    let teams = 0;
    for (const r of results) {
      if (!r || typeof r !== 'object') continue;
      const rec = r as { registrationsRepriced?: number; fee?: { roleId?: string } };
      const n = rec.registrationsRepriced ?? 0;
      if (n <= 0) continue;
      if ((rec.fee?.roleId ?? '').toUpperCase() === CLUBREP_ROLE) teams += n;
      else players += n;   // Player role (or an unattributed fee row) → player registrations
    }
    return { players, teams };
  }

  /** Quantified, role-split, pluralized description of what a save repriced —
   *  e.g. "4 player registrations and 2 teams". Empty string when nothing was repriced. */
  describeReprice(results: unknown[]): string {
    const { players, teams } = this.repricedBreakdown(results);
    const parts: string[] = [];
    if (players > 0) parts.push(`${players} player registration${players === 1 ? '' : 's'}`);
    if (teams > 0) parts.push(`${teams} team${teams === 1 ? '' : 's'}`);
    return parts.join(' and ');
  }

  /**
   * Quantified success-toast copy for a fee save, role-split by player/team. Phase flips always
   * toast (a phase change with nothing in scope is still a successful change → generic copy).
   * Amount/modifier saves toast the repriced count when they repriced, and fall back to generic
   * "Fees updated." copy when a fee genuinely changed but nothing was in scope to reprice. A save
   * where no fee changed (entity-only edit) returns null — the inline save-bar message confirms it.
   *
   * `feeChanged` is the caller's authoritative "a fee value actually changed this save" signal
   * (its roleChanged check) — not inferred from the results, because performSave re-persists an
   * unchanged fee idempotently on an entity-only edit, which must NOT toast.
   */
  saveToastMessage(results: unknown[], isPhaseFlip: boolean, feeChanged: boolean, level?: string): string | null {
    const who = this.describeReprice(results);
    const at = level ? ` at ${level} level` : '';
    if (isPhaseFlip) {
      return who ? `Payment phase updated${at} — converted ${who}.` : `Payment phase updated${at}.`;
    }
    if (who) return `Fees updated${at} — repriced ${who}.`;
    return feeChanged ? `Fees updated${at}.` : null;
  }

  /** Success-toast copy for a payment-phase change quantified by a pre-summed count (the league
   *  fan-out path, whose per-age-group response carries only a total, not a role split). */
  phaseToastMessage(repriced: number, level?: string): string {
    const at = level ? ` at ${level} level` : '';
    return repriced > 0
      ? `Payment phase updated${at} — converted ${repriced} registration${repriced === 1 ? '' : 's'}.`
      : `Payment phase updated${at}.`;
  }
}
