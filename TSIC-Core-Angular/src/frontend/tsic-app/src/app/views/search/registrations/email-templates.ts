/** Pre-built email templates for the batch-email modal. */

import type { RegistrationSearchRequest } from '@core/api';

/** Role IDs — must match TSIC.Domain.Constants.RoleConstants. */
export const ROLE_ID_PLAYER = 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A';
export const ROLE_ID_CLUBREP = '6A26171F-4D94-4928-94FA-2FEFD42C3C3E';

/**
 * Job-level feature flags the template availability evaluator cares about.
 * Built by the caller (usually the search component) from the pulse and/or
 * JobMetadataResponse — decouples templates from any single source DTO so new
 * flags can be added without churning JobPulseDto.
 */
export interface JobFlagsForTemplates {
  offerPlayerRegsaverInsurance: boolean;
  offerTeamRegsaverInsurance: boolean;
  adnArb: boolean;
  /** True for Lacrosse jobs that have a USLax membership-validation window configured. */
  usLaxMembershipValidated: boolean;
}

/**
 * Availability rule for a template. When present, the template is offered only if:
 *   - every flag in `requiresJobFlags` is true on the job flags object, AND
 *   - EVERY filter in `requiresFilters` matches the search request, AND
 *   - no OTHER filter listed in `ACTIVE_FILTER_KEYS` is set, EXCEPT that
 *     `roleIds` equal to `impliedRoleIds` is exempt (auto-enacted by the scoped filter).
 *
 * Strictness is intentional: product-specific copy (insurance, billing) must
 * not reach a hand-filtered subset that might include recipients it wasn't
 * written for.
 */
export interface TemplateAvailability {
  requiresJobFlags: readonly (keyof JobFlagsForTemplates)[];
  requiresFilters: readonly { key: keyof RegistrationSearchRequest; value: unknown }[];
  /** Role IDs the scoped filter auto-enacts; exempt from the "no other filter" check when matched exactly. */
  impliedRoleIds?: readonly string[];
}

export interface EmailTemplate {
  label: string;
  subject: string;
  body: string;
  /** Undefined = always available. */
  availability?: TemplateAvailability;
}

export interface EmailTemplateCategory {
  category: string;
  templates: EmailTemplate[];
}

/**
 * Filter keys evaluated by the "no OTHER filter is active" check.
 * Explicit (not introspected from the DTO) so pagination/sort fields never leak in
 * and so renaming a DTO field surfaces as a compile-time error here.
 */
export const ACTIVE_FILTER_KEYS = [
  'name', 'email', 'phone', 'schoolName', 'invoiceNumber',
  'roleIds', 'teamIds', 'agegroupIds', 'divisionIds', 'clubNames',
  'genders', 'positions', 'gradYears', 'grades', 'ageRangeIds',
  'activeStatuses', 'payStatuses', 'arbSubscriptionStatuses',
  'mobileRegistrationRoles', 'paymentTypes',
  'regDateFrom', 'regDateTo',
  'rosterThreshold', 'rosterThresholdClubNames',
  'cadtTeamIds',
  'hasVIPlayerInsurance', 'hasVITeamInsurance',
  'arbHealthStatus',
  'usLaxMembershipStatus'
] as const satisfies readonly (keyof RegistrationSearchRequest)[];

// Compile-time exhaustiveness check: if a new field is added to
// RegistrationSearchRequest and not listed above, the next line fails with a
// type error naming the missing key. Keep ACTIVE_FILTER_KEYS in sync.
type _MissingFilterKeys = Exclude<keyof RegistrationSearchRequest, typeof ACTIVE_FILTER_KEYS[number]>;
// eslint-disable-next-line @typescript-eslint/no-unused-vars
const _ACTIVE_FILTER_KEYS_EXHAUSTIVE: [_MissingFilterKeys] extends [never] ? true : _MissingFilterKeys = true;

function filterIsActive(value: unknown): boolean {
  if (value == null) return false;
  if (typeof value === 'string') return value.length > 0;
  if (Array.isArray(value)) return value.length > 0;
  return true;
}

/** Value equality tolerant of arrays (order-sensitive). */
function filterValueMatches(actual: unknown, expected: unknown): boolean {
  if (Array.isArray(actual) && Array.isArray(expected)) {
    if (actual.length !== expected.length) return false;
    return actual.every((v, i) => v === expected[i]);
  }
  return actual === expected;
}

/** Case-insensitive compare — GUIDs may come from the DB in either case. */
function sameRoleIds(a: readonly string[] | null | undefined, b: readonly string[]): boolean {
  if (!a || a.length !== b.length) return false;
  const normA = a.map(s => s.toUpperCase()).sort();
  const normB = b.map(s => s.toUpperCase()).sort();
  return normA.every((v, i) => v === normB[i]);
}

export function isTemplateAvailable(
  template: EmailTemplate,
  searchRequest: RegistrationSearchRequest,
  jobFlags: JobFlagsForTemplates | null
): boolean {
  const rule = template.availability;
  if (!rule) return true;

  if (!jobFlags) return false;
  for (const flag of rule.requiresJobFlags) {
    if (!jobFlags[flag]) return false;
  }

  for (const req of rule.requiresFilters) {
    if (!filterValueMatches(searchRequest[req.key], req.value)) return false;
  }

  const requiredKeys = new Set(rule.requiresFilters.map(r => r.key));
  for (const key of ACTIVE_FILTER_KEYS) {
    if (requiredKeys.has(key)) continue;
    if (key === 'roleIds' && rule.impliedRoleIds && sameRoleIds(searchRequest.roleIds, rule.impliedRoleIds)) continue;
    if (filterIsActive(searchRequest[key])) return false;
  }

  return true;
}

const ACTIVE_ONLY: { key: keyof RegistrationSearchRequest; value: unknown } = {
  key: 'activeStatuses',
  value: ['True']
};

/**
 * Templates adapted from ARB Health dashboard (arb-health.component.ts).
 * Tokens use batch-email-compatible names (!PERSON, !AMTOWED, etc.)
 * so they resolve through the standard TextSubstitutionService pipeline.
 */
export const EMAIL_TEMPLATE_CATEGORIES: EmailTemplateCategory[] = [
  {
    category: 'ARB — Behind in Payment',
    templates: [
      {
        label: 'Update CC Info (Active/Suspended)',
        subject: 'Action Required: Update Your Payment Information',
        body:
          'One or more of your automatic payments for !JOBNAME for !PERSON was declined.\n\n' +
          'You can contact your credit card issuer to determine the reason if you need to.\n\n' +
          'Then you can update your credit card information and process the current balance due (!AMTOWED) all in one step.\n\n' +
          'Please !JOBLINK then:\n\n' +
          '1. Login in the upper right corner using the username you used to register initially: !FAMILYUSERNAME\n' +
          '2. Select your Player\'s role\n' +
          '3. Under \'Player\' in the upper right, select \'Update CC Info (will also pay for failed auto-payments)\'\n' +
          '4. Enter your credit card information and you will see the amount due at the bottom of the screen.\n' +
          '5. Click Submit to make the payment and reactivate your future automatic payments.',
        availability: {
          requiresJobFlags: ['adnArb'],
          requiresFilters: [
            { key: 'arbHealthStatus', value: 'behind-active' },
            ACTIVE_ONLY
          ]
        }
      },
      {
        label: 'Pay Balance Due (Expired/Terminated)',
        subject: 'Action Required: Pay Balance Due',
        body:
          'One or more of your automatic payments for !JOBNAME for !PERSON was declined.\n\n' +
          'You can contact your credit card issuer to determine the reason if you need to.\n\n' +
          'Then you can update your credit card information and process the current balance due (!AMTOWED) all in one step.\n\n' +
          'Please !JOBLINK then:\n\n' +
          '1. Login in the upper right corner using the username you used to register initially: !FAMILYUSERNAME\n' +
          '2. Select your Player\'s role\n' +
          '3. Under \'Player\' in the upper right, select \'Pay Balance Due\'',
        availability: {
          requiresJobFlags: ['adnArb'],
          requiresFilters: [
            { key: 'arbHealthStatus', value: 'behind-expired' },
            ACTIVE_ONLY
          ]
        }
      }
    ]
  },
  {
    category: 'Vertical Insure',
    templates: [
      {
        label: 'Player Insurance — Not Yet Accepted',
        subject: 'Player Insurance Available for !JOBNAME',
        body:
          'This is a reminder that player insurance is available for !PERSON\'s registration in !JOBNAME, ' +
          'and your registration is not yet covered.\n\n' +
          'Player insurance protects your registration fees against covered cancellation events.\n\n' +
          'To add player insurance:\n\n' +
          '1. Please !JOBLINK\n' +
          '2. Login using your username: !FAMILYUSERNAME\n' +
          '3. Select your Player\'s role\n' +
          '4. Follow the insurance prompts to complete the optional policy\n\n' +
          'If you have already decided to decline, no further action is needed.',
        availability: {
          requiresJobFlags: ['offerPlayerRegsaverInsurance'],
          requiresFilters: [
            { key: 'hasVIPlayerInsurance', value: false },
            ACTIVE_ONLY
          ],
          impliedRoleIds: [ROLE_ID_PLAYER]
        }
      },
      {
        label: 'Team Insurance — Not Yet Accepted (Club Reps)',
        subject: 'Team Insurance Available for !JOBNAME',
        body:
          'This is a reminder that team registration cancellation insurance is available for !JOBNAME, ' +
          'and one or more of the teams you manage is not yet covered.\n\n' +
          'Team insurance protects team registration fees against covered cancellation events, per team.\n\n' +
          'To add team insurance:\n\n' +
          '1. Please !JOBLINK\n' +
          '2. Login using your username: !FAMILYUSERNAME\n' +
          '3. Select your Club Rep role\n' +
          '4. Review your teams and add insurance per team as desired\n\n' +
          'If you have already decided to decline for all teams, no further action is needed.',
        availability: {
          requiresJobFlags: ['offerTeamRegsaverInsurance'],
          requiresFilters: [
            { key: 'hasVITeamInsurance', value: false },
            ACTIVE_ONLY
          ],
          impliedRoleIds: [ROLE_ID_CLUBREP]
        }
      }
    ]
  },
  {
    category: 'USLax Membership',
    templates: [
      {
        label: 'Expired / Missing Membership',
        subject: 'USA Lacrosse Membership Needed for !JOBNAME',
        body:
          'Our records indicate that !PERSON\'s USA Lacrosse membership either has no expiration on file ' +
          'or expires before the date required for !JOBNAME (valid through !USLAXVALIDTHROUGHDATE).\n\n' +
          'A current USA Lacrosse membership is required to participate.\n\n' +
          'To renew or update your membership:\n\n' +
          '1. Visit https://account.usalacrosse.com/login and renew through USA Lacrosse directly.\n' +
          '2. Once renewed, !JOBLINK and login using your username: !FAMILYUSERNAME\n' +
          '3. Select your Player\'s role\n' +
          '4. Open \'Player Registration\' and confirm your USA Lacrosse number and expiration are up to date\n' +
          '5. Submit to save changes\n\n' +
          'If you believe this message is in error (for example, you have recently renewed), please update your ' +
          'membership number on your registration — we will re-verify against USA Lacrosse.\n\n' +
          'Questions about USA Lacrosse membership: membership@usalacrosse.com or 410-235-6882.',
        availability: {
          requiresJobFlags: ['usLaxMembershipValidated'],
          requiresFilters: [
            { key: 'usLaxMembershipStatus', value: 'expired' },
            ACTIVE_ONLY
          ],
          impliedRoleIds: [ROLE_ID_PLAYER]
        }
      }
    ]
  }
];
