export type AllowedVisibility = 'public' | 'adminOnly' | 'hidden';

export interface AllowedField {
    name: string;
    displayName: string;
    inputType: 'TEXT' | 'TEXTAREA' | 'EMAIL' | 'NUMBER' | 'TEL' | 'DATE' | 'DATETIME' | 'CHECKBOX' | 'SELECT' | 'RADIO' | 'HIDDEN';
    visibility?: AllowedVisibility; // default 'public'
    dbColumn?: string; // default = name
    dataSource?: string; // optional for SELECT fields
    computed?: boolean;
}

// Static list of commonly used profile fields across TSIC Unify profiles.
// This list can be extended safely without code changes to the editor.
export const ALLOWED_PROFILE_FIELDS: AllowedField[] = [
    { name: 'act', displayName: 'ACT', inputType: 'TEXT', visibility: 'public' },
    { name: 'agegroupName', displayName: 'Age Group Name', inputType: 'SELECT', visibility: 'public' },
    { name: 'agerange', displayName: 'Agerange', inputType: 'HIDDEN', visibility: 'hidden' },
    { name: 'amtPaidToDate', displayName: 'Amt Paid To Date', inputType: 'HIDDEN', visibility: 'hidden' },
    { name: 'bAddProcessingFees', displayName: 'Add Processing Fees', inputType: 'HIDDEN', visibility: 'hidden' },
    { name: 'bCollegeCommit', displayName: 'College Commit', inputType: 'CHECKBOX', visibility: 'public' },
    { name: 'bUploadedMedForm', displayName: 'Uploaded Medical Form', inputType: 'CHECKBOX', visibility: 'public' },
    { name: 'bWaiverSigned1', displayName: 'Waiver Signed 1', inputType: 'CHECKBOX', visibility: 'public' },
    { name: 'bWaiverSigned2', displayName: 'Waiver Signed 2', inputType: 'CHECKBOX', visibility: 'public' },
    { name: 'bWaiverSigned3', displayName: 'Waiver Signed 3', inputType: 'CHECKBOX', visibility: 'public' },
    { name: 'bWaiverSignedCv19', displayName: 'COVID-19 Waiver Signed', inputType: 'CHECKBOX', visibility: 'public' },
    { name: 'classRank', displayName: 'Class Rank', inputType: 'TEXT', visibility: 'public' },
    { name: 'clubCoach', displayName: 'Club Coach', inputType: 'TEXT', visibility: 'public' },
    { name: 'clubCoachEmail', displayName: 'Club Coach Email', inputType: 'EMAIL', visibility: 'public' },
    { name: 'clubName', displayName: 'Club Name', inputType: 'SELECT', visibility: 'public' },
    { name: 'clubTeamName', displayName: 'Club Team Name', inputType: 'TEXT', visibility: 'public' },
    { name: 'collegeCommit', displayName: 'College Commit (Text)', inputType: 'TEXT', visibility: 'public' },
    { name: 'dayGroup', displayName: 'Day Group', inputType: 'TEXT', visibility: 'adminOnly' },
    { name: 'dob', displayName: 'DOB', inputType: 'HIDDEN', visibility: 'hidden' },
    { name: 'familyUserId', displayName: 'Family User ID', inputType: 'HIDDEN', visibility: 'hidden' },
    { name: 'firstName', displayName: 'First Name', inputType: 'HIDDEN', visibility: 'hidden' },
    { name: 'gender', displayName: 'Gender', inputType: 'HIDDEN', visibility: 'hidden' },
    { name: 'gpa', displayName: 'GPA', inputType: 'TEXT', visibility: 'public' },
    { name: 'gradYear', displayName: 'Grad Year', inputType: 'SELECT', visibility: 'public' },
    { name: 'heightInches', displayName: 'Height (inches)', inputType: 'SELECT', visibility: 'public' },
    { name: 'instagram', displayName: 'Instagram', inputType: 'TEXT', visibility: 'public' },
    { name: 'jerseySize', displayName: 'Jersey Size', inputType: 'SELECT', visibility: 'public' },
    { name: 'kilt', displayName: 'Kilt', inputType: 'SELECT', visibility: 'public' },
    { name: 'lastName', displayName: 'Last Name', inputType: 'HIDDEN', visibility: 'hidden' },
    { name: 'medicalNote', displayName: 'Medical Note', inputType: 'TEXT', visibility: 'public' },
    { name: 'playerLastName', displayName: 'Player Last Name', inputType: 'HIDDEN', visibility: 'hidden' },
    { name: 'playerUserId', displayName: 'Player User ID', inputType: 'HIDDEN', visibility: 'hidden' },
    { name: 'position', displayName: 'Position', inputType: 'SELECT', visibility: 'public' },
    { name: 'previousCoach1', displayName: 'Previous Coach 1', inputType: 'TEXT', visibility: 'public' },
    { name: 'recruitingHandle', displayName: 'Recruiting Handle', inputType: 'TEXT', visibility: 'public' },
    { name: 'regformName', displayName: 'Regform Name', inputType: 'HIDDEN', visibility: 'hidden' },
    { name: 'registrationId', displayName: 'Registration ID', inputType: 'HIDDEN', visibility: 'hidden' },
    { name: 'reversible', displayName: 'Reversible', inputType: 'SELECT', visibility: 'public' },
    { name: 'roommatePref', displayName: 'Roommate Preference', inputType: 'TEXT', visibility: 'public' },
    { name: 'sat', displayName: 'SAT', inputType: 'TEXT', visibility: 'public' },
    { name: 'satMath', displayName: 'SAT Math', inputType: 'TEXT', visibility: 'public' },
    { name: 'satVerbal', displayName: 'SAT Verbal', inputType: 'TEXT', visibility: 'public' },
    { name: 'satWriting', displayName: 'SAT Writing', inputType: 'TEXT', visibility: 'public' },
    { name: 'schoolCoach', displayName: 'School Coach', inputType: 'TEXT', visibility: 'public' },
    { name: 'schoolCoachEmail', displayName: 'School Coach Email', inputType: 'EMAIL', visibility: 'public' },
    { name: 'schoolGrade', displayName: 'School Grade', inputType: 'SELECT', visibility: 'public' },
    { name: 'schoolName', displayName: 'School Name', inputType: 'TEXT', visibility: 'public' },
    { name: 'shoes', displayName: 'Shoes', inputType: 'SELECT', visibility: 'public' },
    { name: 'shortsSize', displayName: 'Shorts Size', inputType: 'SELECT', visibility: 'public' },
    { name: 'skillLevel', displayName: 'Skill Level', inputType: 'SELECT', visibility: 'public' },
    { name: 'snapchat', displayName: 'Snapchat', inputType: 'TEXT', visibility: 'public' },
    { name: 'specialRequests', displayName: 'Special Requests', inputType: 'TEXT', visibility: 'public' },
    { name: 'sportAssnId', displayName: 'Sport Assn ID', inputType: 'SELECT', visibility: 'public' },
    { name: 'sportAssnIdexpDate', displayName: 'Sport Assn ID Exp Date', inputType: 'DATE', visibility: 'public' },
    { name: 'sportYearsExp', displayName: 'Sport Years Experience', inputType: 'SELECT', visibility: 'public' },
    { name: 'strongHand', displayName: 'Strong Hand', inputType: 'SELECT', visibility: 'public' },
    { name: 'sweatpants', displayName: 'Sweatpants', inputType: 'SELECT', visibility: 'public' },
    { name: 'sweatshirt', displayName: 'Sweatshirt', inputType: 'SELECT', visibility: 'public' },
    { name: 'teamId', displayName: 'Team', inputType: 'SELECT', visibility: 'public' },
    { name: 'tikTokHandle', displayName: 'TikTok Handle', inputType: 'TEXT', visibility: 'public' },
    { name: 'tShirt', displayName: 'T‑Shirt Size', inputType: 'SELECT', visibility: 'public' },
    { name: 'twitter', displayName: 'Twitter', inputType: 'TEXT', visibility: 'public' },
    { name: 'uniformNo', displayName: 'Uniform #', inputType: 'TEXT', visibility: 'public' },
    { name: 'weightLbs', displayName: 'Weight (lbs)', inputType: 'TEXT', visibility: 'public' }
];
