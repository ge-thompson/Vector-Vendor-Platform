/*
================================================================================
  00_CreateDatabase.sql
  Creates VendorAPI_FK database. Idempotent — safe to re-run.
  Target server: 10.10.9.10\SQLEXPRESS12
================================================================================
*/

IF DB_ID('VendorAPI_FK') IS NULL
BEGIN
    PRINT 'Creating database VendorAPI_FK...';
    CREATE DATABASE VendorAPI_FK;
END
ELSE
BEGIN
    PRINT 'Database VendorAPI_FK already exists. Skipping creation.';
END
GO

-- Set recovery model and options. Safe to re-apply.
ALTER DATABASE VendorAPI_FK SET RECOVERY SIMPLE;            -- Phase 1: simple recovery, daily full backup is enough
ALTER DATABASE VendorAPI_FK SET READ_COMMITTED_SNAPSHOT ON; -- Reduces lock contention on heavy read+write workloads
GO

USE VendorAPI_FK;
GO

-- Default schema is dbo. Nothing else to create at the DB level.
PRINT 'Database VendorAPI_FK is ready.';
GO
