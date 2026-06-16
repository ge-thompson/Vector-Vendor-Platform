/*
================================================================================
  02_VendorOutboundTransactions.sql
  One row per dispatch attempt to any vendor. The audit log for outbound.
  Written by Vendor.Common.OutboundTransactionRepository.
  Idempotent.

  Status lifecycle:
    PENDING -> ACK -> CONFIRMED   (success path)
                  \-> REJECTED    (webhook reported errors)
    PENDING -> HTTP_FAIL          (4xx synchronous)
    PENDING -> TRANSPORT_FAIL     (network failure)
    PENDING -> RATE_LIMITED       (429; will retry)
    PENDING -> SKIPPED            (no profile matched the dispatch)
    PENDING -> DEAD_LETTER        (exhausted retries)
================================================================================
*/

USE VendorAPI_FK;
GO

IF OBJECT_ID('dbo.VendorOutboundTransactions', 'U') IS NULL
BEGIN
    PRINT 'Creating table dbo.VendorOutboundTransactions...';

    CREATE TABLE dbo.VendorOutboundTransactions
    (
        TransactionId        BIGINT IDENTITY(1,1) NOT NULL,
        VendorName           NVARCHAR(50)   NOT NULL,
        EventTypeName        NVARCHAR(100)  NOT NULL,     -- "LoadCreatedEvent", "LocationReportedEvent", etc.
        VectorLoadId         NVARCHAR(50)   NOT NULL,
        ShipperCode          NVARCHAR(50)   NULL,         -- resolved at dispatch time
        SourceSystem         NVARCHAR(50)   NULL,         -- "OTR_API", "VectorFBS", "POD_App"

        -- Status and lifecycle
        Status               NVARCHAR(20)   NOT NULL CONSTRAINT DF_VOT_Status DEFAULT ('PENDING'),
        HttpStatusCode       INT            NULL,
        ErrorCategory        NVARCHAR(20)   NULL,         -- "Transient", "Permanent", "RateLimit", "Unknown"
        ErrorMessage         NVARCHAR(MAX)  NULL,

        -- Vendor correlation
        VendorRequestId      NVARCHAR(100)  NULL,         -- FK requestId, P44 tracking id, etc.
        VendorLoadId         NVARCHAR(100)  NULL,         -- populated when the vendor returns/echoes their internal load id
        ExpectedCallbackType NVARCHAR(50)   NULL,         -- "LOAD_CREATION", "STOP_ARRIVAL" — used by correlator to scope matching

        -- Payloads
        RequestPayload       NVARCHAR(MAX)  NULL,         -- the JSON we sent
        ResponseBody         NVARCHAR(MAX)  NULL,         -- the JSON they returned (2xx body, 4xx body, or error message)

        -- Timing
        CreatedUtc           DATETIME2(3)   NOT NULL CONSTRAINT DF_VOT_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        AckUtc               DATETIME2(3)   NULL,         -- when we received HTTP response
        ConfirmedUtc         DATETIME2(3)   NULL,         -- when we received confirming webhook
        DurationMs           INT            NULL,         -- HTTP call duration

        CONSTRAINT PK_VendorOutboundTransactions PRIMARY KEY CLUSTERED (TransactionId)
    );

    PRINT 'Created dbo.VendorOutboundTransactions.';
END
ELSE
BEGIN
    PRINT 'Table dbo.VendorOutboundTransactions already exists. Skipping CREATE.';
END
GO

-- Index for "everything about VectorLoadId X" report
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VOT_VectorLoad_Recent' AND object_id = OBJECT_ID('dbo.VendorOutboundTransactions'))
BEGIN
    CREATE INDEX IX_VOT_VectorLoad_Recent
        ON dbo.VendorOutboundTransactions (VectorLoadId, CreatedUtc DESC)
        INCLUDE (VendorName, EventTypeName, Status, HttpStatusCode);
    PRINT 'Created index IX_VOT_VectorLoad_Recent.';
END
GO

-- Index for dashboard success-rate queries
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VOT_Vendor_Created_Status' AND object_id = OBJECT_ID('dbo.VendorOutboundTransactions'))
BEGIN
    CREATE INDEX IX_VOT_Vendor_Created_Status
        ON dbo.VendorOutboundTransactions (VendorName, CreatedUtc DESC, Status)
        INCLUDE (EventTypeName, HttpStatusCode, ErrorCategory);
    PRINT 'Created index IX_VOT_Vendor_Created_Status.';
END
GO

-- Index for correlator: find PENDING/ACK transactions to match against incoming webhooks
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VOT_Correlation' AND object_id = OBJECT_ID('dbo.VendorOutboundTransactions'))
BEGIN
    CREATE INDEX IX_VOT_Correlation
        ON dbo.VendorOutboundTransactions (VendorName, VendorLoadId, Status)
        WHERE Status IN ('PENDING', 'ACK');
    PRINT 'Created index IX_VOT_Correlation.';
END
GO

-- Index for "VendorRequestId lookup" — used when FK support gives us a requestId to investigate
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VOT_VendorRequestId' AND object_id = OBJECT_ID('dbo.VendorOutboundTransactions'))
BEGIN
    CREATE INDEX IX_VOT_VendorRequestId
        ON dbo.VendorOutboundTransactions (VendorName, VendorRequestId)
        WHERE VendorRequestId IS NOT NULL;
    PRINT 'Created index IX_VOT_VendorRequestId.';
END
GO

-- Check constraint to enforce known status values
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_VOT_Status')
BEGIN
    ALTER TABLE dbo.VendorOutboundTransactions
    ADD CONSTRAINT CK_VOT_Status CHECK (Status IN (
        'PENDING', 'ACK', 'CONFIRMED', 'REJECTED',
        'HTTP_FAIL', 'TRANSPORT_FAIL', 'RATE_LIMITED', 'SKIPPED', 'DEAD_LETTER'
    ));
    PRINT 'Created check constraint CK_VOT_Status.';
END
GO

PRINT 'dbo.VendorOutboundTransactions is ready.';
GO
