-- ============================================================================
-- 04_failures_24h.sql
-- Failures in the last 24 hours, grouped by status + error category.
-- Renders as sorted bar chart or table — largest first.
-- ============================================================================

SELECT
    Status,
    ErrorCategory,
    COUNT(*) AS FailureCount,
    MIN(CreatedUtc) AS FirstSeenUtc,
    MAX(CreatedUtc) AS LastSeenUtc,
    COUNT(DISTINCT VectorLoadId) AS DistinctLoadsAffected
FROM dbo.VendorOutboundTransactions
WHERE VendorName = 'FourKites'
  AND CreatedUtc >= DATEADD(HOUR, -24, SYSUTCDATETIME())
  AND Status IN ('HTTP_FAIL', 'TRANSPORT_FAIL', 'REJECTED', 'DEAD_LETTER', 'RATE_LIMITED')
GROUP BY Status, ErrorCategory
ORDER BY FailureCount DESC;
