// Postgres port A/B HTTP sweep: every safe GET endpoint against the API running on Postgres and
// on SQL Server, same SuperUser login, responses compared after normalising known noise.
//
//   node tools/pg-ab/sweep/sweep.mjs [--baseline tools/pg-ab/sweep/baseline.json] [--write-baseline]
//
// Expects two API instances already running (scripts/pg-port-sync-check.ps1 -Verify starts them):
//   https://localhost:7301  DatabaseProvider=Postgres
//   https://localhost:7302  DatabaseProvider=SqlServer
// Verdicts: SAME, ORDER (same rows, different order), BODY, STATUS. Non-JSON responses compare
// by status only.
// With --baseline, exits non-zero only for endpoints whose verdict or status differs from it.
// The baseline holds path/verdict/status only, never response bodies.
import fs from 'node:fs';
import path from 'node:path';
process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';   // local dev certs only

const arg = n => { const i = process.argv.indexOf(n); return i > 0 ? process.argv[i + 1] : undefined; };
const baselineFile = arg('--baseline');
const writeBaseline = process.argv.includes('--write-baseline');
const PG = 'https://localhost:7301', SS = 'https://localhost:7302';
const USER = 'TSICSuperUser', PASSWORD = 'dev123', REG_ID = '32160261-C0E8-EF11-83F5-2CEA7F87E1C7';
// Real ids from lftc-summer-2026 (dev data). Endpoints needing any other route value are skipped.
const P = {
  jobPath: 'lftc-summer-2026', jobId: '66036C7B-E1E8-4110-977F-F786353F2498', tournamentJobId: '66036C7B-E1E8-4110-977F-F786353F2498',
  customerId: '150DB9A4-0E6D-43D4-A7F8-03DFC740CCD1', teamId: '2EE640D8-27E8-4BEA-B912-CEDCE82C79FE',
  agegroupId: 'A41AC4CB-1089-45D9-A3EE-EF785E0B020B', divId: 'B8D39A36-B70F-4458-B70D-87DB3B858EE1',
  leagueId: '289B265C-A91F-4720-9605-1466FB2716A4', clubRepRegId: '93A9DC07-D2DA-4F48-A004-24E763885A1C',
  regId: '133FC8FE-D7F1-4BD0-88CB-E8064B2CF60B', registrationId: '133FC8FE-D7F1-4BD0-88CB-E8064B2CF60B',
  userId: 'c2a62881-4097-47d9-9b49-f73d754110f6', playerUserId: 'c2a62881-4097-47d9-9b49-f73d754110f6',
  familyUserId: '818214e0-3d21-459b-b304-f1dfbc925c5d', gid: '284285', fieldId: '1647C611-2AB9-4733-94B2-2C04AB36F59F',
};
// Not swept: anything that might change state, call out, or stream. The SQL Server side is the real dev DB.
const RISKY = /(send|notify|sweep|purge|sync|charge|refund|delete|reset|import|clone|migrate|resend|rebuild|recalc|trigger|execute|run-|\/run|logout|revoke|refresh|stream|hub|ping|health|export-bold)/i;
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DT = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(\.\d+)?(Z|[+-]\d{2}:\d{2})?$/;

async function login(base) {
  const post = async (p, body, token) => {
    const r = await fetch(base + p, { method: 'POST', headers: { 'content-type': 'application/json', ...(token && { authorization: 'Bearer ' + token }) }, body: JSON.stringify(body) });
    if (!r.ok) throw new Error(`${base}${p} -> ${r.status}`);
    return r.json();
  };
  const first = await post('/api/auth/login', { username: USER, password: PASSWORD });
  return (await post('/api/auth/select-registration', { regId: REG_ID }, first.accessToken)).accessToken;
}

// Noise: traceId; GUID letter case (SQL Server upper-cases server-side GUID->string); datetimes cut
// to microseconds (Postgres precision); numbers via JSON.parse (decimal scale 0.0000 == 0).
function norm(v) {
  if (Array.isArray(v)) return v.map(norm);
  if (v && typeof v === 'object') return Object.fromEntries(Object.keys(v).filter(k => k !== 'traceId').sort().map(k => [k, norm(v[k])]));
  if (typeof v === 'string') {
    v = v.replace(/(Generated:)\s*\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}( UTC)?/g, '$1 <ts>');   // generated-script headers
    if (GUID.test(v)) return v.toLowerCase();
    const m = DT.exec(v); if (m) return m[1] + '.' + ((m[2] ?? '.').slice(1) + '000000').slice(0, 6) + (m[3] ?? '');
  }
  return v;
}
const sorted = v => Array.isArray(v) ? v.map(sorted).sort((a, b) => (JSON.stringify(a) < JSON.stringify(b) ? -1 : 1))
  : v && typeof v === 'object' ? Object.fromEntries(Object.entries(v).map(([k, x]) => [k, sorted(x)])) : v;
function firstDiff(a, b, at = '$') {
  if (JSON.stringify(a) === JSON.stringify(b)) return null;
  if (Array.isArray(a) && Array.isArray(b)) {
    if (a.length !== b.length) return `${at}: length PG=${a.length} SS=${b.length}`;
    for (let i = 0; i < a.length; i++) { const d = firstDiff(a[i], b[i], `${at}[${i}]`); if (d) return d; }
  } else if (a && b && typeof a === 'object' && typeof b === 'object') {
    for (const k of new Set([...Object.keys(a), ...Object.keys(b)])) { const d = firstDiff(a[k], b[k], `${at}.${k}`); if (d) return d; }
  }
  return at;   // value-level difference: location only, never the data
}

async function call(base, token, p) {
  try {
    const r = await fetch(base + p, { headers: { authorization: 'Bearer ' + token }, signal: AbortSignal.timeout(120_000) });
    const type = r.headers.get('content-type') ?? '';
    const raw = Buffer.from(await r.arrayBuffer());
    let body;
    if (type.includes('json')) { try { body = norm(JSON.parse(raw.toString('utf8'))); } catch { body = undefined; } }
    return { status: r.status, size: raw.length, body };
  } catch (e) { return { status: 'ERR', size: 0, error: String(e).slice(0, 120) }; }
}

const swagger = await (await fetch(PG + '/swagger/v1/swagger.json', { signal: AbortSignal.timeout(60_000) })).json();
const [pgToken, ssToken] = [await login(PG), await login(SS)];

const results = {};
let skippedRisky = 0, skippedParams = 0;
for (const [template, ops] of Object.entries(swagger.paths)) {
  if (!ops.get) continue;
  if (RISKY.test(template)) { skippedRisky++; continue; }
  if ([...template.matchAll(/\{(\w+)\}/g)].some(m => !(m[1] in P))) { skippedParams++; continue; }
  const url = template.replace(/\{(\w+)\}/g, (_, k) => encodeURIComponent(P[k]));
  const [pg, ss] = [await call(PG, pgToken, url), await call(SS, ssToken, url)];

  let verdict, where;
  if (pg.status !== ss.status) verdict = 'STATUS';
  // Files (Excel/PDF) and HTML error pages embed timestamps and stack traces, so their bytes
  // differ run to run: for non-JSON only the status is compared.
  else if (pg.body === undefined && ss.body === undefined) verdict = 'SAME';
  else if (!(where = firstDiff(pg.body, ss.body))) verdict = 'SAME';
  else if (!firstDiff(sorted(pg.body), sorted(ss.body))) verdict = 'ORDER';
  else { verdict = 'BODY'; where = firstDiff(sorted(pg.body), sorted(ss.body)); }
  results[template] = { verdict, pg: pg.status, ss: ss.status, ...(verdict === 'BODY' && { where }) };
}

const tally = Object.values(results).reduce((t, r) => ({ ...t, [r.verdict]: (t[r.verdict] ?? 0) + 1 }), {});
console.log(`swept ${Object.keys(results).length} (skipped: ${skippedRisky} risky, ${skippedParams} unknown route values)`, JSON.stringify(tally));
for (const [p, r] of Object.entries(results).filter(([, r]) => r.verdict === 'BODY')) console.log(`  BODY   ${p}  ${r.where}`);

let regressions = 0;
if (baselineFile && fs.existsSync(baselineFile)) {
  const base = JSON.parse(fs.readFileSync(baselineFile, 'utf8'));
  // Row order of ties is not stable from run to run, and order differences are accepted, so
  // ORDER and SAME count as the same outcome here.
  const outcome = v => (v === 'ORDER' ? 'SAME' : v);
  for (const [p, r] of Object.entries(results)) {
    const b = base[p];
    if (!b) { if (outcome(r.verdict) !== 'SAME') { console.log(`  NEW ENDPOINT ${r.verdict} ${r.pg}/${r.ss} ${p}`); regressions++; } continue; }
    if (outcome(b.verdict) !== outcome(r.verdict) || b.pg !== r.pg || b.ss !== r.ss) {
      console.log(`  CHANGED ${b.verdict} ${b.pg}/${b.ss} -> ${r.verdict} ${r.pg}/${r.ss}  ${p}`);
      regressions++;
    }
  }
  console.log(regressions === 0 ? 'No change from baseline.' : `${regressions} endpoint(s) changed from baseline.`);
}
if (writeBaseline) {
  const file = baselineFile ?? path.join(path.dirname(new URL(import.meta.url).pathname.replace(/^\/(\w:)/, '$1')), 'baseline.json');
  fs.writeFileSync(file, JSON.stringify(Object.fromEntries(Object.entries(results).map(([p, r]) => [p, { verdict: r.verdict, pg: r.pg, ss: r.ss }])), null, 1));
  console.log('Baseline written:', file);
}
process.exit(regressions === 0 ? 0 : 1);
