/* ============================================================================
   Create the UNCLAIMED club "True" for Madii Fowler to claim at signup.

   Leave it with ZERO reps. That is what makes it claimable by the new
   ExistingClubId path in ClubService.RegisterAsync -- and what stops that
   path from ever being pointed at an established club.

   Run this AFTER the backend change is deployed, BEFORE she signs up.
   ============================================================================ */
SET NOCOUNT ON;

/* ---- 1. Confirm the name is not already taken --------------------------- */
SELECT ClubId, ClubName
FROM Clubs.Clubs
WHERE ClubName = N'True';
/* EXPECT: zero rows. If a row comes back, stop -- reuse that ClubId instead. */


/* ---- 2. Create it ------------------------------------------------------- */
BEGIN TRAN;

IF EXISTS (SELECT 1 FROM Clubs.Clubs WHERE ClubName = N'True')
BEGIN
    RAISERROR('A club named True already exists. Nothing changed.', 16, 1);
    ROLLBACK TRAN;
END
ELSE
BEGIN
    INSERT INTO Clubs.Clubs (ClubName, LebUserId, Modified)
    VALUES (N'True', NULL, GETDATE());

    PRINT 'Created ClubId ' + CAST(SCOPE_IDENTITY() AS varchar(10)) + ' = True';
    COMMIT TRAN;
END


/* ---- 3. Hand this ClubId to the signup ---------------------------------- */
SELECT c.ClubId, c.ClubName,
       (SELECT COUNT(*) FROM Clubs.ClubReps  cr WHERE cr.ClubId = c.ClubId) AS Reps,
       (SELECT COUNT(*) FROM Clubs.ClubTeams ct WHERE ct.ClubId = c.ClubId) AS LibraryTeams
FROM Clubs.Clubs c
WHERE c.ClubName = N'True';
/* EXPECT: one row, Reps = 0, LibraryTeams = 0.
   Reps MUST be 0 or RegisterAsync will refuse the claim.                    */


/* ---- 4. AFTER she signs up: verify -------------------------------------- */
/*
SELECT u.UserName, u.Email, c.ClubId, c.ClubName,
       (SELECT COUNT(*) FROM Clubs.ClubReps x WHERE x.ClubRepUserId = u.Id) AS ClubsOnAccount
FROM dbo.AspNetUsers u
JOIN Clubs.ClubReps cr ON cr.ClubRepUserId = u.Id
JOIN Clubs.Clubs    c  ON c.ClubId = cr.ClubId
WHERE c.ClubName = N'True';

   EXPECT: one row. ClubsOnAccount = 1 -- single-club rep, which is the whole
   point: login pins her to "True" with no [0] default and no picker involved.

   And the full picture stays clean:
SELECT ClubId, ClubName FROM Clubs.Clubs WHERE ClubName LIKE 'True%' ORDER BY ClubId;
     2113   True Lacrosse
     2297   True Lacrosse
     2298   True Lady Ballers
     2299   True Massachusetts
     <new>  True
*/
