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
const USLAX_ONLY: JobFlagsForTemplates = { ...NO_FLAGS, usLaxMembershipValidated: true };

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
      activeStatuses: ['True'],
      roleIds: [ROLE_ID_PLAYER]
    };
    expect(isTemplateAvailable(viPlayerTemplate, request, NO_FLAGS)).toBe(false);
    expect(isTemplateAvailable(viPlayerTemplate, request, VI_PLAYER_ONLY)).toBe(true);
  });

  it('returns false when required filter is missing', () => {
    const request: RegistrationSearchRequest = {
      activeStatuses: ['True'],
      roleIds: [ROLE_ID_PLAYER]
    };
    expect(isTemplateAvailable(viPlayerTemplate, request, VI_PLAYER_ONLY)).toBe(false);
  });

  it('returns false when required filter value does not match', () => {
    const request: RegistrationSearchRequest = {
      hasVIPlayerInsurance: true,
      activeStatuses: ['True'],
      roleIds: [ROLE_ID_PLAYER]
    };
    expect(isTemplateAvailable(viPlayerTemplate, request, VI_PLAYER_ONLY)).toBe(false);
  });

  it('returns false when activeStatuses is not ["True"]', () => {
    const request: RegistrationSearchRequest = {
      hasVIPlayerInsurance: false,
      activeStatuses: ['False'],
      roleIds: [ROLE_ID_PLAYER]
    };
    expect(isTemplateAvailable(viPlayerTemplate, request, VI_PLAYER_ONLY)).toBe(false);
  });

  it('returns false when another unrelated filter is active', () => {
    const request: RegistrationSearchRequest = {
      hasVIPlayerInsurance: false,
      activeStatuses: ['True'],
      roleIds: [ROLE_ID_PLAYER],
      clubNames: ['Any Club']
    };
    expect(isTemplateAvailable(viPlayerTemplate, request, VI_PLAYER_ONLY)).toBe(false);
  });

  it('exempts roleIds that match impliedRoleIds (auto-enacted)', () => {
    const request: RegistrationSearchRequest = {
      hasVIPlayerInsurance: false,
      activeStatuses: ['True'],
      roleIds: [ROLE_ID_PLAYER]
    };
    expect(isTemplateAvailable(viPlayerTemplate, request, VI_PLAYER_ONLY)).toBe(true);
  });

  it('role-id comparison is case-insensitive', () => {
    const request: RegistrationSearchRequest = {
      hasVIPlayerInsurance: false,
      activeStatuses: ['True'],
      roleIds: [ROLE_ID_PLAYER.toLowerCase()]
    };
    expect(isTemplateAvailable(viPlayerTemplate, request, VI_PLAYER_ONLY)).toBe(true);
  });

  it('does not exempt a DIFFERENT role from the implied set', () => {
    const request: RegistrationSearchRequest = {
      hasVIPlayerInsurance: false,
      activeStatuses: ['True'],
      roleIds: [ROLE_ID_CLUBREP]
    };
    expect(isTemplateAvailable(viPlayerTemplate, request, VI_PLAYER_ONLY)).toBe(false);
  });

  it('does not exempt a multi-role selection even if it contains the implied role', () => {
    const request: RegistrationSearchRequest = {
      hasVIPlayerInsurance: false,
      activeStatuses: ['True'],
      roleIds: [ROLE_ID_PLAYER, ROLE_ID_CLUBREP]
    };
    expect(isTemplateAvailable(viPlayerTemplate, request, VI_PLAYER_ONLY)).toBe(false);
  });

  it('VI Team template requires ClubRep role + offerTeamRegsaverInsurance', () => {
    const request: RegistrationSearchRequest = {
      hasVITeamInsurance: false,
      activeStatuses: ['True'],
      roleIds: [ROLE_ID_CLUBREP]
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

  it('USLax expired template requires usLaxMembershipValidated flag + expired status + Player role', () => {
    const usLaxExpired = findTemplate('Expired / Missing Membership');
    const goodRequest: RegistrationSearchRequest = {
      usLaxMembershipStatus: 'expired',
      activeStatuses: ['True'],
      roleIds: [ROLE_ID_PLAYER]
    };
    expect(isTemplateAvailable(usLaxExpired, goodRequest, USLAX_ONLY)).toBe(true);
    expect(isTemplateAvailable(usLaxExpired, goodRequest, NO_FLAGS)).toBe(false);

    const wrongRole: RegistrationSearchRequest = { ...goodRequest, roleIds: [ROLE_ID_CLUBREP] };
    expect(isTemplateAvailable(usLaxExpired, wrongRole, USLAX_ONLY)).toBe(false);

    const noStatus: RegistrationSearchRequest = { activeStatuses: ['True'], roleIds: [ROLE_ID_PLAYER] };
    expect(isTemplateAvailable(usLaxExpired, noStatus, USLAX_ONLY)).toBe(false);
  });
});
