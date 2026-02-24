-- Add AdultProfileMetadataJson column to Jobs table
-- Stores role-keyed JSON schema for dynamic adult registration form fields
-- Format: { "UnassignedAdult": { "fields": [...] }, "Referee": { "fields": [...] }, "Recruiter": { "fields": [...] } }

IF COL_LENGTH('Jobs.Jobs', 'AdultProfileMetadataJson') IS NULL
BEGIN
    ALTER TABLE Jobs.Jobs ADD AdultProfileMetadataJson NVARCHAR(MAX) NULL;
    PRINT 'Added AdultProfileMetadataJson column to Jobs table.';
END
ELSE
BEGIN
    PRINT 'AdultProfileMetadataJson column already exists on Jobs table.';
END
