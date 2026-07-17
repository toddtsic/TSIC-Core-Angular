-- Remove the unnecessary separate returns sproc. eCheck returns belong in
-- MonthyQBPExport_Automated as a 'Returned Item' negative-line case, exactly
-- like the existing 'Credited' (refund) case — not a parallel sproc/file.
DROP PROCEDURE IF EXISTS [adn].[MonthyQBPExport_Automated_EcheckReturns];
