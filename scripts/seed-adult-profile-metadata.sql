-- ============================================================
-- Seed: Default AdultProfileMetadataJson for all jobs
-- ============================================================
-- Role-keyed JSON schema for adult registration dynamic fields.
-- Each role key maps to a fields array matching existing
-- Registrations entity columns.
--
-- Run after: add-adult-profile-metadata-column.sql
-- Safe to re-run: only updates jobs where column is NULL.
-- ============================================================

UPDATE Jobs.Jobs
SET AdultProfileMetadataJson = N'{
  "UnassignedAdult": {
    "fields": [
      {
        "name": "SportYearsExp",
        "dbColumn": "SportYearsExp",
        "displayName": "Years of Experience",
        "inputType": "TEXT",
        "order": 1,
        "visibility": "public",
        "validation": { "maxLength": 50 }
      },
      {
        "name": "CertNo",
        "dbColumn": "CertNo",
        "displayName": "Certification Number",
        "inputType": "TEXT",
        "order": 2,
        "visibility": "public",
        "validation": { "maxLength": 100 }
      },
      {
        "name": "CertDate",
        "dbColumn": "CertDate",
        "displayName": "Certification Date",
        "inputType": "DATE",
        "order": 3,
        "visibility": "public"
      },
      {
        "name": "BBgcheck",
        "dbColumn": "BBgcheck",
        "displayName": "Background Check Completed",
        "inputType": "CHECKBOX",
        "order": 4,
        "visibility": "public"
      },
      {
        "name": "BackcheckExplain",
        "dbColumn": "BackcheckExplain",
        "displayName": "Background Check Details",
        "inputType": "TEXTAREA",
        "order": 5,
        "visibility": "public",
        "validation": { "maxLength": 500 },
        "conditionalOn": { "field": "BBgcheck", "operator": "equals", "value": true }
      },
      {
        "name": "SpecialRequests",
        "dbColumn": "SpecialRequests",
        "displayName": "Special Requests or Notes",
        "inputType": "TEXTAREA",
        "order": 6,
        "visibility": "public",
        "validation": { "maxLength": 500 }
      }
    ]
  },
  "Referee": {
    "fields": [
      {
        "name": "SportAssnId",
        "dbColumn": "SportAssnId",
        "displayName": "Association ID",
        "inputType": "TEXT",
        "order": 1,
        "visibility": "public",
        "validation": { "maxLength": 100 }
      },
      {
        "name": "SportAssnIdexpDate",
        "dbColumn": "SportAssnIdexpDate",
        "displayName": "Association ID Expiration",
        "inputType": "DATE",
        "order": 2,
        "visibility": "public"
      },
      {
        "name": "CertNo",
        "dbColumn": "CertNo",
        "displayName": "Referee Certification Number",
        "inputType": "TEXT",
        "order": 3,
        "visibility": "public",
        "validation": { "maxLength": 100 }
      },
      {
        "name": "CertDate",
        "dbColumn": "CertDate",
        "displayName": "Certification Date",
        "inputType": "DATE",
        "order": 4,
        "visibility": "public"
      },
      {
        "name": "SportYearsExp",
        "dbColumn": "SportYearsExp",
        "displayName": "Years of Refereeing Experience",
        "inputType": "TEXT",
        "order": 5,
        "visibility": "public",
        "validation": { "maxLength": 50 }
      },
      {
        "name": "BBgcheck",
        "dbColumn": "BBgcheck",
        "displayName": "Background Check Completed",
        "inputType": "CHECKBOX",
        "order": 6,
        "visibility": "public"
      },
      {
        "name": "SpecialRequests",
        "dbColumn": "SpecialRequests",
        "displayName": "Special Requests or Notes",
        "inputType": "TEXTAREA",
        "order": 7,
        "visibility": "public",
        "validation": { "maxLength": 500 }
      }
    ]
  },
  "Recruiter": {
    "fields": [
      {
        "name": "RecruitingHandle",
        "dbColumn": "RecruitingHandle",
        "displayName": "Recruiting Platform Handle",
        "inputType": "TEXT",
        "order": 1,
        "visibility": "public",
        "validation": { "maxLength": 200 }
      },
      {
        "name": "SchoolName",
        "dbColumn": "SchoolName",
        "displayName": "College / University",
        "inputType": "TEXT",
        "order": 2,
        "visibility": "public",
        "validation": { "required": true, "maxLength": 200 }
      },
      {
        "name": "CollegeCommit",
        "dbColumn": "CollegeCommit",
        "displayName": "Division / Conference",
        "inputType": "TEXT",
        "order": 3,
        "visibility": "public",
        "validation": { "maxLength": 200 }
      },
      {
        "name": "SpecialRequests",
        "dbColumn": "SpecialRequests",
        "displayName": "Additional Notes",
        "inputType": "TEXTAREA",
        "order": 4,
        "visibility": "public",
        "validation": { "maxLength": 500 }
      }
    ]
  }
}'
WHERE AdultProfileMetadataJson IS NULL;

-- Verification
SELECT
    COUNT(*) AS TotalJobs,
    SUM(CASE WHEN AdultProfileMetadataJson IS NOT NULL THEN 1 ELSE 0 END) AS JobsWithMetadata
FROM Jobs.Jobs;
