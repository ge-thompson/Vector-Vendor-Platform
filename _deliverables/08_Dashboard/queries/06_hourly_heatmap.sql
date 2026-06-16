-- ============================================================================
-- 06_hourly_heatmap.sql
-- Hour-of-day x day-of-week heatmap for the last 7 days.
-- Renders as 24-column x 7-row heatmap with SuccessRatePct as color value.
-- Useful for spotting time-of-day patterns or FK maintenance windows.
-- ============================================================================

SELECT
    CAST(CreatedUtc AS DATE) AS Day,
    DATEPART(HOUR, CreatedUtc) AS HourUtc,
    COUNT(*) AS Attempts,
    SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1 ELSE 0 END) AS Successes,
    CAST(
        SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1.0 ELSE 0.0 END)
        * 100.0
        / NULLIF(COUNT(*), 0)
        AS DECIMAL(5, 2)
    ) AS SuccessRatePct
FROM dbo.VendorOutboundTransactions
WHERE VendorName = 'FourKites'
  AND CreatedUtc >= DATEADD(DAY, -7, SYSUTCDATETIME())
GROUP BY CAST(CreatedUtc AS DATE), DATEPART(HOUR, CreatedUtc)
ORDER BY Day DESC, HourUtc;
