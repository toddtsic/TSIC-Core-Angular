import { AllowedField } from './allowed-fields';

// Allowed fields for the ADULT (coach/volunteer, referee, recruiter) form designer.
//
// Every dbColumn below is a real Registrations property that survives FormValueMapper's write-side
// whitelist (IDs are dropped except SportAssnId; fees/system columns and BUploaded*/Adn*/Regsaver*
// are excluded). Player-only academics (gradYear, SAT/ACT, position-by-sport) are intentionally omitted.
//
// NOTE: sportAssnId / sportAssnIdexpDate are intentionally ABSENT. USA Lacrosse is an orthogonal
// per-job capability composed from the immutable RegformName_Coach (see AdultFormCatalog.UsLaxField):
// the migrator/editor prepend the required sportAssnId to USLax jobs' coach block automatically, and the
// type-scoped editor strips it on read / re-composes it on write. Offering it here would let a user add a
// field that is silently stripped on save. Extend this list freely — no editor code changes required.
export const ADULT_ALLOWED_FIELDS: AllowedField[] = [
    // Association / experience
    { name: 'sportYearsExp', displayName: 'Years of Experience', inputType: 'SELECT', visibility: 'public', dbColumn: 'SportYearsExp' },

    // Coach / volunteer
    { name: 'volposition', displayName: 'Volunteer Position', inputType: 'TEXT', visibility: 'public', dbColumn: 'Volposition' },
    { name: 'volChildreninprogram', displayName: 'Children in Program', inputType: 'TEXT', visibility: 'public', dbColumn: 'VolChildreninprogram' },
    { name: 'clubCoach', displayName: 'Club / Team', inputType: 'TEXT', visibility: 'public', dbColumn: 'ClubCoach' },
    { name: 'clubTeamName', displayName: 'Club Team Name', inputType: 'TEXT', visibility: 'public', dbColumn: 'ClubTeamName' },
    { name: 'clubName', displayName: 'Club Name', inputType: 'SELECT', visibility: 'public', dbColumn: 'ClubName' },

    // Referee / certification
    { name: 'certNo', displayName: 'Certification #', inputType: 'TEXT', visibility: 'public', dbColumn: 'CertNo' },
    { name: 'certDate', displayName: 'Certification Date', inputType: 'DATE', visibility: 'public', dbColumn: 'CertDate' },
    { name: 'position', displayName: 'Position', inputType: 'SELECT', visibility: 'public', dbColumn: 'Position' },

    // Background check
    { name: 'bgCheckDate', displayName: 'Background Check Date', inputType: 'DATE', visibility: 'adminOnly', dbColumn: 'BgCheckDate' },
    { name: 'backcheckExplain', displayName: 'Background Check Notes', inputType: 'TEXTAREA', visibility: 'adminOnly', dbColumn: 'BackcheckExplain' },

    // Recruiter / affiliation
    { name: 'schoolName', displayName: 'School / Organization', inputType: 'TEXT', visibility: 'public', dbColumn: 'SchoolName' },
    { name: 'region', displayName: 'Region', inputType: 'TEXT', visibility: 'public', dbColumn: 'Region' },
    { name: 'whoReferred', displayName: 'Referred By', inputType: 'TEXT', visibility: 'public', dbColumn: 'WhoReferred' },

    // Contact / social
    { name: 'twitter', displayName: 'Twitter', inputType: 'TEXT', visibility: 'public', dbColumn: 'Twitter' },
    { name: 'instagram', displayName: 'Instagram', inputType: 'TEXT', visibility: 'public', dbColumn: 'Instagram' },

    // Apparel (SELECT, options sourced from the adult-namespaced ListSizes_Coach* sets seeded into
    // Jobs.JsonOptions at migration — kept distinct from the player ListSizes_* keys so the coach form
    // does NOT inherit the player's size lists and directors edit the two independently).
    { name: 'tShirt', displayName: 'T‑Shirt Size', inputType: 'SELECT', visibility: 'public', dbColumn: 'TShirt', dataSource: 'ListSizes_CoachJersey' },
    { name: 'jerseySize', displayName: "Men's Shirt Size", inputType: 'SELECT', visibility: 'public', dbColumn: 'JerseySize', dataSource: 'ListSizes_CoachJersey' },
    { name: 'shortsSize', displayName: "Men's or Women's Short Size", inputType: 'SELECT', visibility: 'public', dbColumn: 'ShortsSize', dataSource: 'ListSizes_CoachShorts' },
    { name: 'sweatpants', displayName: "Men's Waist Size", inputType: 'SELECT', visibility: 'public', dbColumn: 'Sweatpants', dataSource: 'ListSizes_CoachWaist' },
    { name: 'shoes', displayName: 'Shoe Size', inputType: 'SELECT', visibility: 'public', dbColumn: 'Shoes', dataSource: 'ListSizes_CoachShoes' },

    // General
    { name: 'medicalNote', displayName: 'Medical Note', inputType: 'TEXTAREA', visibility: 'public', dbColumn: 'MedicalNote' },
    { name: 'specialRequests', displayName: 'Special Requests', inputType: 'TEXTAREA', visibility: 'public', dbColumn: 'SpecialRequests' },

    // Waivers (checkboxes)
    { name: 'bWaiverSigned1', displayName: 'Waiver Signed 1', inputType: 'CHECKBOX', visibility: 'public', dbColumn: 'BWaiverSigned1' },
    { name: 'bWaiverSigned2', displayName: 'Waiver Signed 2', inputType: 'CHECKBOX', visibility: 'public', dbColumn: 'BWaiverSigned2' },
    { name: 'bWaiverSigned3', displayName: 'Waiver Signed 3', inputType: 'CHECKBOX', visibility: 'public', dbColumn: 'BWaiverSigned3' },
    { name: 'bWaiverSignedCv19', displayName: 'COVID-19 Waiver Signed', inputType: 'CHECKBOX', visibility: 'public', dbColumn: 'BWaiverSignedCv19' }
];
