-- =============================================================================
-- spTrackingByLoadID_Get
--
-- Database: VectorOTR_TT (DESKTOP-4DEA4AP\SQLDEVELOP in dev; production server
--           per your topology)
-- Connection string name: hostTT
--
-- WHY THIS EXISTS:
--   OTR API's DataTruckerToolsTracking.GetLoadTracking method calls this proc
--   to resolve a tracking record by loadNumber. The C# method was already in
--   the codebase, but the proc itself had never been created.
--
--   The vendor check-call code path in SendStatus uses this lookup to translate
--   the inbound loadNumber to the Vector load id (VectorID column) so the
--   outbound check call can be tagged with the canonical Vector load identifier.
--
--   Without this proc, GetLoadTracking returns an empty Load object (its inner
--   try/catch swallows the missing-proc error), VectorID stays 0, and the
--   check-call code correctly skips dispatch instead of firing an event tagged
--   with VectorLoadId='0'.
--
-- SAFETY:
--   Pure SELECT. No writes. Reads from existing dbo.Tracking table.
--   Idempotent CREATE OR ALTER pattern so re-running this script is safe.
-- =============================================================================

USE VectorOTR_TT;
GO

CREATE OR ALTER PROCEDURE [dbo].[spTrackingByLoadID_Get]
    @loadNumber varchar(10)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP 1
        ID,
        partnerId,
        accountId,
        loadTrackExternalId,
        loadNumber,
        dispatcherId,
        dispatcherEmail,
        dispatcherPhoneNumber,
        textmessage,
        loadType,
        trailerType,
        driverCell,
        trailerNumber,
        truckNumber,
        driverName,
        driverType,
        driverComments,
        loadNotes,
        isTeamLoad,
        carrierDispatcherEmail,
        CarrierID,
        BrokerID,
        ShipperID,
        VectorID
    FROM dbo.Tracking
    WHERE loadNumber = @loadNumber
      AND Deleted    = 0
    ORDER BY ID DESC;
END
GO
