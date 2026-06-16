-- ============================================================================
-- 08_per_load_investigation.sql
-- Drill-down query: called when user clicks a VectorLoadId in the dashboard.
-- Returns 3 result sets:
--   1. Outbound transactions (chronological)
--   2. Inbound callbacks (chronological)
--   3. Cross-references (vendor IDs assigned to this load)
-- Wrapper around the stored procedure from Deliverable #7.
-- ============================================================================

-- Replace 'LOAD12345' with the actual VectorLoadId from the dashboard click
EXEC dbo.usp_GetLoadAuditTrail @VectorLoadId = 'LOAD12345';
