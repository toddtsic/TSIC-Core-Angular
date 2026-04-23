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
          'One or more of your automatic payments for !JOBNAME for !PERSON was declined.\n\n' +
          'You can contact your credit card issuer to determine the reason if you need to.\n\n' +
          'Then you can update your credit card information and process the current balance due (!AMTOWED) all in one step.\n\n' +
          'To fix this, visit !JOBLINK, then:\n\n' +
          '1. Login in the upper right corner using the username you used to register initially: !FAMILYUSERNAME\n' +
          '2. Select your Player\'s role\n' +
          '3. Open the avatar menu in the upper right and select \'Update CC Info\'\n' +
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
          'To fix this, visit !JOBLINK, then:\n\n' +
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
      },
      {
        label: 'Credit Card Expiring This Month',
        subject: 'Your Credit Card is Expiring — Action Required',
        body:
          'The credit card on file for your automatic recurring payments for !JOBNAME for !PERSON is expiring this month.\n\n' +
          'If we cannot bill the new card before your next scheduled payment, your auto-pay will fail.\n\n' +
          'To update your credit card, visit !JOBLINK, then:\n\n' +
          '1. Login in the upper right corner using the username you used to register initially: !FAMILYUSERNAME\n' +
          '2. Select your Player\'s role\n' +
          '3. Open the avatar menu in the upper right and select \'Update CC Info\'\n' +
          '4. Enter your credit card information and submit.',
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
          'This is a reminder that player insurance is available for !PERSON\'s registration in !JOBNAME, ' +
          'and your registration is not yet covered.\n\n' +
          'Player insurance protects your registration fees against covered cancellation events.\n\n' +
          'To add player insurance:\n\n' +
          '1. Visit !JOBLINK\n' +
          '2. Login using your username: !FAMILYUSERNAME\n' +
          '3. Select your Player\'s role\n' +
          '4. Follow the insurance prompts to complete the optional policy\n\n' +
          'If you have already decided to decline, no further action is needed.',
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
          'This is a reminder that team registration cancellation insurance is available for !JOBNAME, ' +
          'and one or more of the teams you manage is not yet covered.\n\n' +
          'Team insurance protects team registration fees against covered cancellation events, per team.\n\n' +
          'To add team insurance:\n\n' +
          '1. Visit !JOBLINK\n' +
          '2. Login using your username: !FAMILYUSERNAME\n' +
          '3. Select your Club Rep role\n' +
          '4. Review your teams and add insurance per team as desired\n\n' +
          'If you have already decided to decline for all teams, no further action is needed.',
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
          'Congratulations !PERSON!\n\n' +
          'You have been removed from the Waitlist for !TEAMNAME in !JOBNAME.\n\n' +
          'To accept your spot, please pay your balance due (!AMTOWED) as follows:\n\n' +
          'Visit !JOBLINK, then:\n\n' +
          '1. You MUST login in the upper right corner using the username you used to register initially: !FAMILYUSERNAME (do NOT re-register).\n' +
          '2. Select your Player\'s role\n' +
          '3. Under \'Player\' in the upper right, select \'Pay Balance Due\' and proceed to pay.',
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
