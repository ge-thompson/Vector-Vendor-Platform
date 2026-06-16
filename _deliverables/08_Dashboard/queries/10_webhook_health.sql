-- ============================================================================
-- 10_webhook_health.sql
-- Inbound webhook health metrics for the last 24 hours.
-- Tracks correlator throughput, backlog, dedupe activity, no-match anomalies.
-- ============================================================================

SELECT
    COUNT(*) AS TotalReceived24h,
    SUM(CASE WHEN ProcessedUtc IS NOT NULL THEN 1 ELSE 0 END) AS Processed,
    SUM(CASE WHEN ProcessedUtc IS NULL THEN 1 ELSE 0 END) AS UnprocessedBacklog,
    AVG(CAST(DATEDIFF(SECOND, ReceivedUtc, ProcessedUtc) AS FLOAT)) AS AvgProcessingSeconds,
    MAX(DATEDIFF(SECOND, ReceivedUtc, ProcessedUtc)) AS MaxProcessingSeconds,
    SUM(CASE WHEN CorrelationStatus = 'MATCHED' THEN 1 ELSE 0 END) AS MatchedCount,
    SUM(CASE WHEN CorrelationStatus = 'NO_MATCH' THEN 1 ELSE 0 END) AS NoMatchCount,
    SUM(CASE WHEN ReceiptCount > 1 THEN 1 ELSE 0 END) AS DuplicatesAbsorbed
FROM dbo.VendorInboundCallbacks
WHERE VendorName = 'FourKites'
  AND ReceivedUtc >= DATEADD(HOUR, -24, SYSUTCDATETIME());
