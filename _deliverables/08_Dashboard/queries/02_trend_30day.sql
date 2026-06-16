-- ============================================================================
-- 02_trend_30day.sql
-- 30-day daily success rate trend. Renders as line chart with 95% reference line.
-- ============================================================================

WITH Days AS (
    SELECT TOP 30
        CAST(DATEADD(DAY, -ROW_NUMBER() OVER (ORDER BY object_id) + 1, CAST(SYSUTCDATETIME() AS DATE)) AS DATE) AS Day
    FROM sys.all_objects
)
SELECT
    d.Day,
    COUNT(t.TransactionId) AS Attempts,
    SUM(CASE WHEN t.Status IN ('ACK', 'CONFIRMED') THEN 1 ELSE 0 END) AS Successes,
    CAST(
        SUM(CASE WHEN t.Status IN ('ACK', 'CONFIRMED') THEN 1.0 ELSE 0.0 END)
        * 100.0
        / NULLIF(COUNT(t.TransactionId), 0)
        AS DECIMAL(5, 2)
    ) AS SuccessRatePct
FROM Days d
LEFT JOIN dbo.VendorOutboundTransactions t
    ON CAST(t.CreatedUtc AS DATE) = d.Day
   AND t.VendorName = 'FourKites'
GROUP BY d.Day
ORDER BY d.Day;
