import { AllowedField } from '../profile-editor/allowed-fields';

// Allowed fields for the ADULT (coach/volunteer, referee, recruiter) form designer.
//
// Every dbColumn below is a real Registrations property that survives FormValueMapper's write-side
// whitelist (IDs are dropped except SportAssnId; fees/system columns and BUploaded*/Adn*/Regsaver*
// are excluded). Player-only academics (gradYear, SAT/ACT, position-by-sport, sizes catalogue) are
// intentionally omitted. Extend this list freely — no editor code changes required.
export const ADULT_ALLOWED_FIELDS: AllowedField[] = [
    // Identity / association
    { name: 'sportAssnId', displayName: 'Sport Assn ID (e.g. USA Lacrosse #)', inputType: 'TEXT', visibility: 'public', dbColumn: 'SportAssnId' },
    { name: 'sportAssnIdexpDate', displayName: 'Sport Assn ID Exp Date', inputType: 'DATE', visibility: 'public', dbColumn: 'SportAssnIdexpDate' },
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

    // Apparel
    { name: 'tShirt', displayName: 'T‑Shirt Size', inputType: 'SELECT', visibility: 'public', dbColumn: 'TShirt' },

    // General
    { name: 'medicalNote', displayName: 'Medical Note', inputType: 'TEXTAREA', visibility: 'public', dbColumn: 'MedicalNote' },
    { name: 'specialRequests', displayName: 'Special Requests', inputType: 'TEXTAREA', visibility: 'public', dbColumn: 'SpecialRequests' },

    // Waivers (checkboxes)
    { name: 'bWaiverSigned1', displayName: 'Waiver Signed 1', inputType: 'CHECKBOX', visibility: 'public', dbColumn: 'BWaiverSigned1' },
    { name: 'bWaiverSigned2', displayName: 'Waiver Signed 2', inputType: 'CHECKBOX', visibility: 'public', dbColumn: 'BWaiverSigned2' },
    { name: 'bWaiverSigned3', displayName: 'Waiver Signed 3', inputType: 'CHECKBOX', visibility: 'public', dbColumn: 'BWaiverSigned3' },
    { name: 'bWaiverSignedCv19', displayName: 'COVID-19 Waiver Signed', inputType: 'CHECKBOX', visibility: 'public', dbColumn: 'BWaiverSignedCv19' }
];
