/*
================================================================================
  01_ClientProfiles.sql
  Per-shipper, per-vendor routing configuration. Read by VendorDispatcher
  on every dispatch (with in-memory caching in the framework).
  Idempotent.
================================================================================
*/

USE VendorAPI_FK;
GO

IF OBJECT_ID('dbo.ClientProfiles', 'U') IS NULL
BEGIN
    PRINT 'Creating table dbo.ClientProfiles...';

    CREATE TABLE dbo.ClientProfiles
    (
        ProfileId       BIGINT IDENTITY(1,1) NOT NULL,
        ShipperCode     NVARCHAR(50)  NOT NULL,
        VendorName      NVARCHAR(50)  NOT NULL,
        IsActive        BIT           NOT NULL CONSTRAINT DF_ClientProfiles_IsActive DEFAULT (1),
        EnabledEvents   NVARCHAR(500) NOT NULL,                -- CSV: "LoadCreatedEvent,LocationReportedEvent,..."
        ConfigJson      NVARCHAR(MAX) NOT NULL,                -- vendor-specific config blob
        Notes           NVARCHAR(500) NULL,                    -- free-text for operators
        CreatedUtc      DATETIME2(3)  NOT NULL CONSTRAINT DF_ClientProfiles_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        UpdatedUtc      DATETIME2(3)  NOT NULL CONSTRAINT DF_ClientProfiles_UpdatedUtc DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_ClientProfiles PRIMARY KEY CLUSTERED (ProfileId),
        CONSTRAINT UQ_ClientProfiles_ShipperVendor UNIQUE (ShipperCode, VendorName)
    );

    PRINT 'Created dbo.ClientProfiles.';
END
ELSE
BEGIN
    PRINT 'Table dbo.ClientProfiles already exists. Skipping CREATE.';
END
GO

-- Lookup index used by VendorDispatcher: "give me active rows for ShipperCode X"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientProfiles_Lookup' AND object_id = OBJECT_ID('dbo.ClientProfiles'))
BEGIN
    CREATE INDEX IX_ClientProfiles_Lookup
        ON dbo.ClientProfiles (ShipperCode, IsActive)
        INCLUDE (VendorName, EnabledEvents, ConfigJson);
    PRINT 'Created index IX_ClientProfiles_Lookup.';
END
GO

-- Trigger to maintain UpdatedUtc automatically
IF OBJECT_ID('dbo.tr_ClientProfiles_UpdatedUtc', 'TR') IS NULL
BEGIN
    EXEC('
        CREATE TRIGGER dbo.tr_ClientProfiles_UpdatedUtc
            ON dbo.ClientProfiles
            AFTER UPDATE
        AS
        BEGIN
            SET NOCOUNT ON;
            UPDATE p
            SET UpdatedUtc = SYSUTCDATETIME()
            FROM dbo.ClientProfiles p
            INNER JOIN inserted i ON p.ProfileId = i.ProfileId;
        END
    ');
    PRINT 'Created trigger tr_ClientProfiles_UpdatedUtc.';
END
GO

PRINT 'dbo.ClientProfiles is ready.';
GO
