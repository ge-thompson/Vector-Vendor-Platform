-- ============================================================================
-- 09_lookup_by_requestid.sql
-- Find a specific transaction by FK's VendorRequestId (the requestId UUID).
-- Used when FK support says "we don't see request <guid>" — proves what we sent.
-- ============================================================================

-- Replace placeholder with the VendorRequestId given by FK support
SELECT
    TransactionId,
    VectorLoadId,
    EventTypeName,
    Status,
    HttpStatusCode,
    CreatedUtc,
    AckUtc,
    ConfirmedUtc,
    RequestPayload,
    ResponseBody,
    ErrorMessage
FROM dbo.VendorOutboundTransactions
WHERE VendorName = 'FourKites'
  AND VendorRequestId = '7f8a9b3c-1234-5678-9abc-def012345678';
