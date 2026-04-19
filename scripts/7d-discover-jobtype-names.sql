-- Lists the canonical JobTypeName values used by the new system's
-- VisibilityRules.jobTypes allowlist. Cross-ref with the legacy CSV's
-- JobTypes column ('player registration for camp or clinic', etc.) to
-- populate Type 1 catalog entries with jobTypes arrays.

SELECT
    jt.JobTypeId,
    jt.JobTypeName,
    jt.JobTypeDesc,
    COUNT(DISTINCT j.JobId) AS JobCount_2025_2027
FROM reference.JobTypes jt
LEFT JOIN Jobs.Jobs j
    ON j.JobTypeId = jt.JobTypeId
    AND j.[year] IN ('2025', '2026', '2027')
GROUP BY
    jt.JobTypeId,
    jt.JobTypeName,
    jt.JobTypeDesc
ORDER BY
    jt.JobTypeId;
