/*
================================================================================
  03_VendorInboundCallbacks.sql
  One row per inbound webhook received from any vendor. The audit log for inbound.
  Dedupe via UNIQUE constraint on (VendorName, PayloadHash).
  Written by VendorWebhookController (initial INSERT) and WebhookCorrelator
  (processing updates).
  Idempotent.
================================================================================
*/

USE VendorAPI_FK;
GO

IF OBJECT_ID('dbo.VendorInboundCallbacks', 'U') IS NULL
BEGIN
    PRINT 'Creating table dbo.VendorInboundCallbacks...';

    CREATE TABLE dbo.VendorInboundCallbacks
    (
        CallbackId            BIGINT IDENTITY(1,1) NOT NULL,
        VendorName            NVARCHAR(50)   NOT NULL,
        PayloadHash           CHAR(64)       NOT NULL,        -- SHA256 hex of raw body (dedupe key)
        RawPayload            NVARCHAR(MAX)  NOT NULL,        -- preserved verbatim for audit

        -- Extracted at receipt time by IInboundEventProcessor.ParseAndExtract
        MessageType           NVARCHAR(50)   NULL,             -- "LOAD_CREATION", "STOP_ARRIVAL", etc. (vendor-defined)
        VendorLoadId          NVARCHAR(100)  NULL,             -- vendor's internal load id (FK: FourKitesLoadId)
        VectorLoadId          NVARCHAR(50)   NULL,             -- if discoverable from payload
        ReferenceNumbersJson  NVARCHAR(MAX)  NULL,             -- JSON array of reference strings
        IsSuccess             BIT            NULL,             -- vendor's report (NULL = unknown / can't determine)
        ErrorsJson            NVARCHAR(MAX)  NULL,

        -- Timing
        ReceivedUtc           DATETIME2(3)   NOT NULL CONSTRAINT DF_VIC_ReceivedUtc DEFAULT (SYSUTCDATETIME()),
        LastSeenUtc           DATETIME2(3)   NOT NULL CONSTRAINT DF_VIC_LastSeenUtc DEFAULT (SYSUTCDATETIME()),  -- updated on duplicate receipt
        ReceiptCount          INT            NOT NULL CONSTRAINT DF_VIC_ReceiptCount DEFAULT (1),                 -- incremented on duplicate
        ProcessedUtc          DATETIME2(3)   NULL,             -- when correlator finished with this row

        -- Correlation result
        MatchedTransactionId  BIGINT         NULL,             -- FK to VendorOutboundTransactions
        CorrelationStatus     NVARCHAR(20)   NULL,             -- "MATCHED", "NO_MATCH", "ERROR"
        CorrelationError      NVARCHAR(MAX)  NULL,

        CONSTRAINT PK_VendorInboundCallbacks PRIMARY KEY CLUSTERED (CallbackId),
        CONSTRAINT UQ_VendorInboundCallbacks_Hash UNIQUE (VendorName, PayloadHash)
    );

    PRINT 'Created dbo.VendorInboundCallbacks.';
END
ELSE
BEGIN
    PRINT 'Table dbo.VendorInboundCallbacks already exists. Skipping CREATE.';
END
GO

-- Filtered index: correlator scans for unprocessed rows
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VIC_Unprocessed' AND object_id = OBJECT_ID('dbo.VendorInboundCallbacks'))
BEGIN
    CREATE INDEX IX_VIC_Unprocessed
        ON dbo.VendorInboundCallbacks (VendorName, ReceivedUtc)
        INCLUDE (VendorLoadId, VectorLoadId, MessageType, ReferenceNumbersJson, RawPayload)
        WHERE ProcessedUtc IS NULL;
    PRINT 'Created index IX_VIC_Unprocessed.';
END
GO

-- Index for "everything about VectorLoadId X" report
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VIC_VectorLoad' AND object_id = OBJECT_ID('dbo.VendorInboundCallbacks'))
BEGIN
    CREATE INDEX IX_VIC_VectorLoad
        ON dbo.VendorInboundCallbacks (VectorLoadId, VendorName, ReceivedUtc DESC)
        WHERE VectorLoadId IS NOT NULL;
    PRINT 'Created index IX_VIC_VectorLoad.';
END
GO

-- Index for joining to outbound transactions on the audit report
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VIC_MatchedTx' AND object_id = OBJECT_ID('dbo.VendorInboundCallbacks'))
BEGIN
    CREATE INDEX IX_VIC_MatchedTx
        ON dbo.VendorInboundCallbacks (MatchedTransactionId)
        WHERE MatchedTransactionId IS NOT NULL;
    PRINT 'Created index IX_VIC_MatchedTx.';
END
GO

PRINT 'dbo.VendorInboundCallbacks is ready.';
GO
