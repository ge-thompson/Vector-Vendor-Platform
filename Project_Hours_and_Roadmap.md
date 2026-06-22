\# Vector Vendor Integration Platform — Hours, Philosophy, and Roadmap



Reference document capturing the hours discussion and forward-looking scope

for the Vector Vendor Integration Platform engagement. Captured at the

close of the FK Phase 1 production verification session.



Suggested location in repo: `\\\_deliverables/15\\\_Project\\\_Hours\\\_and\\\_Roadmap/README.md`



\---



\## 1. Hours — work performed to date



| Hours | Line item |

|---:|---|

| 5 | Fourkites API walkthrough and tracking requirement review \\| Review of the three available API bundles (Load Visibility, Carrier Visibility, Order Visibility) \\| Map Fourkites' tracking model to Vector's load lifecycle and identify capability gaps |

| 8 | Vendor Integration architecture and strategy document with locked design decisions \\| Confirm DLL boundaries for FBS, OTR API, and VB.NET consumers \\| Lock vocabulary and canonical load lifecycle (EDI 204 → 990 → Trip Load → 214 → Check Calls → Delivery → 210) \\| Plan OTR API .NET upgrade and webhook hosting |

| 12 | Create API framework for integrating Customer Status Collaboration to allow future Customer API integration \\| Vendor-agnostic event model, dispatch engine, and audit infrastructure \\| Per-vendor configuration with database-driven status code mapping and hot reload \\| Reusable platform that future vendor adapters plug into without changes to OTR API |

| 3 | Add webhooks to receive responses from Vendor \\| Receiver hosting with configurable authentication (API key, basic, or none) \\| Idempotency design so duplicate vendor callbacks process safely |

| 5 | Validation test suite for the integration framework \\| Mock infrastructure for offline testing of all vendor paths \\| Repeatable test coverage to catch regressions when future vendors are added or contracts change |

| 7 | Connect new Vendor Integration with the existing OTR Website \\| Wire TrackLoad, UpdateTrackLoad, CancelLoadTracking, and SendStatus into the integration framework \\| Enriched load assignment payload (carrier, driver, equipment, dispatcher, shipper, ordered stops with pickup/intermediate/delivery roles) \\| Admin endpoints for live status and configuration refresh without restart |

| 4 | Fourkites API spec deep-dive and Fourkites adapter rewrite to match the actual contract \\| Authentication header, multi-environment URL support, PATCH method for updates, base64 document upload \\| Cross-reference table to persist Fourkites' returned load id and route subsequent updates and deletes correctly |

| 5 | End-to-end Fourkites production verification with full Create / Update / Cancel lifecycle proven against real Fourkites loads \\| Validation test suite updated to the new contract and passing \\| Open questions documented for Fourkites support team follow-up |

| \*\*49\*\* | \*\*Total to date\*\* |



\---



\## 2. Hours philosophy



Captured in the words used during the discussion, because the framing is

worth preserving:



> Using a tool to assist takes fewer physical hours of actual development,

> but that's a part of using any tool to assist. If you give the tool more

> work for less time, it makes it efficient — but you don't always charge

> less. That's part of delivering a professional solution. You don't steal

> the hours, but you make sure the hours meet the effort and the product

> produced.



Restated as the operating principle for this engagement:



\- \*\*Bill the engineered solution and the engineering judgment\*\* — not the

&#x20; keystrokes. A senior engineer who solves a problem in two hours through

&#x20; experience doesn't bill less than a junior who takes ten. Both bills

&#x20; reflect the value of the work, not the time spent on the keyboard.

\- \*\*Tools compress execution time. They don't compress thinking,

&#x20; architecture decisions, spec interpretation, verification cycles, or

&#x20; judgment calls.\*\* Those are the billable activities.

\- \*\*Foundational and platform-building work is invisible relative to its

&#x20; value.\*\* Strategy docs and frameworks tend to look like "nothing to point

&#x20; at," but they're what make every subsequent piece of work cheap. The FK

&#x20; adapter rewrite ran 4 hours instead of 30 \*because\* of the framework

&#x20; work that preceded it.

\- \*\*Responsibility for the production behavior sits with Vector's lead

&#x20; engineer\*\*, regardless of how the implementation was produced. That

&#x20; responsibility is itself part of what's billed.



What Vector's lead engineer (Glen) brought to the engagement that the

tooling did not:



\- Domain knowledge of the freight brokerage model — Trip Load, Check Call,

&#x20; EDI 204/990/214/210 semantics, the canonical Vector lifecycle

\- Framework boundary decisions — what's vendor-agnostic vs vendor-specific

\- The hard line that the OTR API contract doesn't change as new vendors

&#x20; come online

\- Pushback on speculation — refusing guessed schema/column/proc names,

&#x20; forcing verification against the real DB before naming things

\- Precision in framing — pushing back on "missing" to clarify "incomplete

&#x20; code path" when the SP was never written

\- Production-safety defaults — choosing to ship Phase 1 with

&#x20; `VendorDispatch.Enabled=false`

\- Iteration ownership — picking up real FK production cycles and pattern-

&#x20; matching across responses to drive the next request



The 49 hours reflect the work product as a whole, not the share of

typing on either side of the keyboard.



\---



\## 3. Path to invoiceable



Hours stay accurate. Invoicing is gated on the visible deliverable being in

the client's hands. That means:



1\. \*\*Phase 1 in beta\*\* — framework + FK adapter deployed to production OTR

&#x20;  API, `VendorDispatch.Enabled=false` so no behavior change to existing

&#x20;  traffic. Cleared for `Enabled=true` once FK CSM open questions are

&#x20;  answered.

2\. \*\*Phase 2 in place\*\* — FBS talks directly to the Vendor Integration

&#x20;  framework via its existing events. Because the events already fire

&#x20;  inside FBS, this is primarily routing existing triggers to

&#x20;  VendorDispatcher rather than building new event detection.



\### Phase 1 → beta-ready (estimated \~6-10 additional hours)



\- Production deployment: code on the real OTR API box, `VendorAPI\\\_FK` DB

&#x20; created on the production SQL host (not LocalDB), ClientProfile seeded

&#x20; for the production env

\- `VendorDispatch.Enabled = "false"` at launch — framework loaded, no

&#x20; behavior change

\- FK CSM open questions resolved or accepted as-is — at minimum O-001

&#x20; (vectorScac value) so loads don't render as "undefined (VCTR)" in FK

\- HMAC auth path tested with real signed requests

\- Webhook receiver endpoint exposed publicly — DNS, public route, inbound

&#x20; auth header from FK CSM

\- Flip `Enabled=true` on a canary load or two, watch

&#x20; `VendorOutboundTransactions`, confirm FK shipments view reflects them



\### Phase 2 → FBS deliverable (estimated \~3-8 additional hours)



Smaller than initially estimated because the FBS events already exist. The

work is routing those triggers to call VendorDispatcher, not building event

detection. Specifics depend on FBS internals.



\### Estimated total path to invoiceable: 49 + \~10-15 = \~60-65 hours



\---



\## 4. Post-deployment scope (separate engagements)



Three distinct buckets of work after the platform is live, each worth

scoping and pricing separately.



\### Ongoing maintenance / iteration



Reactive work surfacing as real load traffic flows: FK adds a field, a new

edge case appears, a stop type Vector hasn't seen, a rate-limit tweak, a

status code mapping refinement.



\- Usually small and ticket-shaped

\- Often billed hourly against a retainer or per-ticket

\- The framework design helps significantly — most changes are config rows

&#x20; or mapping table edits (no code deploy), not code changes. Some still

&#x20; need code.



\### New vendor onboarding



Each new vendor (Project44, MacroPoint, CH Robinson, etc.) is its own

engagement.



\- Framework keeps each one short — new adapter assembly + ClientProfile

&#x20; row + status mapping rows — but "short" still means real work

\- API spec walkthrough, auth model, payload shape, event semantics,

&#x20; cross-reference state model if applicable, webhook patterns, end-to-end

&#x20; verification

\- Realistic estimate: 15-25 hours per vendor once the pattern is grooved

\- The first vendor (FK) took 49 because it built the platform. The next

&#x20; ones inherit it.



\### Monitoring / operator dashboard



This is the one genuinely new piece of scope.



\*\*Real-time operator view (the daily driver):\*\*



\- Live tail of `VendorOutboundTransactions` — last N events, color-coded

&#x20; by Status (ACK / HTTP\_FAIL / RateLimit / Skipped)

\- Filter by VendorName, VectorLoadId, EventTypeName, time window

\- Click a row → see RequestPayload, ResponseBody, ErrorMessage

\- 24-hour rollups: success rate, failure rate, retry rate, skip rate

\- Per-vendor health: requests/sec, p50/p95/p99 latency from DurationMs



\*\*Investigation view:\*\*



\- "What happened to load X across all vendors" — query by VectorLoadId,

&#x20; get the full timeline

\- "Show me everything that failed in the last hour" — failure triage

\- Cross-reference status per load (ACTIVE / STOPPED / never-Created) per

&#x20; vendor



\*\*Configuration view (low-write):\*\*



\- ClientProfile list with Enabled toggle per vendor

\- Status mapping table editor with the refresh button wired to

&#x20; `/api/admin/refresh-mappings`

\- Per-vendor verbosity toggle (Generous / Conservative)

\- Read-only view of LoadCrossReference



\*\*Alerts (later phase, maybe):\*\*



\- Error rate above threshold for X minutes → email or webhook

\- Vendor returning consistent 401s → likely an auth rotation needed

\- Webhook receiver silent for N hours → FK might have something wrong



\*\*Build decisions to make before scoping:\*\*



\- Standalone web app, or page inside an existing Vector admin app?

\- ASP.NET MVC, Razor Pages, Blazor, or SPA (React/Vue) on Web API?

\- Read-only first, then add config-edit pieces?

\- Identity model — Vector AD, SSO, or local users?



\*\*Honest hour ranges by scope:\*\*



\- Read-only operator dashboard (real-time + investigation): 30-50 hours

\- + Configuration editing UI: another 20-30 hours

\- + Alerts: another 15-25 hours

\- + Multi-vendor support baked in from start: minimal extra if designed

&#x20; for it



Total realistic dashboard build: \*\*50-100 hours\*\* depending on scope and

tech stack.



\*\*Recommendation:\*\* start with the read-only operator dashboard. That's

where \~80% of the value is. Configuration editing can stay in SSMS for a

while if needed; alerts can wait until there's enough traffic to know

what thresholds matter. Scope as its own project after Phase 2 ships

and the integration has been live long enough to know what operators

actually want to see.



\---



\## 5. Open issues to revisit



Carried forward as project-level concerns (separate from the FK API open

questions in `\\\_deliverables/14\\\_FK\\\_Open\\\_Questions/`):



\- \*\*Credentials hardening\*\* — `WebCallFunctions.cs` has TT/FBS/SMTP

&#x20; credentials in plaintext; SQL `sa` password in `Web.config`. Move to

&#x20; config-only or a secrets store before going live. Rotation needed for

&#x20; anything that leaked through dev work.

\- \*\*Admin endpoints behind auth\*\* — `/api/admin/dispatcher-status` and

&#x20; `/api/admin/refresh-mappings` currently have no auth (intended for

&#x20; local-only use). Add auth before exposing externally.

\- \*\*Hardcoded TT URLs in WebCallFunctions.cs\*\* — move to `Web.config` so

&#x20; dev/staging/prod can flip without recompiling.



\---



\## 6. Notes on revisiting this document



This doc is captured to settle the hours discussion and the forward

roadmap in one place. If hours estimates need updating after Phase 1 beta

or Phase 2 ships, update the relevant section and date the change. The

philosophy section (Section 2) should remain stable — it's the operating

principle for the engagement, not a per-phase artifact.

