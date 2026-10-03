// Club rep reopen stampede: every club rep from the 2026 event returns to the 2027 event at the
// same instant, registers their teams (mapped by grad year) and pays by CC (sandbox test card).
//
// Normally run via Run-StressTest.ps1 (starts the API + frontend, then runs this). Direct use:
//   node clubrep-reopen-stampede.mjs --dry            print the cast, touch nothing
//   node clubrep-reopen-stampede.mjs --enable         SuperUser turns on team registration, then stops
//   node clubrep-reopen-stampede.mjs --one <user>     run a single rep end to end
//   node clubrep-reopen-stampede.mjs                  enable + full stampede + report
//   options: --reps N (first N reps only)   --no-pay (register only)
//
// Guards: API must be localhost; DB is the local .\SS2016. Payments use 4111111111111111, which
// the live Authorize.Net account declines, and the Development env routes ADN to SANDBOX.

import { execFileSync } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const SRC_JOB = '51cd29ab-c44d-48ba-a9cc-968252f1e118';
const TGT_JOB = '86806e88-d482-409f-acff-9c0852fa3519';
const TGT_JOBPATH = 'lftc-fallshowcase-2027';
const YOUNGEST_SRC = '2035'; // youngest grad year in 2026
const YOUNGEST_TGT = '2036'; // new youngest grad year in 2027 (hand-entered teams)
const SUPERUSER = 'TSICSuperUser';
const PASSWORD = 'dev123';
const API = process.env.STRESS_API ?? 'https://localhost:7215/api';
const SQL_SERVER = '.\\SS2016';
const SQL_DB = 'TSICV5';
const HERE = dirname(fileURLToPath(import.meta.url));

const CARD = {
    number: '4111111111111111', expiry: '1230', code: '123',
    address: '123 Test St', zip: '21201', email: 'stress-test@example.com', phone: '4105551212',
};

// ---- guards ---------------------------------------------------------------------------------
if (!/^https?:\/\/(localhost|127\.0\.0\.1|\[::1\])(:\d+)?\//i.test(API)) {
    console.error(`REFUSING: API ${API} is not localhost.`);
    process.exit(1);
}
process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0'; // local dev cert; localhost only (guarded above)

// ---- args -----------------------------------------------------------------------------------
const argv = process.argv.slice(2);
const flag = (f) => argv.includes(f);
const opt = (f) => { const i = argv.indexOf(f); return i >= 0 ? argv[i + 1] : undefined; };
const DRY = flag('--dry');
const ENABLE_ONLY = flag('--enable');
const NO_PAY = flag('--no-pay');
const ONE = opt('--one');
const REPS = opt('--reps') ? Number(opt('--reps')) : undefined;

// ---- sql ------------------------------------------------------------------------------------
// Single-value query. -y 0 (unlimited width, needed for FOR JSON) can't be combined with -h -1, so
// a named column prints a header + dash line; keep only what follows the dashes.
function sql(query) {
    const out = execFileSync('sqlcmd', ['-S', SQL_SERVER, '-d', SQL_DB, '-E', '-b', '-y', '0', '-Q', `SET NOCOUNT ON; ${query}`],
        { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
    const lines = out.split(/\r?\n/);
    const dash = lines.findIndex(l => /^-+\s*$/.test(l));
    return lines.slice(dash + 1).join('').trim();
}
function sqlFile(path) {
    return execFileSync('sqlcmd', ['-S', SQL_SERVER, '-d', SQL_DB, '-E', '-b', '-W', '-s', '|', '-i', path],
        { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
}

// Cast: 2026 club reps, their counted teams whose grad year has a 2027 age group, and the
// hand-entered teams for the new youngest age group.
// 2026 teams that count as "registered": active, and not in a WAITLIST or Dropped Teams age group.
const COUNTED_TEAM = `t.active = 1 AND sa.agegroupName NOT LIKE 'WAITLIST%' AND sa.agegroupName <> 'Dropped Teams'`;

function loadCast() {
    const json = sql(`
SELECT (
  SELECT r.RegistrationId AS srcRegId, u.UserName AS userName, r.club_name AS clubName,
    -- Last year's teams. clubTeamId = the library row (picked by id, so a library rename still
    -- matches); eventName/levelOfPlay = the 2026 event copy, hand-entered when the dropdown has no match.
    (SELECT x.clubTeamId, x.eventName, x.gradYear, x.levelOfPlay
     FROM (SELECT t.ClubTeamId AS clubTeamId, t.teamName AS eventName,
                  COALESCE(ct.ClubTeamGradYear, sa.agegroupName) AS gradYear,
                  COALESCE(t.level_of_play, ct.ClubTeamLevelOfPlay) AS levelOfPlay
           FROM Leagues.teams t
           JOIN Leagues.agegroups sa ON sa.agegroupID = t.agegroupID
           LEFT JOIN Clubs.ClubTeams ct ON ct.ClubTeamId = t.ClubTeamId
           WHERE t.clubrep_registrationid = r.RegistrationId AND t.jobID = '${SRC_JOB}' AND ${COUNTED_TEAM}) x
     WHERE EXISTS (SELECT 1 FROM Leagues.agegroups a JOIN Jobs.Job_Leagues jl ON jl.leagueID = a.leagueID
                   WHERE jl.jobID = '${TGT_JOB}' AND a.agegroupName = x.gradYear)
     ORDER BY x.gradYear, x.eventName
     FOR JSON PATH) AS teams,
    -- Youngest age group is new next year: one hand-entered team per 2026 youngest-AG team,
    -- year swapped in the name (2035 Blue -> 2036 Blue).
    (SELECT CASE WHEN CHARINDEX('${YOUNGEST_SRC}', ct.ClubTeamName) > 0
                 THEN REPLACE(ct.ClubTeamName, '${YOUNGEST_SRC}', '${YOUNGEST_TGT}')
                 ELSE CONCAT('${YOUNGEST_TGT} ', ct.ClubTeamName) END AS clubTeamName,
            '${YOUNGEST_TGT}' AS gradYear, ct.ClubTeamLevelOfPlay AS levelOfPlay
     FROM Leagues.teams t
     JOIN Leagues.agegroups sa ON sa.agegroupID = t.agegroupID
     JOIN Clubs.ClubTeams ct ON ct.ClubTeamId = t.ClubTeamId
     WHERE t.clubrep_registrationid = r.RegistrationId AND t.jobID = '${SRC_JOB}' AND ${COUNTED_TEAM}
       AND ct.ClubTeamGradYear = '${YOUNGEST_SRC}'
     FOR JSON PATH) AS newTeams
  FROM Jobs.Registrations r
  JOIN dbo.AspNetUsers u ON u.Id = r.UserId
  WHERE r.RegistrationId IN (SELECT DISTINCT t.clubrep_registrationid FROM Leagues.teams t
                             JOIN Leagues.agegroups sa ON sa.agegroupID = t.agegroupID
                             WHERE t.jobID = '${SRC_JOB}' AND ${COUNTED_TEAM})
  ORDER BY u.UserName
  FOR JSON PATH)`);
    return JSON.parse(json)
        .map(r => ({ ...r, teams: r.teams ?? [], newTeams: r.newTeams ?? [] }))
        .filter(r => r.teams.length || r.newTeams.length);
}

// ---- http -----------------------------------------------------------------------------------
const calls = [];
const T0 = { at: 0 };

async function call(rep, step, method, path, token, body) {
    const start = performance.now();
    let status = 0, data, err;
    try {
        const res = await fetch(`${API}${path}`, {
            method,
            headers: {
                'Content-Type': 'application/json',
                ...(token ? { Authorization: `Bearer ${token}` } : {}),
            },
            body: body === undefined ? undefined : JSON.stringify(body),
            signal: AbortSignal.timeout(180_000),
        });
        status = res.status;
        const text = await res.text();
        try { data = text ? JSON.parse(text) : undefined; } catch { data = text; }
    } catch (e) {
        err = e.name === 'TimeoutError' ? 'timeout' : e.message;
    }
    const ms = performance.now() - start;
    const ok = status >= 200 && status < 300;
    calls.push({
        rep, step, status, ok, ms: Math.round(ms), atMs: Math.round(start - T0.at),
        msg: ok ? undefined : (err ?? (typeof data === 'string' ? data.slice(0, 300) : JSON.stringify(data)?.slice(0, 300))),
    });
    if (!ok) throw new Error(`${step} -> ${status || err}: ${calls.at(-1).msg}`);
    return data;
}

// ---- superuser: open team registration on the target ---------------------------------------
async function enableTeamRegistration() {
    const suRegId = sql(`
SELECT TOP 1 r.RegistrationId FROM Jobs.Registrations r
JOIN dbo.AspNetUsers u ON u.Id = r.UserId
WHERE u.UserName = '${SUPERUSER}' AND r.jobID = '${TGT_JOB}' AND r.bActive = 1`);
    if (!suRegId) throw new Error(`${SUPERUSER} has no active registration on the target job`);
    const login = await call('SU', 'su-login', 'POST', '/auth/login', null, { username: SUPERUSER, password: PASSWORD });
    const sel = await call('SU', 'su-select', 'POST', '/auth/select-registration', login.accessToken, { regId: suRegId });
    await call('SU', 'su-visibility', 'PUT', '/job-visibility', sel.accessToken, { allowTeamRegistration: true });
    const flags = sql(`SELECT CONCAT('bRegistrationAllowTeam=', bRegistrationAllowTeam, ' bClubRepAllowAdd=', bClubRepAllowAdd) FROM Jobs.Jobs WHERE JobId = '${TGT_JOB}'`);
    console.log(`Target job: ${flags}`);
    if (!flags.includes('bRegistrationAllowTeam=1') || !flags.includes('bClubRepAllowAdd=1')) {
        throw new Error('Club rep registration is not open on the target job');
    }
}

// ---- one club rep, end to end --------------------------------------------------------------
async function runRep(rep, gate) {
    const r = rep.userName;
    const out = { rep: r, club: rep.clubName, planned: rep.teams.length + rep.newTeams.length, handEntered: 0, created: 0, registered: 0, waitlisted: 0, paid: 0, charged: 0, error: undefined };
    await gate;
    try {
        const login = await call(r, 'login', 'POST', '/auth/login', null, { username: r, password: PASSWORD });
        if (login.requiresTosSignature) await call(r, 'accept-tos', 'POST', '/auth/accept-tos', login.accessToken);

        const clubs = await call(r, 'my-clubs', 'GET', '/team-registration/my-clubs', login.accessToken);
        const club = clubs.find(c => c.clubName?.toLowerCase() === rep.clubName?.toLowerCase()) ?? clubs[0];
        if (!club) throw new Error('my-clubs returned no clubs');

        const init = await call(r, 'initialize', 'POST', '/team-registration/initialize-registration', login.accessToken,
            { clubName: club.clubName, jobPath: TGT_JOBPATH });
        const tok = init.accessToken;

        let meta = await call(r, 'metadata', 'GET', '/team-registration/metadata?bPayBalanceDue=false', tok);
        if (!meta.bWaiverSigned3) await call(r, 'accept-refund', 'POST', '/team-registration/accept-refund-policy', tok);

        const agByName = new Map(meta.ageGroups.filter(a => !a.ageGroupName.startsWith('WAITLIST')).map(a => [a.ageGroupName, a.ageGroupId]));

        // The add row's dropdown offers the Club Team Library minus archived teams and teams
        // already registered here. Rebuilt from each reload, as the step does.
        const offered = (m) => {
            const here = new Set(m.registeredTeams.map(t => t.clubTeamId).filter(id => id != null));
            return m.clubTeams.filter(t => !t.bArchived && !here.has(t.clubTeamId));
        };
        const skip = (msg) => calls.push({ rep: r, step: 'register-team', status: 0, ok: false, ms: 0, atMs: 0, msg });

        const register = async (lib, ageGroupId) => {
            try {
                const res = await call(r, 'register-team', 'POST', '/team-registration/register-team', tok, {
                    clubTeamId: lib.clubTeamId, teamName: lib.clubTeamName, clubTeamGradYear: lib.clubTeamGradYear,
                    levelOfPlay: lib.clubTeamLevelOfPlay || undefined, ageGroupId,
                });
                out.registered++;
                if (res.isWaitlisted) out.waitlisted++;
            } catch { /* recorded in calls; keep going, as a rep would */ }
            // The Teams step reloads metadata after every add row result (teams-step loadTeamsMetadata).
            meta = await call(r, 'metadata-reload', 'GET', '/team-registration/metadata?bPayBalanceDue=false', tok).catch(() => meta);
        };

        // Typed into the add row: a name + grad year matching an offered library team becomes a
        // pick; otherwise the add row creates the library team first, then registers it.
        const handEnter = async (name, gradYear, levelOfPlay, ageGroupId) => {
            let lib = offered(meta).find(t => t.clubTeamName.trim().toLowerCase() === name.trim().toLowerCase()
                && t.clubTeamGradYear === gradYear);
            if (!lib) {
                try {
                    lib = await call(r, 'create-club-team', 'POST', '/team-registration/create-club-team', tok,
                        { clubTeamName: name, clubTeamGradYear: gradYear, levelOfPlay: levelOfPlay || undefined });
                    out.created++;
                } catch { return; }
            }
            await register(lib, ageGroupId);
        };

        // 1. Last year's teams: pick the same library team from the dropdown (by id, so a library
        //    rename still matches). No match -> hand-enter last year's event name.
        for (const team of rep.teams) {
            const ageGroupId = agByName.get(team.gradYear);
            if (!ageGroupId) { skip(`no 2027 age group ${team.gradYear}`); continue; }
            const lib = team.clubTeamId != null && offered(meta).find(t => t.clubTeamId === team.clubTeamId);
            if (lib) await register(lib, ageGroupId);
            else { out.handEntered++; await handEnter(team.eventName, team.gradYear, team.levelOfPlay, ageGroupId); }
        }

        // 2. The new youngest age group, hand-entered.
        for (const nt of rep.newTeams) {
            const ageGroupId = agByName.get(nt.gradYear);
            if (!ageGroupId) { skip(`no 2027 age group ${nt.gradYear}`); continue; }
            out.handEntered++;
            await handEnter(nt.clubTeamName, nt.gradYear, nt.levelOfPlay, ageGroupId);
        }

        if (!NO_PAY && out.registered > 0) {
            meta = await call(r, 'metadata-prepay', 'GET', '/team-registration/metadata?bPayBalanceDue=false', tok);
            const owing = meta.registeredTeams.filter(t => t.owedTotal > 0);
            const total = Math.round(owing.reduce((s, t) => s + t.ccOwedTotal, 0) * 100) / 100;
            if (owing.length && total > 0) {
                const [firstName, lastName] = ['Stress', r.replace(/[^A-Za-z]/g, '').slice(0, 20) || 'Rep'];
                const pay = await call(r, 'pay', 'POST', '/team-payment/process', tok, {
                    teamIds: owing.map(t => t.teamId), totalAmount: total,
                    creditCard: { ...CARD, firstName, lastName },
                });
                out.paid = (pay.teams ?? []).filter(t => t.charged).length;
                out.charged = (pay.teams ?? []).reduce((s, t) => s + (t.charged ? t.chargedAmount : 0), 0);
                if (!pay.success) out.error = `pay: ${pay.error ?? ''} ${pay.message ?? ''}`.trim();
            }
        }
    } catch (e) {
        out.error = e.message;
    }
    return out;
}

// ---- report ---------------------------------------------------------------------------------
function pct(sorted, p) { return sorted.length ? sorted[Math.min(sorted.length - 1, Math.floor(p * sorted.length))] : 0; }

function report(results, wallMs) {
    const lines = [];
    const log = (s = '') => { console.log(s); lines.push(s); };
    log(`Wall clock: ${(wallMs / 1000).toFixed(1)} s for ${results.length} reps`);
    log('');
    log('step              calls   ok  fail    p50ms   p95ms   maxms');
    const steps = [...new Set(calls.map(c => c.step))];
    for (const s of steps) {
        const cs = calls.filter(c => c.step === s && c.ms > 0);
        const ms = cs.map(c => c.ms).sort((a, b) => a - b);
        const ok = cs.filter(c => c.ok).length;
        log(`${s.padEnd(16)} ${String(cs.length).padStart(6)} ${String(ok).padStart(4)} ${String(cs.length - ok).padStart(5)} ${String(pct(ms, .5)).padStart(8)} ${String(pct(ms, .95)).padStart(7)} ${String(ms.at(-1) ?? 0).padStart(7)}`);
    }
    log('');
    const sum = (k) => results.reduce((s, r) => s + r[k], 0);
    log(`Teams planned ${sum('planned')} (hand-entered ${sum('handEntered')}, new library teams ${sum('created')}), registered ${sum('registered')} (waitlisted ${sum('waitlisted')}), paid ${sum('paid')}, charged $${sum('charged').toFixed(2)}`);
    const failed = results.filter(r => r.error);
    log(`Reps with an error: ${failed.length}`);
    for (const f of failed) log(`  ${f.rep}: ${f.error}`);
    const teamFails = calls.filter(c => c.step === 'register-team' && !c.ok);
    if (teamFails.length) {
        log(`register-team failures: ${teamFails.length}`);
        const byMsg = Object.entries(teamFails.reduce((m, c) => (m[c.msg] = (m[c.msg] ?? 0) + 1, m), {}));
        for (const [m, n] of byMsg) log(`  ${n} x ${m}`);
    }
    return lines;
}

// ---- main -----------------------------------------------------------------------------------
async function main() {
    let cast = loadCast();
    if (ONE) cast = cast.filter(r => r.userName.toLowerCase() === ONE.toLowerCase());
    if (REPS) cast = cast.slice(0, REPS);
    console.log(`Cast: ${cast.length} reps, ${cast.reduce((s, r) => s + r.teams.length, 0)} returning teams + ${cast.reduce((s, r) => s + r.newTeams.length, 0)} new ${YOUNGEST_TGT} teams`);

    if (DRY) {
        for (const r of cast) console.log(`  ${r.userName.padEnd(30)} ${String(r.teams.length).padStart(3)} +${r.newTeams.length}  ${r.clubName}`);
        return;
    }

    await enableTeamRegistration();
    if (ENABLE_ONLY) return;
    if (!cast.length) throw new Error('Empty cast');

    let release;
    const gate = new Promise(res => (release = res));
    let done = 0;
    const running = cast.map(rep => runRep(rep, gate).finally(() => done++));
    T0.at = performance.now();
    release();
    const ticker = setInterval(() => {
        const fails = calls.filter(c => !c.ok).length;
        console.log(`  ${((performance.now() - T0.at) / 1000).toFixed(0).padStart(4)} s  reps done ${done}/${cast.length}  calls ${calls.length}  failed ${fails}`);
    }, 5000);
    const results = await Promise.all(running);
    clearInterval(ticker);
    const wallMs = performance.now() - T0.at;

    const lines = report(results, wallMs);
    console.log('\n==== DB checks (stress-report.sql) ====');
    const db = sqlFile(join(HERE, 'stress-report.sql'));
    console.log(db);

    const dir = join(HERE, 'runs', new Date().toISOString().replace(/[:.]/g, '-'));
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'calls.json'), JSON.stringify(calls, null, 1));
    writeFileSync(join(dir, 'reps.json'), JSON.stringify(results, null, 1));
    writeFileSync(join(dir, 'summary.txt'), lines.join('\n') + '\n\n==== DB checks ====\n' + db);
    console.log(`Saved to ${dir}`);
}

main().catch(e => { console.error(e.message); process.exit(1); });
