-- 01_OutboundQueue.sql
-- ─────────────────────────────────────────────────────────────────────────────
-- NOT REQUIRED for the current architecture (Vector calls OutboundService via HTTP).
-- If you later switch to an outbox pattern (SQL polling), this is the schema to use.
-- Kept here as a reference for future architecture changes.
--
-- Run order: this script can be skipped for the HTTP-based deployment.
-- ─────────────────────────────────────────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'FourKitesOutboundQueue')
BEGIN
    CREATE TABLE dbo.FourKitesOutboundQueue
    (
        QueueId            BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        VectorLoadId       NVARCHAR(50)         NOT NULL,
        EndpointPath       NVARCHAR(100)        NOT NULL,  -- /api/fourkites/dispatch-update etc.
        PayloadJson        NVARCHAR(MAX)        NOT NULL,
        Status             NVARCHAR(20)         NOT NULL DEFAULT 'NEW',  -- NEW | CLAIMED | DONE | FAILED
        ClaimedBy          NVARCHAR(100)        NULL,
        ClaimedAtUtc       DATETIME2            NULL,
        AttemptCount       INT                  NOT NULL DEFAULT 0,
        LastAttemptedAtUtc DATETIME2            NULL,
        LastErrorMessage   NVARCHAR(4000)       NULL,
        CreatedUtc         DATETIME2            NOT NULL DEFAULT SYSUTCDATETIME(),
        CompletedUtc       DATETIME2            NULL,
        INDEX IX_Queue_Status_Created NONCLUSTERED (Status, CreatedUtc)
    );
    PRINT 'Created dbo.FourKitesOutboundQueue';
END
ELSE
BEGIN
    PRINT 'dbo.FourKitesOutboundQueue already exists, skipped.';
END
GO
