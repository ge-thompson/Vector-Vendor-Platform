-- ============================================================================
-- 03_volume_today.sql
-- Today's total volume + breakdown by event type. Renders as KPI tiles.
-- ============================================================================

SELECT
    COUNT(*) AS AttemptsToday,
    COUNT(DISTINCT VectorLoadId) AS DistinctLoadsToday,
    SUM(CASE WHEN EventTypeName = 'LocationReportedEvent' THEN 1 ELSE 0 END) AS LocationUpdates,
    SUM(CASE WHEN EventTypeName = 'LoadStatusEvent' THEN 1 ELSE 0 END) AS StatusEvents,
    SUM(CASE WHEN EventTypeName = 'LoadCreatedEvent' THEN 1 ELSE 0 END) AS LoadsCreated,
    SUM(CASE WHEN EventTypeName = 'LoadAssignedEvent' THEN 1 ELSE 0 END) AS Assignments,
    SUM(CASE WHEN EventTypeName = 'DocumentAvailableEvent' THEN 1 ELSE 0 END) AS Documents,
    SUM(CASE WHEN EventTypeName = 'LoadTrackingStoppedEvent' THEN 1 ELSE 0 END) AS TrackingStopped
FROM dbo.VendorOutboundTransactions
WHERE VendorName = 'FourKites'
  AND CreatedUtc >= CAST(SYSUTCDATETIME() AS DATE);
