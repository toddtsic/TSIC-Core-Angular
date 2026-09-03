/** Pre-built email templates for the batch-email modal. */

import type { RegistrationSearchRequest } from '@core/api';
import { RoleIds, RoleFilterSentinels } from '@infrastructure/constants/roles.constants';

// Feature-local aliases for the canonical role identifiers (single source: roles.constants.ts).
export const ROLE_ID_PLAYER = RoleIds.Player;
export const ROLE_ID_CLUBREP = RoleIds.ClubRep;
export const ROLE_FILTER_PLAYER_NOT_WAITLISTED = RoleFilterSentinels.PlayerNotWaitlisted;
export const ROLE_FILTER_CLUBREP_ACTIVE_TEAMS = RoleFilterSentinels.ClubRepActiveTeams;

/** True when a single role-filter value scopes the search to Players — the real Player role GUID
 *  or its "not waitlisted" sentinel. Case-insensitive, since backend GUID casing is not guaranteed. */
export function isPlayerRoleFilter(roleId: string): boolean {
  const v = roleId.toLowerCase();
  return v === ROLE_ID_PLAYER.toLowerCase() || v === ROLE_FILTER_PLAYER_NOT_WAITLISTED.toLowerCase();
}

/** True when a single role-filter value scopes the search to Club Reps — the real Club Rep role GUID
 *  or its "active teams" sentinel. Case-insensitive, since backend GUID casing is not guaranteed. */
export function isClubRepRoleFilter(roleId: string): boolean {
  const v = roleId.toLowerCase();
  return v === ROLE_ID_CLUBREP.toLowerCase() || v === ROLE_FILTER_CLUBREP_ACTIVE_TEAMS.toLowerCase();
}

export interface EmailTokenInfo {
  token: string;
  description: string;
}

/**
 * The substitution tokens offered by BOTH compose surfaces — the batch-email modal and the
 * registrant fly-in. Previously each hardcoded its own list and they drifted: the fly-in was
 * missing !SEASON / !SPORT / !CUSTOMERNAME (Ann, AM-059). One list, both consumers.
 *
 * Invite tokens are deliberately absent — they are SEEDED by the Invite action, which knows the
 * role and pre-places the right link. Offering them by hand let an admin drop the wrong
 * invitation, or an invite into a plain email.
 */
export const EMAIL_BASE_TOKENS: readonly EmailTokenInfo[] = [
  { token: '!PERSON', description: 'Contact person name' },
  { token: '!EMAIL', description: 'Contact email address' },
  { token: '!FAMILYUSERNAME', description: 'Recipient\'s login username' },
  { token: '!JOBNAME', description: 'League/Organization name' },
  { token: '!JOBLINK', description: 'Job name as a clickable link (e.g., "visit !JOBLINK")' },
  { token: '!AMTFEES', description: 'Total fees amount' },
  { token: '!AMTPAID', description: 'Amount paid' },
  { token: '!AMTOWED', description: 'Amount owed' },
  { token: '!SEASON', description: 'Season name' },
  { token: '!SPORT', description: 'Sport name' },
  { token: '!CUSTOMERNAME', description: 'Customer name' }
];

/** Offered only when the search is scoped to Club Reps. A club rep's registration IS their login
 *  account, so !USERNAME resolves to the rep's own login username. For players it resolves to the
 *  child record's username — NOT the family login (!FAMILYUSERNAME) — so offering it to player
 *  audiences invites "login with !USERNAME" mistakes; hence gated, not on the base list. */
export const CLUBREP_USERNAME_TOKEN: EmailTokenInfo = {
  token: '!USERNAME',
  description: 'Club rep\'s login username'
};

/** Offered only when the job validates USA Lacrosse membership. */
export const USLAX_VALID_THROUGH_TOKEN: EmailTokenInfo = {
  token: '!USLAXVALIDTHROUGHDATE',
  description: 'USA Lacrosse membership must be valid through this date'
};

/** Offered only for a registrant who actually has an ARB subscription — meaningless otherwise,
 *  which is why these stay off the base list rather than resolving to blanks for everyone else. */
export const SUBSCRIPTION_TOKENS: readonly EmailTokenInfo[] = [
  { token: '!SUBSCRIPTIONID', description: 'Authorize.net recurring-billing subscription ID' },
  { token: '!SUBSCRIPTIONSTATUS', description: 'Recurring-billing subscription status' }
];

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
  /** True when the job has a USLax membership-validation window configured (UslaxNumberValidThroughDate). */
  usLaxMembershipValidated: boolean;
}

/** Transient UI modes that gate template availability beyond search-request state.
 *  Today there's only one: "cardExpiring" — set while the grid is showing results
 *  from a live Authorize.net card-expiring-this-month lookup. */
export type TemplateMode = 'cardExpiring';

export interface TemplateModes {
  cardExpiring?: boolean;
}

/**
 * Availability rule for a template. When present, the template is offered only if:
 *   - every flag in `requiresJobFlags` is true on the job flags object, AND
 *   - EVERY filter in `requiresFilters` matches the search request, AND
 *   - every mode in `requiresModes` is active in the current UI context.
 *
 * The model is: **defaults + required filters = baseline**. Additional user
 * narrowings (gender, club, agegroup, etc.) are allowed — the template's scope
 * is already established by its required filters; narrower audiences within
 * that scope are legitimate segmentation, not "inappropriate targeting."
 */
export interface TemplateAvailability {
  requiresJobFlags: readonly (keyof JobFlagsForTemplates)[];
  requiresFilters: readonly { key: keyof RegistrationSearchRequest; value: unknown }[];
  requiresModes?: readonly TemplateMode[];
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

/** Value equality tolerant of arrays (order-sensitive). Case-insensitive for
 *  string elements so role-GUID comparisons work regardless of backend format. */
function filterValueMatches(actual: unknown, expected: unknown): boolean {
  if (Array.isArray(actual) && Array.isArray(expected)) {
    if (actual.length !== expected.length) return false;
    return actual.every((v, i) => stringCompareInsensitive(v, expected[i]));
  }
  return stringCompareInsensitive(actual, expected);
}

function stringCompareInsensitive(a: unknown, b: unknown): boolean {
  if (typeof a === 'string' && typeof b === 'string') return a.toLowerCase() === b.toLowerCase();
  return a === b;
}

export function isTemplateAvailable(
  template: EmailTemplate,
  searchRequest: RegistrationSearchRequest,
  jobFlags: JobFlagsForTemplates | null,
  modes: TemplateModes = {}
): boolean {
  const rule = template.availability;
  if (!rule) return true;

  if (rule.requiresJobFlags.length > 0) {
    if (!jobFlags) return false;
    for (const flag of rule.requiresJobFlags) {
      if (!jobFlags[flag]) return false;
    }
  }

  for (const req of rule.requiresFilters) {
    if (!filterValueMatches(searchRequest[req.key], req.value)) return false;
  }

  if (rule.requiresModes && rule.requiresModes.length > 0) {
    for (const mode of rule.requiresModes) {
      if (!modes[mode]) return false;
    }
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
          '<p>One or more of your automatic payments for !JOBNAME for !PERSON was declined.</p>' +
          '<p>You can contact your credit card issuer to determine the reason if you need to.</p>' +
          '<p>Then you can update your credit card information and process the current balance due (!AMTOWED) all in one step.</p>' +
          '<p>To fix this, visit !JOBLINK, then:</p>' +
          '<ol>' +
          '<li>Login in the upper right corner using the username you used to register initially: !FAMILYUSERNAME</li>' +
          '<li>Select your Player\'s role</li>' +
          '<li>Open the avatar menu in the upper right and select \'Update CC Info\'</li>' +
          '<li>Your <b>Balance Due</b> is shown near the top of the page. Enter your credit card information below it.</li>' +
          '<li>Click <b>Update Card &amp; Pay Balance</b> to make the payment and reactivate your future automatic payments.</li>' +
          '</ol>',
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
          '<p>One or more of your automatic payments for !JOBNAME for !PERSON was declined.</p>' +
          '<p>You can contact your credit card issuer to determine the reason if you need to.</p>' +
          '<p>Then you can update your credit card information and process the current balance due (!AMTOWED) all in one step.</p>' +
          '<p>To fix this, visit !JOBLINK, then:</p>' +
          '<ol>' +
          '<li>Login in the upper right corner using the username you used to register initially: !FAMILYUSERNAME</li>' +
          '<li>Select your Player\'s role</li>' +
          '<li>Under \'Player\' in the upper right, select \'Pay Balance Due\'</li>' +
          '</ol>',
        availability: {
          requiresJobFlags: ['adnArb'],
          requiresFilters: [
            { key: 'arbHealthStatus', value: 'behind-expired' },
            ACTIVE_ONLY
          ]
        }
      },
      {
        label: 'Credit Card Expiring This Month',
        subject: 'Your Credit Card is Expiring — Action Required',
        body:
          '<p>The credit card on file for your automatic recurring payments for !JOBNAME for !PERSON is expiring this month.</p>' +
          '<p>If we cannot bill the new card before your next scheduled payment, your auto-pay will fail.</p>' +
          '<p>To update your credit card, visit !JOBLINK, then:</p>' +
          '<ol>' +
          '<li>Login in the upper right corner using the username you used to register initially: !FAMILYUSERNAME</li>' +
          '<li>Select your Player\'s role</li>' +
          '<li>Open the avatar menu in the upper right and select \'Update CC Info\'</li>' +
          '<li>Enter your credit card information, then click <b>Update Card &amp; Pay Balance</b>.</li>' +
          '</ol>',
        availability: {
          // Gated by mode: only offered when the grid is showing lookup results from
          // the live Authorize.net card-expiring query. Dropped / inactive registrants
          // can be recipients — no activeStatuses filter here by design.
          requiresJobFlags: ['adnArb'],
          requiresFilters: [],
          requiresModes: ['cardExpiring']
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
          '<p>This is a reminder that player insurance is available for !PERSON\'s registration in !JOBNAME, ' +
          'and your registration is not yet covered.</p>' +
          '<p>Player insurance protects your registration fees against covered cancellation events.</p>' +
          '<p>To add player insurance:</p>' +
          '<ol>' +
          '<li>Visit !JOBLINK</li>' +
          '<li>Login using your username: !FAMILYUSERNAME</li>' +
          '<li>Select your Player\'s role</li>' +
          '<li>Follow the insurance prompts to complete the optional policy</li>' +
          '</ol>' +
          '<p>If you have already decided to decline, no further action is needed.</p>',
        availability: {
          requiresJobFlags: ['offerPlayerRegsaverInsurance'],
          requiresFilters: [
            { key: 'hasVIPlayerInsurance', value: false },
            ACTIVE_ONLY
          ]
        }
      },
      {
        label: 'Team Insurance — Not Yet Accepted (Club Reps)',
        subject: 'Team Insurance Available for !JOBNAME',
        body:
          '<p>This is a reminder that team registration cancellation insurance is available for !JOBNAME, ' +
          'and one or more of the teams you manage is not yet covered.</p>' +
          '<p>Team insurance protects team registration fees against covered cancellation events, per team.</p>' +
          '<p>To add team insurance:</p>' +
          '<ol>' +
          '<li>Visit !JOBLINK</li>' +
          '<li>Login using your username: !FAMILYUSERNAME</li>' +
          '<li>Select your Club Rep role</li>' +
          '<li>Review your teams and add insurance per team as desired</li>' +
          '</ol>' +
          '<p>If you have already decided to decline for all teams, no further action is needed.</p>',
        availability: {
          requiresJobFlags: ['offerTeamRegsaverInsurance'],
          requiresFilters: [
            { key: 'hasVITeamInsurance', value: false },
            ACTIVE_ONLY
          ]
        }
      }
    ]
  },
  // USA Lacrosse membership emails live on the dedicated reconciliation tool page
  // (views/tools/uslax-membership). That page owns its own inline compose panel
  // with row-level tokens the shared batch-email pipeline can't resolve.
  {
    category: 'Waitlist',
    templates: [
      {
        label: 'Activation (Off the Waitlist)',
        subject: 'You\'re off the waitlist for !JOBNAME',
        body:
          '<p>Congratulations !PERSON!</p>' +
          '<p>You have been removed from the Waitlist for !TEAMNAME in !JOBNAME.</p>' +
          '<p>To accept your spot, please pay your balance due (!AMTOWED) as follows:</p>' +
          '<p>Visit !JOBLINK, then:</p>' +
          '<ol>' +
          '<li>You MUST login in the upper right corner using the username you used to register initially: !FAMILYUSERNAME (do NOT re-register).</li>' +
          '<li>Select your Player\'s role</li>' +
          '<li>Under \'Player\' in the upper right, select \'Pay Balance Due\' and proceed to pay.</li>' +
          '</ol>',
        availability: {
          requiresJobFlags: [],
          requiresFilters: [
            { key: 'roleIds', value: [ROLE_ID_PLAYER] },
            ACTIVE_ONLY
          ]
        }
      }
    ]
  }
];
