-- =============================================================================
-- Verification queries for Phase 1 host integration tests
--
-- TWO SERVERS:
--   (localdb)\mssqllocaldb         - VendorAPI_FK  (dispatch audit)
--   DESKTOP-4DEA4AP\SQLDEVELOP     - VectorOTR     (OTR API's own AuditLogs)
--
-- Run each block against the appropriate server. The block headers say which.
-- =============================================================================


-- =============================================================================
-- SERVER: (localdb)\mssqllocaldb    DATABASE: VendorAPI_FK
-- (This is what the dispatch framework writes to.)
-- =============================================================================

USE VendorAPI_FK;
GO

-- 1. After 02_TrackLoad.http -- expect 1 LoadAssignedEvent row for 999001
SELECT TOP 5
    TransactionId,
    EventTypeName,
    VendorName,
    Status,
    HttpStatusCode,
    LEFT(ErrorMessage, 150) AS ErrorPreview,
    LEFT(RequestPayload,  300) AS RequestPreview,
    CreatedUtc,
    DurationMs
FROM dbo.VendorOutboundTransactions
WHERE VectorLoadId = '999001'
ORDER BY CreatedUtc DESC;


-- 2. After 03_UpdateTrackLoad.http -- expect 2 LoadAssignedEvent rows for 999001 now
SELECT COUNT(*) AS LoadAssignedCount
FROM dbo.VendorOutboundTransactions
WHERE VectorLoadId  = '999001'
  AND EventTypeName = 'LoadAssignedEvent';


-- 3. After 04_CancelLoadTracking.http -- expect 1 LoadTrackingStoppedEvent for 999002
SELECT TOP 5
    EventTypeName, Status, HttpStatusCode,
    LEFT(RequestPayload, 300) AS RequestPreview,
    CreatedUtc
FROM dbo.VendorOutboundTransactions
WHERE VectorLoadId = '999002'
ORDER BY CreatedUtc DESC;


-- 4. After 05_SendStatus_Generous.http -- expect 6-7 rows (mix of types) for 999003
SELECT EventTypeName, COUNT(*) AS EventCount
FROM dbo.VendorOutboundTransactions
WHERE VectorLoadId = '999003'
GROUP BY EventTypeName;

SELECT TOP 20
    EventTypeName, Status, HttpStatusCode,
    LEFT(RequestPayload, 250) AS RequestPreview,
    CreatedUtc
FROM dbo.VendorOutboundTransactions
WHERE VectorLoadId = '999003'
ORDER BY CreatedUtc DESC;


-- 5. After 06_SendStatus_Conservative.http -- expect 1-2 rows in the last 2 minutes
SELECT TOP 10
    EventTypeName, Status, CreatedUtc
FROM dbo.VendorOutboundTransactions
WHERE VectorLoadId = '999003'
  AND CreatedUtc > DATEADD(MINUTE, -2, SYSUTCDATETIME())
ORDER BY CreatedUtc DESC;


-- 6. Overall status breakdown across all test rows
SELECT Status, COUNT(*) AS RowCount
FROM dbo.VendorOutboundTransactions
WHERE VectorLoadId IN ('999001', '999002', '999003')
GROUP BY Status;


-- 7. Errors only (anything not SUCCESS) for these tests
SELECT TOP 10
    CreatedUtc, EventTypeName, VectorLoadId, Status, HttpStatusCode,
    LEFT(ErrorMessage, 300) AS ErrorPreview
FROM dbo.VendorOutboundTransactions
WHERE VectorLoadId IN ('999001', '999002', '999003')
  AND Status <> 'SUCCESS'
ORDER BY CreatedUtc DESC;


-- 8. Mapping cache rows still match seed
SELECT VendorName, Direction, SourceSystem, SourceCode, TargetCode, IsActive
FROM dbo.VendorStatusMapping
ORDER BY VendorName, Direction, SourceCode;


-- 9. Confirm verbosity setting on FK profile
SELECT
    ShipperCode,
    VendorName,
    IsActive,
    JSON_VALUE(ConfigJson, '$.dispatchPolicy.verbosity') AS Verbosity,
    LEN(ConfigJson) AS ConfigBytes
FROM dbo.ClientProfiles
WHERE VendorName = 'FourKites';


-- 10. Cleanup test data (run AFTER you're satisfied with results)
-- DELETE FROM dbo.VendorOutboundTransactions WHERE VectorLoadId IN ('999001','999002','999003');



-- =============================================================================
-- SERVER: DESKTOP-4DEA4AP\SQLDEVELOP     DATABASE: VectorOTR
-- (This is OTR API's own audit log -- where DataAudit.InsertErrorAuditLog writes.)
-- =============================================================================

USE VectorOTR;
GO

-- 11. Dispatch-block errors specifically (try/catch in our insertion points)
SELECT TOP 20 ID, LogTypeName, LogMessage, Created
FROM dbo.AuditLogs
WHERE LogMessage LIKE 'TrackLoad.VendorDispatch%'
   OR LogMessage LIKE 'UpdateTrackLoad.VendorDispatch%'
   OR LogMessage LIKE 'CancelLoadTracking.VendorDispatch%'
   OR LogMessage LIKE 'SendStatus.VendorDispatch%'
ORDER BY Created DESC;


-- 12. Any recent errors at all from the four touched methods
SELECT TOP 20 ID, LogTypeName, LogMessage, Created
FROM dbo.AuditLogs
WHERE LogMessage LIKE 'TrackLoad:%'
   OR LogMessage LIKE 'UpdateTrackLoad:%'
   OR LogMessage LIKE 'CancelLoadTracking:%'
   OR LogMessage LIKE 'SendStatus:%'
   OR LogMessage LIKE 'TrackLoadStatus%'
ORDER BY Created DESC;


-- 13. Anything written to AuditLogs in the last hour (catch-all sanity check)
SELECT TOP 30 ID, LogTypeName, LogMessage, Created
FROM dbo.AuditLogs
WHERE Created > DATEADD(HOUR, -1, GETDATE())
ORDER BY Created DESC;
