-- ============================================================================
-- 05_success_by_event_type.sql
-- 7-day success rate broken out by event type. Reveals which integrations
-- are healthiest vs. weakest. Renders as table with color-coded SuccessRatePct.
-- Sort: worst first (forces attention).
-- ============================================================================

SELECT
    EventTypeName,
    COUNT(*) AS Attempts,
    SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1 ELSE 0 END) AS Successes,
    CAST(
        SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1.0 ELSE 0.0 END)
        * 100.0
        / NULLIF(COUNT(*), 0)
        AS DECIMAL(5, 2)
    ) AS SuccessRatePct,
    SUM(CASE WHEN Status = 'CONFIRMED' THEN 1 ELSE 0 END) AS ConfirmedCount,
    SUM(CASE WHEN Status = 'HTTP_FAIL' THEN 1 ELSE 0 END) AS HttpFails,
    SUM(CASE WHEN Status = 'TRANSPORT_FAIL' THEN 1 ELSE 0 END) AS TransportFails,
    SUM(CASE WHEN Status = 'REJECTED' THEN 1 ELSE 0 END) AS Rejected,
    SUM(CASE WHEN Status = 'RATE_LIMITED' THEN 1 ELSE 0 END) AS RateLimited
FROM dbo.VendorOutboundTransactions
WHERE VendorName = 'FourKites'
  AND CreatedUtc >= DATEADD(DAY, -7, SYSUTCDATETIME())
GROUP BY EventTypeName
ORDER BY SuccessRatePct ASC;
