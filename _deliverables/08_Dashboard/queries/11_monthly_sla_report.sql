-- ============================================================================
-- 11_monthly_sla_report.sql
-- Monthly SLA compliance report. Single row showing the current month's
-- success rate and PASS/FAIL against the 95% threshold.
--
-- For prior months: change @MonthStart to the first of the desired month.
-- For a multi-month history view, see the trailing query.
-- ============================================================================

DECLARE @MonthStart DATE = DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1);
DECLARE @NextMonthStart DATE = DATEADD(MONTH, 1, @MonthStart);

-- Current month summary
SELECT
    'Current Month' AS Period,
    @MonthStart AS PeriodStart,
    @NextMonthStart AS PeriodEnd,
    COUNT(*) AS TotalAttempts,
    SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1 ELSE 0 END) AS Successes,
    COUNT(*) - SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1 ELSE 0 END) AS Failures,
    CAST(
        SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1.0 ELSE 0.0 END)
        * 100.0
        / NULLIF(COUNT(*), 0)
        AS DECIMAL(5, 2)
    ) AS SuccessRatePct,
    CASE
        WHEN CAST(SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1.0 ELSE 0.0 END)
                  * 100.0 / NULLIF(COUNT(*), 0) AS DECIMAL(5,2)) >= 95.0 THEN 'PASS'
        ELSE 'FAIL'
    END AS SlaStatus
FROM dbo.VendorOutboundTransactions
WHERE VendorName = 'FourKites'
  AND CreatedUtc >= @MonthStart
  AND CreatedUtc < @NextMonthStart;


-- ============================================================================
-- Multi-month history (last 12 months, oldest first)
-- ============================================================================
;WITH Months AS (
    SELECT DATEFROMPARTS(
            YEAR(DATEADD(MONTH, -ROW_NUMBER() OVER (ORDER BY object_id) + 1, SYSUTCDATETIME())),
            MONTH(DATEADD(MONTH, -ROW_NUMBER() OVER (ORDER BY object_id) + 1, SYSUTCDATETIME())),
            1
           ) AS MonthStart
    FROM sys.all_objects
    WHERE ROW_NUMBER() OVER (ORDER BY object_id) <= 12
)
SELECT
    m.MonthStart,
    DATEADD(MONTH, 1, m.MonthStart) AS MonthEnd,
    COUNT(t.TransactionId) AS Attempts,
    SUM(CASE WHEN t.Status IN ('ACK', 'CONFIRMED') THEN 1 ELSE 0 END) AS Successes,
    CAST(
        SUM(CASE WHEN t.Status IN ('ACK', 'CONFIRMED') THEN 1.0 ELSE 0.0 END)
        * 100.0
        / NULLIF(COUNT(t.TransactionId), 0)
        AS DECIMAL(5, 2)
    ) AS SuccessRatePct,
    CASE
        WHEN COUNT(t.TransactionId) = 0 THEN 'NO_DATA'
        WHEN CAST(SUM(CASE WHEN t.Status IN ('ACK', 'CONFIRMED') THEN 1.0 ELSE 0.0 END)
                  * 100.0 / NULLIF(COUNT(t.TransactionId), 0) AS DECIMAL(5,2)) >= 95.0 THEN 'PASS'
        ELSE 'FAIL'
    END AS SlaStatus
FROM Months m
LEFT JOIN dbo.VendorOutboundTransactions t
    ON t.CreatedUtc >= m.MonthStart
   AND t.CreatedUtc <  DATEADD(MONTH, 1, m.MonthStart)
   AND t.VendorName = 'FourKites'
GROUP BY m.MonthStart
ORDER BY m.MonthStart;
