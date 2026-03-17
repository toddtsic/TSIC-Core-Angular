-- Migration: Add BracketDepth to AgegroupScheduleProfile
-- Tracks which bracket level each agegroup used in prior year
-- Values: 'F' (finals), 'S' (semis), 'Q' (quarters), 'X', 'Y', 'Z'
-- NULL = no championship games for this agegroup

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('scheduling.AgegroupScheduleProfile')
    AND name = 'BracketDepth'
)
BEGIN
    ALTER TABLE scheduling.AgegroupScheduleProfile
        ADD BracketDepth CHAR(1) NULL;
END
