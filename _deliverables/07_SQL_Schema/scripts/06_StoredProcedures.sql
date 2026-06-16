/*
================================================================================
  06_StoredProcedures.sql
  Four procedures used by the framework and dashboard:
    - usp_GetLoadAuditTrail   : "tell me everything for this VectorLoadId"
    - usp_GetSuccessRate      : aggregate success% over a time window
    - usp_UpsertInboundCallback : dedupe-safe webhook INSERT
    - usp_RecordVendorLoadCrossReference : abstracts Option A vs B
  Idempotent — uses CREATE OR ALTER.
================================================================================
*/

USE VendorAPI_FK;
GO

-- ----------------------------------------------------------------------------
-- usp_GetLoadAuditTrail
-- Returns three result sets for a single VectorLoadId:
--   1. Outbound transactions, chronological
--   2. Inbound callbacks, chronological
--   3. Cross-references (VendorLoadId per vendor)
-- ----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_GetLoadAuditTrail
    @VectorLoadId NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    -- Result set 1: outbound transactions
    SELECT
        TransactionId,
        VendorName,
        EventTypeName,
        Status,
        HttpStatusCode,
        ErrorCategory,
        VendorRequestId,
        VendorLoadId,
        CreatedUtc,
        AckUtc,
        ConfirmedUtc,
        DurationMs,
        SourceSystem,
        ShipperCode,
        RequestPayload,
        ResponseBody,
        ErrorMessage
    FROM dbo.VendorOutboundTransactions
    WHERE VectorLoadId = @VectorLoadId
    ORDER BY CreatedUtc ASC;

    -- Result set 2: inbound callbacks
    SELECT
        CallbackId,
        VendorName,
        MessageType,
        VendorLoadId,
        IsSuccess,
        ReceivedUtc,
        ProcessedUtc,
        ReceiptCount,
        MatchedTransactionId,
        CorrelationStatus,
        ErrorsJson,
        RawPayload
    FROM dbo.VendorInboundCallbacks
    WHERE VectorLoadId = @VectorLoadId
       OR CallbackId IN (
           SELECT MatchedTransactionId
           FROM dbo.VendorInboundCallbacks
           WHERE MatchedTransactionId IN (
               SELECT TransactionId
               FROM dbo.VendorOutboundTransactions
               WHERE VectorLoadId = @VectorLoadId
           )
       )
    ORDER BY ReceivedUtc ASC;

    -- Result set 3: cross-references
    SELECT
        VendorName,
        VendorLoadId,
        TrackingStatus,
        CreatedUtc,
        UpdatedUtc
    FROM dbo.LoadCrossReference
    WHERE VectorLoadId = @VectorLoadId
    ORDER BY VendorName;
END;
GO


-- ----------------------------------------------------------------------------
-- usp_GetSuccessRate
-- Aggregate success% over a time window, by vendor and (optionally) event type.
-- "Success" = Status IN ('ACK', 'CONFIRMED'). Everything else is a failure mode.
-- ----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_GetSuccessRate
    @WindowStartUtc DATETIME2(3),
    @WindowEndUtc   DATETIME2(3),
    @VendorName     NVARCHAR(50)  = NULL,
    @EventTypeName  NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        VendorName,
        EventTypeName,
        COUNT(*)                                                            AS Attempts,
        SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1 ELSE 0 END)     AS Successes,
        SUM(CASE WHEN Status = 'CONFIRMED' THEN 1 ELSE 0 END)               AS Confirmed,
        SUM(CASE WHEN Status = 'HTTP_FAIL' THEN 1 ELSE 0 END)               AS HttpFailures,
        SUM(CASE WHEN Status = 'TRANSPORT_FAIL' THEN 1 ELSE 0 END)          AS TransportFailures,
        SUM(CASE WHEN Status = 'RATE_LIMITED' THEN 1 ELSE 0 END)            AS RateLimited,
        SUM(CASE WHEN Status = 'REJECTED' THEN 1 ELSE 0 END)                AS Rejected,
        SUM(CASE WHEN Status = 'DEAD_LETTER' THEN 1 ELSE 0 END)             AS DeadLetter,
        CAST(
            SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1.0 ELSE 0.0 END)
            * 100.0
            / NULLIF(COUNT(*), 0)
            AS DECIMAL(5, 2)
        )                                                                   AS SuccessRatePct
    FROM dbo.VendorOutboundTransactions
    WHERE CreatedUtc >= @WindowStartUtc
      AND CreatedUtc <  @WindowEndUtc
      AND (@VendorName IS NULL OR VendorName = @VendorName)
      AND (@EventTypeName IS NULL OR EventTypeName = @EventTypeName)
    GROUP BY VendorName, EventTypeName
    ORDER BY VendorName, EventTypeName;
END;
GO


-- ----------------------------------------------------------------------------
-- usp_UpsertInboundCallback
-- Dedupe-safe INSERT for webhook receipt. Handles the (VendorName, PayloadHash)
-- UNIQUE constraint by treating a duplicate as an update of the existing row's
-- receipt counters, not a new row.
--
-- Returns the CallbackId of the row (new or existing).
-- ----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_UpsertInboundCallback
    @VendorName            NVARCHAR(50),
    @PayloadHash           CHAR(64),
    @RawPayload            NVARCHAR(MAX),
    @MessageType           NVARCHAR(50)  = NULL,
    @VendorLoadId          NVARCHAR(100) = NULL,
    @VectorLoadId          NVARCHAR(50)  = NULL,
    @ReferenceNumbersJson  NVARCHAR(MAX) = NULL,
    @IsSuccess             BIT           = NULL,
    @ErrorsJson            NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @CallbackId BIGINT;

    -- MERGE handles concurrent insert race: if two duplicate webhooks arrive in
    -- the same millisecond, only one INSERT succeeds; the other becomes UPDATE.
    MERGE dbo.VendorInboundCallbacks AS target
    USING (SELECT @VendorName AS VendorName, @PayloadHash AS PayloadHash) AS src
       ON target.VendorName = src.VendorName AND target.PayloadHash = src.PayloadHash
    WHEN MATCHED THEN
        UPDATE SET
            LastSeenUtc = SYSUTCDATETIME(),
            ReceiptCount = target.ReceiptCount + 1
    WHEN NOT MATCHED THEN
        INSERT (VendorName, PayloadHash, RawPayload, MessageType, VendorLoadId,
                VectorLoadId, ReferenceNumbersJson, IsSuccess, ErrorsJson,
                ReceivedUtc, LastSeenUtc, ReceiptCount)
        VALUES (@VendorName, @PayloadHash, @RawPayload, @MessageType, @VendorLoadId,
                @VectorLoadId, @ReferenceNumbersJson, @IsSuccess, @ErrorsJson,
                SYSUTCDATETIME(), SYSUTCDATETIME(), 1);

    -- Return the CallbackId — works for both insert and update paths
    SELECT @CallbackId = CallbackId
    FROM dbo.VendorInboundCallbacks
    WHERE VendorName = @VendorName AND PayloadHash = @PayloadHash;

    SELECT @CallbackId AS CallbackId;
END;
GO


-- ----------------------------------------------------------------------------
-- usp_RecordVendorLoadCrossReference
-- Records (VectorLoadId, VendorName) -> VendorLoadId mapping.
-- Idempotent: re-running updates the existing row's UpdatedUtc and TrackingStatus.
--
-- This proc abstracts the choice between Option A (columns on Vector's [Load]
-- table) and Option B (this cross-reference table). The deliverable uses Option B
-- by default. If you switch to Option A, edit this proc to also UPDATE Vector's
-- table — but adapters' OnConfirmedAsync code does not change.
-- ----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_RecordVendorLoadCrossReference
    @VectorLoadId   NVARCHAR(50),
    @VendorName     NVARCHAR(50),
    @VendorLoadId   NVARCHAR(100),
    @TrackingStatus NVARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    MERGE dbo.LoadCrossReference AS target
    USING (SELECT @VectorLoadId AS V, @VendorName AS Vn) AS src
       ON target.VectorLoadId = src.V AND target.VendorName = src.Vn
    WHEN MATCHED THEN
        UPDATE SET
            VendorLoadId = @VendorLoadId,
            TrackingStatus = COALESCE(@TrackingStatus, target.TrackingStatus),
            UpdatedUtc = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT (VectorLoadId, VendorName, VendorLoadId, TrackingStatus, CreatedUtc, UpdatedUtc)
        VALUES (@VectorLoadId, @VendorName, @VendorLoadId, @TrackingStatus, SYSUTCDATETIME(), SYSUTCDATETIME());

    -- If Option A is preferred, add the UPDATE to Vector's [Load] table here.
    -- Wrap in TRY/CATCH so a missing column doesn't break the cross-reference write:
    --
    -- BEGIN TRY
    --     UPDATE dbo.[Load]
    --     SET FourKitesLoadId = @VendorLoadId, FourKitesTrackingStatus = @TrackingStatus
    --     WHERE LoadId = @VectorLoadId;
    -- END TRY
    -- BEGIN CATCH
    --     -- swallow: schema may not have FK columns yet
    -- END CATCH
END;
GO


PRINT 'Stored procedures created/updated.';
GO
