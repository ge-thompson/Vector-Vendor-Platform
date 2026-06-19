/*
================================================================================
  08_VendorStatusMapping.sql
  DB-driven overrides for status code translation.

  TWO DIRECTIONS in one table (distinguished by Direction column):

    Inbound  : upstream raw code -> framework LoadStatusType enum
               Example: TruckTools "X1" -> "ArrivedAtPickup"
               Used by callers like OTR API's TruckToolsStatusMapper.

    Outbound : framework LoadStatusType -> vendor-specific code
               Example: "ArrivedAtPickup" -> EDI 214 "X1" for FourKites
               Used by adapter mappers like Vendor.FourKites.LoadStatusMapper.

  The hardcoded mappers in code are the TEMPLATE. Rows here OVERRIDE the template.
  Missing row -> code falls back to its hardcoded default. Idempotent.

  Phase 1 seed: outbound rows for FourKites (matches the existing hardcoded
  template, so behavior is unchanged until someone edits a row). Inbound rows
  are intentionally NOT seeded -- the TruckToolsStatusMapper file will ship
  with hardcoded defaults and DB rows are added later as Vector learns which
  TT codes appear in real traffic (open item O-002).
================================================================================
*/

USE VendorAPI_FK;
GO

IF OBJECT_ID('dbo.VendorStatusMapping', 'U') IS NULL
BEGIN
    PRINT 'Creating table dbo.VendorStatusMapping...';

    CREATE TABLE dbo.VendorStatusMapping
    (
        MappingId       BIGINT IDENTITY(1,1) NOT NULL,
        VendorName      NVARCHAR(50)  NOT NULL,                          -- e.g. "FourKites"; "GLOBAL" for cross-vendor inbound
        Direction       NVARCHAR(20)  NOT NULL,                          -- "Inbound" or "Outbound"
        SourceSystem    NVARCHAR(50)  NULL,                              -- "TruckTools" for inbound; NULL for outbound
        SourceCode      NVARCHAR(50)  NOT NULL,                          -- the input value being translated
        TargetCode      NVARCHAR(50)  NOT NULL,                          -- the output value
        IsActive        BIT           NOT NULL CONSTRAINT DF_VendorStatusMapping_IsActive DEFAULT (1),
        Notes           NVARCHAR(500) NULL,
        CreatedUtc      DATETIME2(3)  NOT NULL CONSTRAINT DF_VendorStatusMapping_CreatedUtc DEFAULT (SYSUTCDATETIME()),
        UpdatedUtc      DATETIME2(3)  NOT NULL CONSTRAINT DF_VendorStatusMapping_UpdatedUtc DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_VendorStatusMapping PRIMARY KEY CLUSTERED (MappingId),
        CONSTRAINT UQ_VendorStatusMapping UNIQUE (VendorName, Direction, SourceSystem, SourceCode),
        CONSTRAINT CK_VendorStatusMapping_Direction CHECK (Direction IN ('Inbound', 'Outbound'))
    );

    PRINT 'Created dbo.VendorStatusMapping.';
END
ELSE
BEGIN
    PRINT 'Table dbo.VendorStatusMapping already exists. Skipping CREATE.';
END
GO

-- Lookup index: framework reads "give me active rows for VendorName X / Direction Y"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_VendorStatusMapping_Lookup' AND object_id = OBJECT_ID('dbo.VendorStatusMapping'))
BEGIN
    CREATE INDEX IX_VendorStatusMapping_Lookup
        ON dbo.VendorStatusMapping (VendorName, Direction, IsActive)
        INCLUDE (SourceSystem, SourceCode, TargetCode);
    PRINT 'Created index IX_VendorStatusMapping_Lookup.';
END
GO

-- Trigger to maintain UpdatedUtc automatically
IF OBJECT_ID('dbo.tr_VendorStatusMapping_UpdatedUtc', 'TR') IS NULL
BEGIN
    EXEC('
        CREATE TRIGGER dbo.tr_VendorStatusMapping_UpdatedUtc
            ON dbo.VendorStatusMapping
            AFTER UPDATE
        AS
        BEGIN
            SET NOCOUNT ON;
            UPDATE m
            SET UpdatedUtc = SYSUTCDATETIME()
            FROM dbo.VendorStatusMapping m
            INNER JOIN inserted i ON m.MappingId = i.MappingId;
        END
    ');
    PRINT 'Created trigger tr_VendorStatusMapping_UpdatedUtc.';
END
GO

/*
================================================================================
  SEED DATA: Outbound mappings for FourKites
  Mirrors the hardcoded template in Vendor.FourKites.Mapping.LoadStatusMapper.cs
  so behavior is identical until someone edits a row.

  Each row says: "when the framework dispatches a LoadStatusEvent with
  StatusType = SourceCode, FourKites should receive TargetCode (EDI 214)".

  Idempotent via MERGE — safe to re-run.
================================================================================
*/

MERGE dbo.VendorStatusMapping AS target
USING (VALUES
    ('FourKites', 'Outbound', NULL, 'ArrivedAtPickup',    'X1', 'EDI 214: Arrived at Pickup Location'),
    ('FourKites', 'Outbound', NULL, 'DepartedPickup',     'AF', 'EDI 214: Carrier Departed Pickup Location with Shipment'),
    ('FourKites', 'Outbound', NULL, 'ArrivedAtDelivery',  'X3', 'EDI 214: Arrived at Delivery Location'),
    ('FourKites', 'Outbound', NULL, 'DepartedDelivery',   'CD', 'EDI 214: Carrier Departed Delivery Location'),
    ('FourKites', 'Outbound', NULL, 'Delivered',          'D1', 'EDI 214: Completed (some shippers prefer J1 - refine after first prod data)'),
    ('FourKites', 'Outbound', NULL, 'InTransit',          'AG', 'EDI 214: Estimated Delivery (used here for in-transit progression)'),
    ('FourKites', 'Outbound', NULL, 'Dispatched',         'OA', 'EDI 214: Dispatched'),
    ('FourKites', 'Outbound', NULL, 'Exception',          'A3', 'EDI 214: Shipment Returned to Shipper (placeholder; FK accepts finer codes via SourceStatusCode pass-through)'),
    ('FourKites', 'Outbound', NULL, 'Other',              'X9', 'EDI 214: catch-all Other; adapter falls back to SourceStatusCode')
) AS src (VendorName, Direction, SourceSystem, SourceCode, TargetCode, Notes)
   ON  target.VendorName   = src.VendorName
   AND target.Direction    = src.Direction
   AND ISNULL(target.SourceSystem, '') = ISNULL(src.SourceSystem, '')
   AND target.SourceCode   = src.SourceCode
WHEN MATCHED THEN
    UPDATE SET
        TargetCode = src.TargetCode,
        IsActive   = 1,
        Notes      = src.Notes,
        UpdatedUtc = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (VendorName, Direction, SourceSystem, SourceCode, TargetCode, IsActive, Notes)
    VALUES (src.VendorName, src.Direction, src.SourceSystem, src.SourceCode, src.TargetCode, 1, src.Notes);
GO

PRINT 'Seeded FourKites outbound mappings (LoadStatusType -> EDI 214).';
GO

-- Verify
SELECT VendorName, Direction, SourceSystem, SourceCode, TargetCode, IsActive, LEFT(Notes, 60) AS NotesPreview
FROM dbo.VendorStatusMapping
ORDER BY VendorName, Direction, SourceCode;
GO

PRINT 'dbo.VendorStatusMapping is ready.';
GO
