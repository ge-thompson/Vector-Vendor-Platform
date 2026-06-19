# OTR API patches discovered during Phase 1 integration

Small, surgical changes to OTR API code or DB objects that surfaced while
wiring up Phase 1 check calls. Each item is its own file with a header
explaining what's needed and why.

These aren't part of the vendor integration framework -- they're gaps in
the existing OTR API that the framework's new code paths exposed. Versioning
them here so future deployments don't lose them.

## What's here

| File | Purpose |
|---|---|
| `01_spTrackingByLoadID_Get.sql` | Lookup SP for SendStatus -> resolve loadNumber to VectorID |
