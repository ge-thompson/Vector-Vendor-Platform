/*
================================================================================
  99_Verify.sql
  Read-only verification script. Run AFTER all other scripts.
  Reports green/red for each expected object. Safe to re-run anytime.
================================================================================
*/

USE VendorAPI_FK;
GO

SET NOCOUNT ON;
DECLARE @AllGreen BIT = 1;
DECLARE @CheckName NVARCHAR(100), @Status NVARCHAR(20), @Detail NVARCHAR(500);

DECLARE @Results TABLE (CheckName NVARCHAR(100), Status NVARCHAR(20), Detail NVARCHAR(500));

-- Tables
DECLARE @ExpectedTables TABLE (TableName SYSNAME);
INSERT INTO @ExpectedTables VALUES
    ('ClientProfiles'),
    ('VendorOutboundTransactions'),
    ('VendorInboundCallbacks'),
    ('LoadCrossReference'),
    ('VendorRateLimitWindow');

INSERT INTO @Results (CheckName, Status, Detail)
SELECT
    'Table: ' + e.TableName,
    CASE WHEN OBJECT_ID('dbo.' + e.TableName, 'U') IS NOT NULL THEN 'OK' ELSE 'MISSING' END,
    CASE WHEN OBJECT_ID('dbo.' + e.TableName, 'U') IS NOT NULL THEN 'exists' ELSE 'NOT FOUND' END
FROM @ExpectedTables e;

-- Indexes (a few key ones — not exhaustive)
DECLARE @ExpectedIndexes TABLE (IndexName SYSNAME, TableName SYSNAME);
INSERT INTO @ExpectedIndexes VALUES
    ('IX_ClientProfiles_Lookup', 'ClientProfiles'),
    ('IX_VOT_VectorLoad_Recent', 'VendorOutboundTransactions'),
    ('IX_VOT_Vendor_Created_Status', 'VendorOutboundTransactions'),
    ('IX_VOT_Correlation', 'VendorOutboundTransactions'),
    ('IX_VIC_Unprocessed', 'VendorInboundCallbacks'),
    ('IX_VIC_VectorLoad', 'VendorInboundCallbacks'),
    ('IX_LCR_VendorLoadId', 'LoadCrossReference');

INSERT INTO @Results (CheckName, Status, Detail)
SELECT
    'Index: ' + i.IndexName,
    CASE WHEN EXISTS (
        SELECT 1 FROM sys.indexes ix
        WHERE ix.name = i.IndexName
          AND ix.object_id = OBJECT_ID('dbo.' + i.TableName)
    ) THEN 'OK' ELSE 'MISSING' END,
    'on ' + i.TableName
FROM @ExpectedIndexes i;

-- Stored procedures
DECLARE @ExpectedProcs TABLE (ProcName SYSNAME);
INSERT INTO @ExpectedProcs VALUES
    ('usp_GetLoadAuditTrail'),
    ('usp_GetSuccessRate'),
    ('usp_UpsertInboundCallback'),
    ('usp_RecordVendorLoadCrossReference');

INSERT INTO @Results (CheckName, Status, Detail)
SELECT
    'Proc: ' + p.ProcName,
    CASE WHEN OBJECT_ID('dbo.' + p.ProcName, 'P') IS NOT NULL THEN 'OK' ELSE 'MISSING' END,
    CASE WHEN OBJECT_ID('dbo.' + p.ProcName, 'P') IS NOT NULL THEN 'exists' ELSE 'NOT FOUND' END
FROM @ExpectedProcs p;

-- Seed data
INSERT INTO @Results (CheckName, Status, Detail)
SELECT
    'Seed: FourKites VECTOR_DEFAULT profile',
    CASE WHEN EXISTS (
        SELECT 1 FROM dbo.ClientProfiles
        WHERE ShipperCode = 'VECTOR_DEFAULT' AND VendorName = 'FourKites'
    ) THEN 'OK' ELSE 'MISSING' END,
    'row in dbo.ClientProfiles';

-- Functional smoke: try the dedupe path
BEGIN TRY
    DECLARE @Cb1 BIGINT, @Cb2 BIGINT;
    DECLARE @TestHash CHAR(64) = REPLICATE('a', 64);  -- assign to variable; EXEC won't accept REPLICATE() inline

    EXEC dbo.usp_UpsertInboundCallback
        @VendorName = 'VERIFY_TEST',
        @PayloadHash = @TestHash,
        @RawPayload = '{"test":"verify"}',
        @MessageType = 'TEST';

    EXEC dbo.usp_UpsertInboundCallback
        @VendorName = 'VERIFY_TEST',
        @PayloadHash = @TestHash,
        @RawPayload = '{"test":"verify"}',
        @MessageType = 'TEST';

    DECLARE @RowCount INT, @ReceiptCount INT;
    SELECT @RowCount = COUNT(*), @ReceiptCount = MAX(ReceiptCount)
    FROM dbo.VendorInboundCallbacks
    WHERE VendorName = 'VERIFY_TEST';

    IF @RowCount = 1 AND @ReceiptCount = 2
        INSERT INTO @Results VALUES ('Smoke: dedupe via UNIQUE+upsert', 'OK', '1 row, ReceiptCount=2');
    ELSE
        INSERT INTO @Results VALUES ('Smoke: dedupe via UNIQUE+upsert', 'FAIL',
            'Expected 1 row with ReceiptCount=2, got ' + CAST(@RowCount AS NVARCHAR) + ' rows, max ReceiptCount=' + CAST(@ReceiptCount AS NVARCHAR));

    -- Cleanup
    DELETE FROM dbo.VendorInboundCallbacks WHERE VendorName = 'VERIFY_TEST';
END TRY
BEGIN CATCH
    INSERT INTO @Results VALUES ('Smoke: dedupe via UNIQUE+upsert', 'FAIL', ERROR_MESSAGE());
END CATCH;

-- Report
PRINT '';
PRINT '================================================================';
PRINT '  VendorAPI_FK Verification Report';
PRINT '  Run at: ' + CONVERT(NVARCHAR, SYSUTCDATETIME(), 121);
PRINT '================================================================';
PRINT '';

DECLARE @FailCount INT = (SELECT COUNT(*) FROM @Results WHERE Status <> 'OK');

SELECT
    CheckName,
    Status,
    Detail
FROM @Results
ORDER BY
    CASE Status WHEN 'OK' THEN 1 ELSE 0 END,
    CheckName;

PRINT '';
IF @FailCount = 0
    PRINT 'ALL CHECKS PASSED. Schema is ready.';
ELSE
    PRINT 'FAILED: ' + CAST(@FailCount AS NVARCHAR) + ' check(s). Review results above and re-run missing scripts.';
PRINT '================================================================';
GO
