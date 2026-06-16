-- ============================================================================
-- 07_recent_failures.sql
-- Up to 100 most recent failed transactions in the last 24 hours.
-- Renders as drill-down table — each row is clickable for full audit.
-- ============================================================================

SELECT TOP 100
    TransactionId,
    CreatedUtc,
    VectorLoadId,
    EventTypeName,
    Status,
    HttpStatusCode,
    ErrorCategory,
    LEFT(COALESCE(ErrorMessage, ''), 200) AS ErrorPreview,
    VendorRequestId,
    SourceSystem
FROM dbo.VendorOutboundTransactions
WHERE VendorName = 'FourKites'
  AND Status IN ('HTTP_FAIL', 'TRANSPORT_FAIL', 'REJECTED', 'DEAD_LETTER')
  AND CreatedUtc >= DATEADD(HOUR, -24, SYSUTCDATETIME())
ORDER BY CreatedUtc DESC;
