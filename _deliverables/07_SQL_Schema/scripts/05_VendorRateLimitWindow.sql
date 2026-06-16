/*
================================================================================
  05_VendorRateLimitWindow.sql
  Optional DB-backed rate limit state. Useful only if multiple app instances
  share rate limit budget (e.g., OTR API runs on multiple IIS nodes for HA).

  Phase 1: single instance — in-memory tracking in the adapter is sufficient.
  This table is created so the schema is ready when needed; it stays empty
  unless an adapter chooses to write to it.

  Idempotent.
================================================================================
*/

USE VendorAPI_FK;
GO

IF OBJECT_ID('dbo.VendorRateLimitWindow', 'U') IS NULL
BEGIN
    PRINT 'Creating table dbo.VendorRateLimitWindow...';

    CREATE TABLE dbo.VendorRateLimitWindow
    (
        VendorName      NVARCHAR(50)   NOT NULL,
        WindowStartUtc  DATETIME2(3)   NOT NULL,             -- start of the rate-limit window
        WindowSeconds   INT            NOT NULL,             -- size of window (e.g., 60 for FK's per-minute limit)
        CallCount       INT            NOT NULL CONSTRAINT DF_VRLW_CallCount DEFAULT (0),
        LimitMax        INT            NOT NULL,             -- e.g., 60 for FK
        IsCurrentlyLimited BIT         NOT NULL CONSTRAINT DF_VRLW_IsLimited DEFAULT (0),
        ResetUtc        DATETIME2(3)   NULL,                 -- if rate-limited, when does it reset?
        LastUpdatedUtc  DATETIME2(3)   NOT NULL CONSTRAINT DF_VRLW_LastUpdatedUtc DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_VendorRateLimitWindow PRIMARY KEY CLUSTERED (VendorName, WindowStartUtc)
    );

    PRINT 'Created dbo.VendorRateLimitWindow.';
END
ELSE
BEGIN
    PRINT 'Table dbo.VendorRateLimitWindow already exists. Skipping CREATE.';
END
GO

-- Lookup index
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VRLW_Vendor_Recent' AND object_id = OBJECT_ID('dbo.VendorRateLimitWindow'))
BEGIN
    CREATE INDEX IX_VRLW_Vendor_Recent
        ON dbo.VendorRateLimitWindow (VendorName, WindowStartUtc DESC)
        INCLUDE (CallCount, IsCurrentlyLimited, ResetUtc);
    PRINT 'Created index IX_VRLW_Vendor_Recent.';
END
GO

PRINT 'dbo.VendorRateLimitWindow is ready (table empty; populated only by DB-backed rate trackers).';
GO
