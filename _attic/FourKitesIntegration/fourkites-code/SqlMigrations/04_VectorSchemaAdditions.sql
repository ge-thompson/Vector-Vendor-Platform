-- 04_VectorSchemaAdditions.sql
-- ─────────────────────────────────────────────────────────────────────────────
-- Additions to existing Vector FBS tables. EDIT THE TABLE NAMES below to match
-- your actual Vector schema (Shipper, Load). These are guesses based on a typical
-- broker-software layout — verify against your actual DB.
--
-- All changes are additive (ADD COLUMN) — no data migration risk.
-- ─────────────────────────────────────────────────────────────────────────────

-- ── Shipper additions ───────────────────────────────────────────────────────
-- Replace [Shipper] with your actual table name.
DECLARE @SchemaWarnShipper NVARCHAR(200) = 'INSTRUCTION: edit this script with your real Shipper table name before running.';

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'FourKitesBillToCode' AND Object_ID = Object_ID(N'dbo.Shipper'))
BEGIN
    ALTER TABLE dbo.Shipper ADD FourKitesBillToCode NVARCHAR(50) NULL;
    PRINT 'Added Shipper.FourKitesBillToCode';
END

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'SendEdi214' AND Object_ID = Object_ID(N'dbo.Shipper'))
BEGIN
    ALTER TABLE dbo.Shipper ADD SendEdi214 BIT NOT NULL DEFAULT 1;
    PRINT 'Added Shipper.SendEdi214 (default 1 = enabled)';
END

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'SendFourKitesAPI' AND Object_ID = Object_ID(N'dbo.Shipper'))
BEGIN
    ALTER TABLE dbo.Shipper ADD SendFourKitesAPI BIT NOT NULL DEFAULT 0;
    PRINT 'Added Shipper.SendFourKitesAPI (default 0 = disabled)';
END

-- ── Load additions ──────────────────────────────────────────────────────────
-- Replace [Load] with your actual table name (might be Load, Loads, Shipment, Order, etc.)

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'FourKitesLoadId' AND Object_ID = Object_ID(N'dbo.Load'))
BEGIN
    ALTER TABLE dbo.[Load] ADD FourKitesLoadId BIGINT NULL;
    PRINT 'Added Load.FourKitesLoadId';
END

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'FourKitesCreatedUtc' AND Object_ID = Object_ID(N'dbo.Load'))
BEGIN
    ALTER TABLE dbo.[Load] ADD FourKitesCreatedUtc DATETIME2 NULL;
    PRINT 'Added Load.FourKitesCreatedUtc';
END

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'FourKitesEncryptedUrl' AND Object_ID = Object_ID(N'dbo.Load'))
BEGIN
    ALTER TABLE dbo.[Load] ADD FourKitesEncryptedUrl NVARCHAR(2048) NULL;
    PRINT 'Added Load.FourKitesEncryptedUrl';
END

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'FourKitesTrackingStatus' AND Object_ID = Object_ID(N'dbo.Load'))
BEGIN
    ALTER TABLE dbo.[Load] ADD FourKitesTrackingStatus NVARCHAR(20) NULL;
    PRINT 'Added Load.FourKitesTrackingStatus';
END

PRINT '--- Vector schema additions complete ---';
PRINT 'Reminder: if Shipper or Load is not your actual table name, edit this script and re-run.';
GO
