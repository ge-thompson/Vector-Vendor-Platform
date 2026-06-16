-- ============================================================================
-- 01_headline_success_rate.sql
-- The big number: current 7-day rolling ACK success rate.
-- Refresh every 60 seconds.
-- ============================================================================

SELECT
    CAST(
        SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1.0 ELSE 0.0 END)
        * 100.0
        / NULLIF(COUNT(*), 0)
        AS DECIMAL(5, 2)
    ) AS SuccessRatePct,
    COUNT(*) AS TotalAttempts,
    SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1 ELSE 0 END) AS Successes,
    COUNT(*) - SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1 ELSE 0 END) AS Failures
FROM dbo.VendorOutboundTransactions
WHERE VendorName = 'FourKites'
  AND CreatedUtc >= DATEADD(DAY, -7, SYSUTCDATETIME());
