# _attic — retired code, kept for reference

Code in this folder is **superseded and not part of any active build**. It is kept
on disk only so prior implementations can be consulted if needed. Nothing in the
main `VectorVendorIntegration.sln` references anything here.

## FourKitesIntegration/

First-generation FourKites integration (its own `FourKitesIntegration.sln` with
`.Core`, `.OutboundService`, `.WebhookReceiver`, `.SmokeTest`, plus SqlMigrations).

Retired on 2026-06-23. Fully replaced by the vendor-adapter framework:

| Old (here, retired)                          | Replacement (active)                                   |
|----------------------------------------------|--------------------------------------------------------|
| `FourKitesIntegration.Core` (client/mapping) | `_build\Vendor.FourKites` + `_build\Vendor.Common`     |
| `FourKitesIntegration.OutboundService`       | `Vendor.FourKites.FourKitesAdapter` (status/check-call/POD/create/delete) |
| `FourKitesIntegration.WebhookReceiver`       | `_OTR_API\Controllers\VendorWebhookController` (generic, vendor-by-name) |
| `Core\Mapping\Edi214Mapper.cs`               | **Salvaged** to `Vendor.FourKites\Mapping\Edi214Reference.cs` |

The only piece deliberately carried forward was the EDI 214 status/reason code
catalog (`Edi214Mapper.cs`), now `Edi214Reference.cs` in the FourKites adapter
project — reference data for refining the live `LoadStatusMapper`.

Safe to delete this folder entirely once the new path has run in production long
enough that the old reference is no longer useful.
