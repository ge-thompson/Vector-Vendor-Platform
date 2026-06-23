-- 02_OutboundTransactions.sql
-- ─────────────────────────────────────────────────────────────────────────────
-- Logs every outbound API call we make to FourKites. Populated by OutboundService.
-- Updated when matching webhooks arrive (via WebhookReceiver -> background processor).
-- See Reference Doc Sections 9.5 and 11 for column meanings and lifecycle.
-- ─────────────────────────────────────────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'FourKitesOutboundTransactions')
BEGIN
    CREATE TABLE dbo.FourKitesOutboundTransactions
    (
        TransactionId        BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        VectorLoadId         NVARCHAR(50)         NULL,
        UpdateType           NVARCHAR(30)         NOT NULL,  -- createShipment | locationUpdate | eventUpdate | etc.
        BillToCode           NVARCHAR(50)         NULL,
        PrimaryReference     NVARCHAR(100)        NULL,
        ExpectedCallbackType NVARCHAR(80)         NULL,      -- LOAD_CREATION | STOP_ARRIVAL | NONE | etc.
        Status               NVARCHAR(20)         NOT NULL DEFAULT 'PENDING',
            -- PENDING | ACK | CONFIRMED | REJECTED | HTTP_FAIL | TRANSPORT_FAIL | DEAD_LETTER
        HttpStatusCode       INT                  NULL,
        FourKitesRequestId   NVARCHAR(50)         NULL,
        FourKitesLoadId      BIGINT               NULL,
        RequestPayload       NVARCHAR(MAX)        NULL,
        ResponseBody         NVARCHAR(MAX)        NULL,
        WebhookErrors        NVARCHAR(MAX)        NULL,
        CreatedUtc           DATETIME2            NOT NULL DEFAULT SYSUTCDATETIME(),
        AckUtc               DATETIME2            NULL,
        ConfirmedUtc         DATETIME2            NULL,
        INDEX IX_Outbound_PrimaryReference NONCLUSTERED (PrimaryReference),
        INDEX IX_Outbound_FkLoadId         NONCLUSTERED (FourKitesLoadId),
        INDEX IX_Outbound_Status_Created   NONCLUSTERED (Status, CreatedUtc)
    );
    PRINT 'Created dbo.FourKitesOutboundTransactions';
END
ELSE
BEGIN
    PRINT 'dbo.FourKitesOutboundTransactions already exists, skipped.';
END
GO
