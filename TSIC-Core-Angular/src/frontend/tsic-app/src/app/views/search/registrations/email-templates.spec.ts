import type { RegistrationSearchRequest } from '@core/api';
import {
  EMAIL_TEMPLATE_CATEGORIES,
  isTemplateAvailable,
  ROLE_ID_PLAYER,
  ROLE_ID_CLUBREP,
  type EmailTemplate,
  type JobFlagsForTemplates
} from './email-templates';

const NO_FLAGS: JobFlagsForTemplates = {
  offerPlayerRegsaverInsurance: false,
  offerTeamRegsaverInsurance: false,
  adnArb: false,
  usLaxMembershipValidated: false
};
const VI_PLAYER_ONLY: JobFlagsForTemplates = { ...NO_FLAGS, offerPlayerRegsaverInsurance: true };
const VI_TEAM_ONLY: JobFlagsForTemplates = { ...NO_FLAGS, offerTeamRegsaverInsurance: true };
const ARB_ONLY: JobFlagsForTemplates = { ...NO_FLAGS, adnArb: true };

const EMPTY_REQUEST: RegistrationSearchRequest = {};

function findTemplate(label: string): EmailTemplate {
  for (const cat of EMAIL_TEMPLATE_CATEGORIES) {
    const t = cat.templates.find(t => t.label === label);
    if (t) return t;
  }
  throw new Error(`Template not found: ${label}`);
}

describe('isTemplateAvailable', () => {
  const viPlayerTemplate = findTemplate('Player Insurance — Not Yet Accepted');
  const viTeamTemplate = findTemplate('Team Insurance — Not Yet Accepted (Club Reps)');
  const arbBehindActive = findTemplate('Update CC Info (Active/Suspended)');
  it('templates without availability are always available', () => {
    const alwaysAvailable: EmailTemplate = { label: 'x', subject: 'y', body: 'z' };
    expect(isTemplateAvailable(alwaysAvailable, EMPTY_REQUEST, NO_FLAGS)).toBe(true);
    expect(isTemplateAvailable(alwaysAvailable, EMPTY_REQUEST, null)).toBe(true);
  });

  it('returns false when jobFlags is null and rule has requiresJobFlags', () => {
    expect(isTemplateAvailable(viPlayerTemplate, EMPTY_REQUEST, null)).toBe(false);
  });

  it('returns false when a required job flag is not set', () => {
    const request: RegistrationSearchRequest = {
      hasVIPlayerInsurance: false,
      activeStatuses: ['True']
    };
    expect(isTemplateAvailable(viPlayerTemplate, request, NO_FLAGS)).toBe(false);
    expect(isTemplateAvailable(viPlayerTemplate, request, VI_PLAYER_ONLY)).toBe(true);
  });

  it('returns false when required filter is missing', () => {
    const request: RegistrationSearchRequest = {
      activeStatuses: ['True']
    };
    expect(isTemplateAvailable(viPlayerTemplate, request, VI_PLAYER_ONLY)).toBe(false);
  });

  it('returns false when required filter value does not match', () => {
    const request: RegistrationSearchRequest = {
      hasVIPlayerInsurance: true,
      activeStatuses: ['True']
    };
    expect(isTemplateAvailable(viPlayerTemplate, request, VI_PLAYER_ONLY)).toBe(false);
  });

  it('returns false when activeStatuses is not ["True"]', () => {
    const request: RegistrationSearchRequest = {
      hasVIPlayerInsurance: false,
      activeStatuses: ['False']
    };
    expect(isTemplateAvailable(viPlayerTemplate, request, VI_PLAYER_ONLY)).toBe(false);
  });

  it('allows user-added narrowings (club, gender, agegroup, etc.) when required filters match', () => {
    const request: RegistrationSearchRequest = {
      hasVIPlayerInsurance: false,
      activeStatuses: ['True'],
      clubNames: ['Any Club'],
      genders: ['F']
    };
    expect(isTemplateAvailable(viPlayerTemplate, request, VI_PLAYER_ONLY)).toBe(true);
  });

  it('allows defaults baseline (genders pre-selected, active pre-selected) without blocking', () => {
    const request: RegistrationSearchRequest = {
      hasVIPlayerInsurance: false,
      activeStatuses: ['True'],
      genders: ['F', 'M'],
      roleIds: [ROLE_ID_PLAYER]
    };
    expect(isTemplateAvailable(viPlayerTemplate, request, VI_PLAYER_ONLY)).toBe(true);
  });

  it('VI Team template requires offerTeamRegsaverInsurance flag', () => {
    const request: RegistrationSearchRequest = {
      hasVITeamInsurance: false,
      activeStatuses: ['True']
    };
    expect(isTemplateAvailable(viTeamTemplate, request, VI_TEAM_ONLY)).toBe(true);
    expect(isTemplateAvailable(viTeamTemplate, request, VI_PLAYER_ONLY)).toBe(false);
  });

  it('ARB template requires adnArb flag and arbHealthStatus value', () => {
    const goodRequest: RegistrationSearchRequest = {
      arbHealthStatus: 'behind-active',
      activeStatuses: ['True']
    };
    const wrongValue: RegistrationSearchRequest = {
      arbHealthStatus: 'behind-expired',
      activeStatuses: ['True']
    };
    expect(isTemplateAvailable(arbBehindActive, goodRequest, ARB_ONLY)).toBe(true);
    expect(isTemplateAvailable(arbBehindActive, goodRequest, NO_FLAGS)).toBe(false);
    expect(isTemplateAvailable(arbBehindActive, wrongValue, ARB_ONLY)).toBe(false);
  });

  it('CC Expiring template requires adnArb + cardExpiring mode, ignores filter state', () => {
    const ccExpiring = findTemplate('Credit Card Expiring This Month');
    const emptyRequest: RegistrationSearchRequest = {};

    // Job flag required
    expect(isTemplateAvailable(ccExpiring, emptyRequest, NO_FLAGS, { cardExpiring: true })).toBe(false);

    // Mode flag required
    expect(isTemplateAvailable(ccExpiring, emptyRequest, ARB_ONLY, { cardExpiring: false })).toBe(false);
    expect(isTemplateAvailable(ccExpiring, emptyRequest, ARB_ONLY, {})).toBe(false);

    // Both present → available
    expect(isTemplateAvailable(ccExpiring, emptyRequest, ARB_ONLY, { cardExpiring: true })).toBe(true);

    // Filter state is ignored — even with inactive-only it still shows
    const inactiveOnly: RegistrationSearchRequest = { activeStatuses: ['False'] };
    expect(isTemplateAvailable(ccExpiring, inactiveOnly, ARB_ONLY, { cardExpiring: true })).toBe(true);
  });

  it('Waitlist activation template requires Player role + Active', () => {
    const waitlistActivation = findTemplate('Activation (Off the Waitlist)');

    const goodRequest: RegistrationSearchRequest = {
      roleIds: [ROLE_ID_PLAYER],
      activeStatuses: ['True']
    };
    expect(isTemplateAvailable(waitlistActivation, goodRequest, NO_FLAGS)).toBe(true);

    // Lowercase GUID (backend format) must still match
    const lowercaseRoleRequest: RegistrationSearchRequest = {
      roleIds: [ROLE_ID_PLAYER.toLowerCase()],
      activeStatuses: ['True']
    };
    expect(isTemplateAvailable(waitlistActivation, lowercaseRoleRequest, NO_FLAGS)).toBe(true);

    // Wrong role — blocks
    const clubRepRequest: RegistrationSearchRequest = {
      roleIds: [ROLE_ID_CLUBREP],
      activeStatuses: ['True']
    };
    expect(isTemplateAvailable(waitlistActivation, clubRepRequest, NO_FLAGS)).toBe(false);

    // Inactive — blocks
    const inactiveRequest: RegistrationSearchRequest = {
      roleIds: [ROLE_ID_PLAYER],
      activeStatuses: ['False']
    };
    expect(isTemplateAvailable(waitlistActivation, inactiveRequest, NO_FLAGS)).toBe(false);
  });

});
