/*
================================================================================
  07_SeedData.sql
  Sample ClientProfile row for FourKites. Edit the ConfigJson before running
  in any environment beyond local dev:
    - apiKey: replace with real FK API key (sandbox to start)
    - billToCode: replace with FK-assigned shipper code
    - webhookAuth: replace with credentials FK CSM provisions for inbound auth

  Idempotent — uses MERGE. If a row already exists for (VECTOR_DEFAULT, FourKites),
  re-running OVERWRITES its config. If you've manually edited the row, edit this
  script to match before re-running.
================================================================================
*/

USE VendorAPI_FK;
GO

DECLARE @ConfigJson NVARCHAR(MAX) = N'{
    "apiKey": "REPLACE_WITH_FK_API_KEY",
    "billToCode": "REPLACE_WITH_FK_BILLTOCODE",
    "baseUrl": "https://api.fourkites.com",
    "timeoutSeconds": 15,
    "dispatchPolicy": {
        "verbosity": "Generous"
    },
    "webhookAuth": {
        "mode": "apikey",
        "headerName": "X-FourKites-Token",
        "headerValue": "REPLACE_WITH_INBOUND_TOKEN"
    }
}';

DECLARE @EnabledEvents NVARCHAR(500) = N'LoadCreatedEvent,LoadAssignedEvent,LocationReportedEvent,LoadStatusEvent,LoadTrackingStoppedEvent,DocumentAvailableEvent';

MERGE dbo.ClientProfiles AS target
USING (SELECT 'VECTOR_DEFAULT' AS ShipperCode, 'FourKites' AS VendorName) AS src
   ON target.ShipperCode = src.ShipperCode AND target.VendorName = src.VendorName
WHEN MATCHED THEN
    UPDATE SET
        IsActive = 1,
        EnabledEvents = @EnabledEvents,
        ConfigJson = @ConfigJson,
        Notes = 'Phase 1 default profile — routes everything to FourKites. Edit ConfigJson with real credentials before production.',
        UpdatedUtc = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (ShipperCode, VendorName, IsActive, EnabledEvents, ConfigJson, Notes)
    VALUES ('VECTOR_DEFAULT', 'FourKites', 1, @EnabledEvents, @ConfigJson,
            'Phase 1 default profile — routes everything to FourKites. Edit ConfigJson with real credentials before production.');
GO

PRINT 'Seed data installed. REMEMBER to update ConfigJson with real credentials before production.';
SELECT ProfileId, ShipperCode, VendorName, IsActive, LEFT(ConfigJson, 100) + '...' AS ConfigPreview
FROM dbo.ClientProfiles
WHERE ShipperCode = 'VECTOR_DEFAULT';
GO
