/*
================================================================================
  04_LoadCrossReference.sql
  Maps VectorLoadId to each vendor's internal load id.
  This is the framework-pure alternative (Option B) to adding vendor-specific
  columns to Vector's [Load] table.

  Adding a new vendor requires zero schema change here — just new rows.

  Written by FourKitesWebhookProcessor.OnConfirmedAsync (and other adapters'
  equivalent methods) via usp_RecordVendorLoadCrossReference.
  Idempotent.
================================================================================
*/

USE VendorAPI_FK;
GO

IF OBJECT_ID('dbo.LoadCrossReference', 'U') IS NULL
BEGIN
    PRINT 'Creating table dbo.LoadCrossReference...';

    CREATE TABLE dbo.LoadCrossReference
    (
        VectorLoadId    NVARCHAR(50)   NOT NULL,
        VendorName      NVARCHAR(50)   NOT NULL,
        VendorLoadId    NVARCHAR(100)  NOT NULL,
        TrackingStatus  NVARCHAR(20)   NULL,             -- "CREATED", "ACTIVE", "STOPPED", "DELIVERED"
        CreatedUtc      DATETIME2(3)   NOT NULL CONSTRAINT DF_LCR_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        UpdatedUtc      DATETIME2(3)   NOT NULL CONSTRAINT DF_LCR_UpdatedUtc DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_LoadCrossReference PRIMARY KEY CLUSTERED (VectorLoadId, VendorName)
    );

    PRINT 'Created dbo.LoadCrossReference.';
END
ELSE
BEGIN
    PRINT 'Table dbo.LoadCrossReference already exists. Skipping CREATE.';
END
GO

-- Reverse lookup: "what's the VectorLoadId for this VendorLoadId from FK?"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LCR_VendorLoadId' AND object_id = OBJECT_ID('dbo.LoadCrossReference'))
BEGIN
    CREATE INDEX IX_LCR_VendorLoadId
        ON dbo.LoadCrossReference (VendorName, VendorLoadId)
        INCLUDE (VectorLoadId, TrackingStatus);
    PRINT 'Created index IX_LCR_VendorLoadId.';
END
GO

-- Trigger to maintain UpdatedUtc automatically
IF OBJECT_ID('dbo.tr_LoadCrossReference_UpdatedUtc', 'TR') IS NULL
BEGIN
    EXEC('
        CREATE TRIGGER dbo.tr_LoadCrossReference_UpdatedUtc
            ON dbo.LoadCrossReference
            AFTER UPDATE
        AS
        BEGIN
            SET NOCOUNT ON;
            UPDATE x
            SET UpdatedUtc = SYSUTCDATETIME()
            FROM dbo.LoadCrossReference x
            INNER JOIN inserted i ON x.VectorLoadId = i.VectorLoadId AND x.VendorName = i.VendorName;
        END
    ');
    PRINT 'Created trigger tr_LoadCrossReference_UpdatedUtc.';
END
GO

PRINT 'dbo.LoadCrossReference is ready.';
GO
