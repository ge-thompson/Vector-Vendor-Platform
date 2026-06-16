-- 03_InboundCallbacks.sql
-- ─────────────────────────────────────────────────────────────────────────────
-- Logs every webhook callback received from FourKites. Populated by WebhookReceiver.
-- The DedupeHash column is the dedup primitive — UNIQUE constraint prevents double-processing
-- when FourKites retries the same callback. See Reference Doc Section 7.8.
-- ─────────────────────────────────────────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'FourKitesInboundCallbacks')
BEGIN
    CREATE TABLE dbo.FourKitesInboundCallbacks
    (
        CallbackId           BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        MessageType          NVARCHAR(50)         NULL,
        FourKitesLoadId      BIGINT               NULL,
        LoadNumber           NVARCHAR(100)        NULL,
        ReferenceNumbersJson NVARCHAR(MAX)        NULL,
        RawPayload           NVARCHAR(MAX)        NOT NULL,
        DedupeHash           CHAR(64)             NOT NULL,
        ReceivedUtc          DATETIME2            NOT NULL DEFAULT SYSUTCDATETIME(),
        ProcessedUtc         DATETIME2            NULL,
        MatchedTransactionId BIGINT               NULL,
        CONSTRAINT UQ_FourKitesInboundCallbacks_Hash UNIQUE (DedupeHash),
        INDEX IX_Inbound_FkLoadId         NONCLUSTERED (FourKitesLoadId),
        INDEX IX_Inbound_MessageType_Time NONCLUSTERED (MessageType, ReceivedUtc),
        INDEX IX_Inbound_Unprocessed      NONCLUSTERED (ProcessedUtc) WHERE ProcessedUtc IS NULL
    );
    PRINT 'Created dbo.FourKitesInboundCallbacks';
END
ELSE
BEGIN
    PRINT 'dbo.FourKitesInboundCallbacks already exists, skipped.';
END
GO

-- Optional housekeeping: a stored proc to clear old dedupe entries past the 48-hour FourKites retry window.
-- Run as a daily SQL Agent job.
IF OBJECT_ID('dbo.usp_FourKitesPurgeOldCallbacks', 'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_FourKitesPurgeOldCallbacks;
GO

CREATE PROCEDURE dbo.usp_FourKitesPurgeOldCallbacks
    @RetainHours INT = 48
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.FourKitesInboundCallbacks
    WHERE ReceivedUtc < DATEADD(HOUR, -@RetainHours, SYSUTCDATETIME())
      AND ProcessedUtc IS NOT NULL;
    PRINT 'Purged ' + CAST(@@ROWCOUNT AS NVARCHAR(20)) + ' old callbacks.';
END
GO
