# 95% Success Rate Dashboard

**Document:** Deliverable #8 of 11
**Version:** 1.0
**Date:** May 26, 2026
**Owner:** Glen Thompson (executor); Claude (designer)
**Prerequisites:** Deliverable #7 (SQL schema) deployed; outbound dispatch in #4 actively logging
**Related decisions:** D-011, D-024

---

## 0. Purpose

This deliverable defines how to monitor the 95% success rate SLA from FourKites. The strategy doc (D-011) flagged this as a hard SLA — losing it has commercial consequences. The dashboard provides:

- **At-a-glance status:** Are we above or below 95% right now?
- **Trend visibility:** Is the rate climbing, holding, or falling?
- **Failure attribution:** When it falls, what's failing and where?
- **Per-load drill-down:** When operations asks "what happened to load X?", one click gets the answer
- **Alerting:** Page someone when the rate crosses the threshold downward, not after the contract review

This is **operational reporting**, not application code. Glen renders these queries in whichever BI tool the shop standardizes on — Power BI, SSRS, Grafana, a custom web page, an Excel pivot connected to SQL. The queries don't care.

---

## 1. What "success" means — definition

The FK contract reads "95% success rate" but doesn't natively define success. Three reasonable definitions exist in our schema:

| Definition | SQL filter | What it measures |
|---|---|---|
| **A. Transport success** | `Status IN ('ACK', 'CONFIRMED')` | FK received our bytes (HTTP 2xx). Doesn't prove they applied the update. |
| **B. Application success** | `Status = 'CONFIRMED'` | FK's webhook confirmed the update applied. Strictest. |
| **C. Not failed** | `Status NOT IN ('HTTP_FAIL', 'TRANSPORT_FAIL', 'REJECTED', 'DEAD_LETTER')` | Inverse — counts anything that didn't outright fail. Includes PENDING (still in-flight) which inflates the rate. |

**Recommendation: use Definition A as the primary metric.**

Reasons:
1. **It matches what most carrier visibility SLAs actually measure.** FK contractually monitors HTTP-level delivery. Webhook confirmations are bonus visibility, not contract terms.
2. **It's the metric we have direct control over.** If FK's webhook fires late or not at all, we shouldn't be penalized for their downstream processing.
3. **It correlates with our actions.** When we fail to ACK, we know about it within seconds and can act.

Definition B (CONFIRMED) is a **secondary metric** — useful for spotting issues in our outbound dispatch quality (are we sending malformed payloads that FK silently drops?) but not the SLA number.

Definition C is for completeness; not for reporting.

**Glen confirms with FK CSM that "ACK rate" is the contractual metric.** If their CSM says it's something else, the queries adjust by changing one WHERE clause.

---

## 2. The metrics we track

```
                    ┌────────────────────────────┐
                    │   PRIMARY: Success Rate    │
                    │   (rolling 7-day, ACK %)   │
                    │     ▲                       │
                    │     │  95% line             │
                    │     │                       │
                    │     ●───●───●───●─●  ◄── good
                    │                  \         │
                    │                   ●  ◄── alert here
                    └────────────────────────────┘
                              │
            ┌─────────────────┼─────────────────┐
            ▼                 ▼                 ▼
   ┌─────────────────┐ ┌────────────────┐ ┌────────────────────┐
   │ Volume per day  │ │ Failures by    │ │ Per-event-type     │
   │ Total attempts  │ │ category       │ │ success rate       │
   │                 │ │ (HTTP_FAIL,    │ │                    │
   │                 │ │  RATE_LIMITED, │ │                    │
   │                 │ │  TRANSPORT,    │ │                    │
   │                 │ │  REJECTED)     │ │                    │
   └─────────────────┘ └────────────────┘ └────────────────────┘
                              │
                              ▼
                ┌─────────────────────────────┐
                │ Drill-down: list of failed  │
                │ transactions (last 24h)     │
                │ Click row → usp_GetLoadAuditTrail │
                └─────────────────────────────┘
```

### 2.1 The metrics by tier

**Tier 1 — what executives see (dashboard top):**
- Current 7-day rolling success rate (single number, color-coded)
- Today's volume (single number)
- Sparkline showing last 30 days of rate trend

**Tier 2 — what ops sees (dashboard middle):**
- Failures by category, last 24 hours
- Success rate broken down by event type (which event types are we worst at?)
- Time-of-day heatmap (do failures cluster at certain hours?)

**Tier 3 — what support uses (dashboard drill-down):**
- Recent failed transactions, filterable by event type / time
- Click any row → full audit trail for that VectorLoadId
- "Tell me everything for VectorLoadId X" input box

---

## 3. Color and threshold conventions

Consistent visual language so anyone glancing at the dashboard understands the state:

| Rate | Color | Meaning |
|---|---|---|
| ≥ 98% | **Green** | Well above SLA. No action needed. |
| 95.0% – 97.9% | **Yellow** | Above SLA but with little margin. Watch for trend. |
| 90.0% – 94.9% | **Orange** | Below SLA. Investigate now. |
| < 90% | **Red** | Critical. Page someone. |

Alerts (Section 6) trigger at orange and red.

---

## 4. The queries

Real SQL, runnable against `VendorAPI_FK`. Each query is in its own `.sql` file in the `queries\` subfolder for easy copy-paste into BI tools.

### 4.1 Headline metric — current 7-day rolling success rate

**File:** `queries\01_headline_success_rate.sql`

This is the single most important number on the dashboard. Refresh every 60 seconds.

```sql
SELECT
    CAST(
        SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1.0 ELSE 0.0 END)
        * 100.0
        / NULLIF(COUNT(*), 0)
        AS DECIMAL(5, 2)
    ) AS SuccessRatePct,
    COUNT(*) AS TotalAttempts,
    SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1 ELSE 0 END) AS Successes,
    COUNT(*) - SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1 ELSE 0 END) AS Failures
FROM dbo.VendorOutboundTransactions
WHERE VendorName = 'FourKites'
  AND CreatedUtc >= DATEADD(DAY, -7, SYSUTCDATETIME());
```

Renders as one big number with the percentage symbol and color-coded background per Section 3.

### 4.2 30-day trend sparkline

**File:** `queries\02_trend_30day.sql`

One row per day for the last 30 days, showing the rate trajectory. Renders as a line chart or sparkline.

```sql
WITH Days AS (
    SELECT TOP 30
        CAST(DATEADD(DAY, -ROW_NUMBER() OVER (ORDER BY object_id) + 1, CAST(SYSUTCDATETIME() AS DATE)) AS DATE) AS Day
    FROM sys.all_objects
)
SELECT
    d.Day,
    COUNT(t.TransactionId) AS Attempts,
    SUM(CASE WHEN t.Status IN ('ACK', 'CONFIRMED') THEN 1 ELSE 0 END) AS Successes,
    CAST(
        SUM(CASE WHEN t.Status IN ('ACK', 'CONFIRMED') THEN 1.0 ELSE 0.0 END)
        * 100.0
        / NULLIF(COUNT(t.TransactionId), 0)
        AS DECIMAL(5, 2)
    ) AS SuccessRatePct
FROM Days d
LEFT JOIN dbo.VendorOutboundTransactions t
    ON CAST(t.CreatedUtc AS DATE) = d.Day
   AND t.VendorName = 'FourKites'
GROUP BY d.Day
ORDER BY d.Day;
```

The chart should include a horizontal 95% line so you can see when we crossed.

### 4.3 Volume today

**File:** `queries\03_volume_today.sql`

```sql
SELECT
    COUNT(*) AS AttemptsToday,
    COUNT(DISTINCT VectorLoadId) AS DistinctLoadsToday,
    SUM(CASE WHEN EventTypeName = 'LocationReportedEvent' THEN 1 ELSE 0 END) AS LocationUpdates,
    SUM(CASE WHEN EventTypeName = 'LoadStatusEvent' THEN 1 ELSE 0 END) AS StatusEvents,
    SUM(CASE WHEN EventTypeName = 'LoadCreatedEvent' THEN 1 ELSE 0 END) AS LoadsCreated,
    SUM(CASE WHEN EventTypeName = 'LoadAssignedEvent' THEN 1 ELSE 0 END) AS Assignments,
    SUM(CASE WHEN EventTypeName = 'DocumentAvailableEvent' THEN 1 ELSE 0 END) AS Documents,
    SUM(CASE WHEN EventTypeName = 'LoadTrackingStoppedEvent' THEN 1 ELSE 0 END) AS TrackingStopped
FROM dbo.VendorOutboundTransactions
WHERE VendorName = 'FourKites'
  AND CreatedUtc >= CAST(SYSUTCDATETIME() AS DATE);
```

Renders as a small table or set of KPI tiles.

### 4.4 Failures by category — last 24 hours

**File:** `queries\04_failures_24h.sql`

When the rate is below threshold, this tells you WHY.

```sql
SELECT
    Status,
    ErrorCategory,
    COUNT(*) AS FailureCount,
    MIN(CreatedUtc) AS FirstSeenUtc,
    MAX(CreatedUtc) AS LastSeenUtc,
    COUNT(DISTINCT VectorLoadId) AS DistinctLoadsAffected
FROM dbo.VendorOutboundTransactions
WHERE VendorName = 'FourKites'
  AND CreatedUtc >= DATEADD(HOUR, -24, SYSUTCDATETIME())
  AND Status IN ('HTTP_FAIL', 'TRANSPORT_FAIL', 'REJECTED', 'DEAD_LETTER', 'RATE_LIMITED')
GROUP BY Status, ErrorCategory
ORDER BY FailureCount DESC;
```

Renders as a sorted bar chart or table.

**Reading the result:** the biggest bar is the most pressing problem. If `HTTP_FAIL` dominates, FK is rejecting our payloads (likely malformed). If `TRANSPORT_FAIL` dominates, the network is flaky between us and FK. If `RATE_LIMITED` dominates, we're hammering FK too fast.

### 4.5 Success rate by event type

**File:** `queries\05_success_by_event_type.sql`

Tells you which event types are healthiest vs. weakest.

```sql
SELECT
    EventTypeName,
    COUNT(*) AS Attempts,
    SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1 ELSE 0 END) AS Successes,
    CAST(
        SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1.0 ELSE 0.0 END)
        * 100.0
        / NULLIF(COUNT(*), 0)
        AS DECIMAL(5, 2)
    ) AS SuccessRatePct,
    SUM(CASE WHEN Status = 'CONFIRMED' THEN 1 ELSE 0 END) AS ConfirmedCount,
    SUM(CASE WHEN Status = 'HTTP_FAIL' THEN 1 ELSE 0 END) AS HttpFails,
    SUM(CASE WHEN Status = 'TRANSPORT_FAIL' THEN 1 ELSE 0 END) AS TransportFails,
    SUM(CASE WHEN Status = 'REJECTED' THEN 1 ELSE 0 END) AS Rejected,
    SUM(CASE WHEN Status = 'RATE_LIMITED' THEN 1 ELSE 0 END) AS RateLimited
FROM dbo.VendorOutboundTransactions
WHERE VendorName = 'FourKites'
  AND CreatedUtc >= DATEADD(DAY, -7, SYSUTCDATETIME())
GROUP BY EventTypeName
ORDER BY SuccessRatePct ASC;  -- worst first
```

Render as a table with conditional formatting on the `SuccessRatePct` column using Section 3 colors.

**Reading the result:** If `LocationReportedEvent` (highest volume) has 99% success but `DocumentAvailableEvent` (lowest volume) has 60%, the overall blended rate masks a real problem on POD uploads. This per-type view is how you find that.

### 4.6 Hourly heatmap (last 7 days)

**File:** `queries\06_hourly_heatmap.sql`

Reveals time-of-day patterns: do failures cluster during a specific shift, or correlate with FK's known maintenance windows?

```sql
SELECT
    CAST(CreatedUtc AS DATE) AS Day,
    DATEPART(HOUR, CreatedUtc) AS HourUtc,
    COUNT(*) AS Attempts,
    SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1 ELSE 0 END) AS Successes,
    CAST(
        SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1.0 ELSE 0.0 END)
        * 100.0
        / NULLIF(COUNT(*), 0)
        AS DECIMAL(5, 2)
    ) AS SuccessRatePct
FROM dbo.VendorOutboundTransactions
WHERE VendorName = 'FourKites'
  AND CreatedUtc >= DATEADD(DAY, -7, SYSUTCDATETIME())
GROUP BY CAST(CreatedUtc AS DATE), DATEPART(HOUR, CreatedUtc)
ORDER BY Day DESC, HourUtc;
```

Render as a 24-column × 7-row heatmap with success rate as the color value (green high, red low).

### 4.7 Recent failures (drill-down list)

**File:** `queries\07_recent_failures.sql`

For ops to investigate. Returns up to 100 most recent failed transactions, ready for click-through.

```sql
SELECT TOP 100
    TransactionId,
    CreatedUtc,
    VectorLoadId,
    EventTypeName,
    Status,
    HttpStatusCode,
    ErrorCategory,
    LEFT(COALESCE(ErrorMessage, ''), 200) AS ErrorPreview,
    VendorRequestId,
    SourceSystem
FROM dbo.VendorOutboundTransactions
WHERE VendorName = 'FourKites'
  AND Status IN ('HTTP_FAIL', 'TRANSPORT_FAIL', 'REJECTED', 'DEAD_LETTER')
  AND CreatedUtc >= DATEADD(HOUR, -24, SYSUTCDATETIME())
ORDER BY CreatedUtc DESC;
```

Render as a table. Each row is a failure. The `VectorLoadId` column is a clickable link that calls `usp_GetLoadAuditTrail` and displays the full audit story.

### 4.8 Per-load investigation — the drill-down query

**File:** `queries\08_per_load_investigation.sql`

This is just `usp_GetLoadAuditTrail` from Deliverable #7, surfaced as the drill-down endpoint:

```sql
-- Called from the dashboard when user clicks a load ID
-- Returns 3 result sets: outbound transactions, inbound callbacks, cross-references
EXEC dbo.usp_GetLoadAuditTrail @VectorLoadId = 'LOAD12345';
```

The dashboard displays the three result sets as collapsible sections on a per-load detail page.

### 4.9 The "search by VendorRequestId" query

**File:** `queries\09_lookup_by_requestid.sql`

When FK support says "we don't see request 7f8a9b3c..." this is how you confirm what we sent.

```sql
SELECT
    TransactionId,
    VectorLoadId,
    EventTypeName,
    Status,
    HttpStatusCode,
    CreatedUtc,
    AckUtc,
    RequestPayload,
    ResponseBody
FROM dbo.VendorOutboundTransactions
WHERE VendorName = 'FourKites'
  AND VendorRequestId = '7f8a9b3c-1234-5678-9abc-def012345678';
```

Renders as a one-row detail page in the dashboard.

### 4.10 Inbound webhook health

**File:** `queries\10_webhook_health.sql`

Slightly different metric — tracks the *inbound* side. How fast is our correlator processing webhooks?

```sql
SELECT
    COUNT(*) AS TotalReceived24h,
    SUM(CASE WHEN ProcessedUtc IS NOT NULL THEN 1 ELSE 0 END) AS Processed,
    SUM(CASE WHEN ProcessedUtc IS NULL THEN 1 ELSE 0 END) AS UnprocessedBacklog,
    AVG(DATEDIFF(SECOND, ReceivedUtc, ProcessedUtc)) AS AvgProcessingSeconds,
    MAX(DATEDIFF(SECOND, ReceivedUtc, ProcessedUtc)) AS MaxProcessingSeconds,
    SUM(CASE WHEN CorrelationStatus = 'MATCHED' THEN 1 ELSE 0 END) AS MatchedCount,
    SUM(CASE WHEN CorrelationStatus = 'NO_MATCH' THEN 1 ELSE 0 END) AS NoMatchCount,
    SUM(CASE WHEN ReceiptCount > 1 THEN 1 ELSE 0 END) AS DuplicatesAbsorbed
FROM dbo.VendorInboundCallbacks
WHERE VendorName = 'FourKites'
  AND ReceivedUtc >= DATEADD(HOUR, -24, SYSUTCDATETIME());
```

**What you're looking for:**
- `UnprocessedBacklog > 100` — correlator might be down or slow
- `AvgProcessingSeconds > 30` — correlator is lagging
- `NoMatchCount` high — FK is sending webhooks for loads we didn't dispatch (or we're losing the outbound side of the match)
- `DuplicatesAbsorbed > 0` — confirms dedupe is working (good; expected)

---

## 5. Dashboard layout

A suggested layout for a single-page dashboard. Glen adapts to whichever tool renders it.

```
╔══════════════════════════════════════════════════════════════════════════════╗
║  FourKites Integration Health                            Last update: 14:23 ║
╠══════════════════════════════════════════════════════════════════════════════╣
║                                                                              ║
║   ┌──────────────────┐  ┌──────────────────────────────────────────────┐    ║
║   │                  │  │  Success Rate Trend (last 30 days)           │    ║
║   │     97.4%        │  │                                              │    ║
║   │                  │  │   100 ┤   ___________                         │    ║
║   │  7-day ACK rate  │  │       │  /            \___                    │    ║
║   │                  │  │   95 ─┼─/──────────────────\─── SLA line     │    ║
║   │  ▲ above 95%     │  │       │                     \___              │    ║
║   │                  │  │   90  └──────────────────────────────         │    ║
║   └──────────────────┘  └──────────────────────────────────────────────┘    ║
║                                                                              ║
║   ┌─────────────────────────────┐  ┌─────────────────────────────────────┐  ║
║   │  Today's Volume             │  │  Failures by Category (24h)         │  ║
║   │                             │  │                                      │  ║
║   │  Total: 2,847               │  │  HTTP_FAIL:        ████  18         │  ║
║   │  Loads: 412                 │  │  TRANSPORT_FAIL:   ██    7          │  ║
║   │  Location: 2,200            │  │  REJECTED:         █     3          │  ║
║   │  Status: 478                │  │  RATE_LIMITED:           0          │  ║
║   │  Created: 102               │  │  DEAD_LETTER:            0          │  ║
║   │  Assigned: 38               │  │                                      │  ║
║   │  Docs: 27                   │  │  Total failures: 28 of 1,072 (2.6%) │  ║
║   │  TrackingStopped: 2         │  │                                      │  ║
║   └─────────────────────────────┘  └─────────────────────────────────────┘  ║
║                                                                              ║
║   ┌──────────────────────────────────────────────────────────────────────┐  ║
║   │  Success Rate by Event Type (7-day)                                  │  ║
║   │                                                                       │  ║
║   │  LocationReportedEvent          ████████████████████  99.1% (15,234) │  ║
║   │  LoadStatusEvent                ███████████████████   97.8% (3,201)  │  ║
║   │  LoadAssignedEvent              ██████████████████    96.4%   (267)  │  ║
║   │  LoadCreatedEvent               ████████████████      94.1%   (708)  │  ║
║   │  LoadTrackingStoppedEvent       ████████████████      94.7%    (76)  │  ║
║   │  DocumentAvailableEvent         ██████████████        89.2%   (185)  │  ║
║   └──────────────────────────────────────────────────────────────────────┘  ║
║                                                                              ║
║   ┌──────────────────────────────────────────────────────────────────────┐  ║
║   │  Recent Failures (last 24h)                  Search load: [_______]  │  ║
║   │  ───────────────────────────────────────────────────────────────────│  ║
║   │  14:18  LOAD52341  LocationReported   HTTP_FAIL  400  invalid lat   │  ║
║   │  14:15  LOAD52298  LoadStatus         REJECTED   202  unknown code  │  ║
║   │  14:09  LOAD52201  DocumentAvailable  TRANSPORT  -    timeout       │  ║
║   │  13:55  LOAD52144  LocationReported   HTTP_FAIL  400  invalid lat   │  ║
║   │  ...                                                                 │  ║
║   │                                  [click any row for full audit →]   │  ║
║   └──────────────────────────────────────────────────────────────────────┘  ║
║                                                                              ║
║   ┌──────────────────────────────────────────────────────────────────────┐  ║
║   │  Webhook Health                                                      │  ║
║   │  Received 24h: 1,047    Backlog: 0    Avg proc: 4.2s   No-match: 12 │  ║
║   └──────────────────────────────────────────────────────────────────────┘  ║
║                                                                              ║
╚══════════════════════════════════════════════════════════════════════════════╝
```

### 5.1 Layout principles

- **Top-left: headline metric.** Biggest font, color-coded background. The number anyone glancing should grasp in one second.
- **Top-right: trend.** Confirms whether the headline number is improving or deteriorating.
- **Middle: volume and failures.** Side by side because they're often read together ("how much, and how much went wrong").
- **Below: by-event-type breakdown.** This is where you spot which integration is sick.
- **Below that: drill-down list with search.** Both reactive (recent failures) and proactive (search by load).
- **Bottom: webhook health.** Smaller because inbound is less critical than outbound for the SLA — but visible because a stalled correlator hides real problems.

### 5.2 Drill-down navigation

Clicking a `VectorLoadId` anywhere on the dashboard navigates to a per-load page that calls `usp_GetLoadAuditTrail`. That page displays:

1. **Outbound transactions** (chronological) — what we sent, when, what happened
2. **Inbound callbacks** (chronological) — what FK sent back
3. **Cross-references** — vendor's internal load ID, current tracking status

Most BI tools support click-through with URL parameters. Implementation detail per tool.

---

## 6. Alerting

The dashboard is read on demand. Alerts are pushed.

### 6.1 Alert thresholds

| Trigger | Severity | Channel |
|---|---|---|
| 7-day ACK rate drops below 95% for 1 hour straight | **High** | Email + page on-call |
| 7-day ACK rate drops below 90% (any duration) | **Critical** | Page on-call immediately |
| Same `ErrorCategory` produces > 50 failures in 30 minutes | **High** | Email + Slack |
| Inbound webhook backlog (`UnprocessedBacklog`) > 100 for 10 minutes | **Medium** | Slack |
| Zero outbound transactions logged in last 30 minutes during business hours | **High** | Email + page (means caller is down) |
| `usp_GetSuccessRate` query itself fails (returns error) | **Medium** | Email |

### 6.2 Alert implementation

Two reasonable paths:

**Path 1 — SQL Agent job.** A job runs every 5 minutes, calls `usp_GetSuccessRate` with appropriate windows, raises an alert via `xp_sendmail` or by writing to an alert table that a notification service watches. Simplest if you already use SQL Agent.

**Path 2 — Dashboard tool's native alerting.** Power BI, Grafana, etc. all support alert rules on query results. Configure the rule in the tool, no extra SQL Agent jobs needed. Better if your shop already standardizes on a BI tool.

Glen picks per shop conventions (O-009).

### 6.3 What to do when an alert fires — abbreviated runbook

| Alert | First three things to check |
|---|---|
| ACK rate below 95% | 1) `04_failures_24h.sql` to see failure category breakdown. 2) `05_success_by_event_type.sql` to see if one event type is dragging the rate. 3) FK status page (status.fourkites.com) for known incidents. |
| Below 90% | All of the above, plus immediately escalate to FK CSM if FK side appears down. |
| Repeated same ErrorCategory | `07_recent_failures.sql` and read the `ErrorPreview` column. Same error in 50 transactions usually means one specific kind of bad data — fix at source rather than retry. |
| Webhook backlog | Check OTR API event log for `WebhookCorrelator` errors. May need to restart app pool. |
| Zero transactions during business hours | OTR API down or `VendorDispatch.Enabled = false` got toggled. Check IIS, check Web.config. |

A fuller runbook is its own future deliverable; this gets ops through the common 80%.

---

## 7. SLA reporting — the monthly number

The headline dashboard is real-time. For contract compliance reporting (typically monthly), you need a different cut:

**File:** `queries\11_monthly_sla_report.sql`

```sql
DECLARE @MonthStart DATE = DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1);
DECLARE @NextMonthStart DATE = DATEADD(MONTH, 1, @MonthStart);

SELECT
    'Current Month' AS Period,
    @MonthStart AS PeriodStart,
    @NextMonthStart AS PeriodEnd,
    COUNT(*) AS TotalAttempts,
    SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1 ELSE 0 END) AS Successes,
    CAST(
        SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1.0 ELSE 0.0 END)
        * 100.0
        / NULLIF(COUNT(*), 0)
        AS DECIMAL(5, 2)
    ) AS SuccessRatePct,
    CASE
        WHEN CAST(SUM(CASE WHEN Status IN ('ACK', 'CONFIRMED') THEN 1.0 ELSE 0.0 END)
                  * 100.0 / NULLIF(COUNT(*), 0) AS DECIMAL(5,2)) >= 95.0 THEN 'PASS'
        ELSE 'FAIL'
    END AS SlaStatus
FROM dbo.VendorOutboundTransactions
WHERE VendorName = 'FourKites'
  AND CreatedUtc >= @MonthStart
  AND CreatedUtc < @NextMonthStart;
```

For previous months, change the `@MonthStart` calculation. A history view that returns one row per month is easy to derive.

Save the output of this query at month-end for compliance records. FK may also request the underlying data — `VendorOutboundTransactions` rows for the relevant period satisfy that.

---

## 8. Performance considerations

The queries above were designed against the indexes created in Deliverable #7. Specifically:

| Query | Index used | Expected runtime at 1M rows |
|---|---|---|
| Headline rate | `IX_VOT_Vendor_Created_Status` | < 50ms |
| 30-day trend | `IX_VOT_Vendor_Created_Status` | < 200ms |
| Failures 24h | `IX_VOT_Vendor_Created_Status` | < 100ms |
| Per-event-type | `IX_VOT_Vendor_Created_Status` + index scan | < 500ms |
| Hourly heatmap | `IX_VOT_Vendor_Created_Status` | < 500ms |
| Recent failures | `IX_VOT_Vendor_Created_Status` | < 50ms |
| Per-load drill-down (`usp_GetLoadAuditTrail`) | `IX_VOT_VectorLoad_Recent`, `IX_VIC_VectorLoad` | < 50ms |
| Lookup by VendorRequestId | `IX_VOT_VendorRequestId` | < 20ms |
| Webhook health | clustered scan on `VendorInboundCallbacks` | < 200ms at scale |

These estimates assume Phase 1 volume (~1-3M rows/year). They scale to ~50M before any reindexing decisions are needed. **If the dashboard ever feels slow:** the first thing to check is whether something is querying without a `VendorName` filter, which forces a full scan.

---

## 9. Open items specific to this deliverable

| ID | Item | Resolution needed before |
|---|---|---|
| O-801 | Confirm with FK CSM whether the contractual metric is ACK rate (Definition A) or CONFIRMED rate (Definition B) | Going live with monthly SLA reporting |
| O-802 | Which BI tool will render the dashboard? (Power BI, SSRS, Grafana, custom, etc.) | Implementation |
| O-803 | Which alerting channel does the on-call rotation use? (PagerDuty, OpsGenie, email-only, etc.) | Alert configuration |
| O-804 | Should the dashboard be internet-accessible (for remote ops) or intranet-only? Affects auth choice | Implementation |
| O-805 | Retention: do we keep dashboard query results / snapshots? Or always run live? | Operations |

---

## 10. Done-when checklist

Mark this deliverable complete when:

- [ ] All 11 query files exist in `queries\` and run successfully against production `VendorAPI_FK`
- [ ] Dashboard is rendered in chosen tool (per O-802)
- [ ] Color coding matches Section 3 thresholds
- [ ] Drill-down from any load reference calls `usp_GetLoadAuditTrail` and displays results
- [ ] Search-by-load-id input works
- [ ] At least one alert rule (success-rate threshold) is wired up and tested by manually inserting failure rows
- [ ] Monthly SLA report query produces sensible output for the current partial month
- [ ] Dashboard accessible to ops staff with appropriate permissions
- [ ] Runbook section (6.3) is shared with whoever responds to alerts

---

## 11. What this deliverable proves

After completion:

- The 95% SLA is **observable, not just contractual.** You see in real-time whether you're meeting it.
- Failures have a path to root cause: rate down → category breakdown → event type breakdown → specific failed transactions → full audit story for one load
- Operations can answer "what happened with load X?" without engineering help
- Compliance has a monthly number for contract review without engineering help either
- When vendor #2 ships, the same dashboard works by adding `VendorName = 'Project44'` filter — the queries are already vendor-aware
- Alerts catch problems before they become contract incidents

---

## 12. File index

| File | Purpose |
|---|---|
| `08_Dashboard.md` | This document |
| `queries\01_headline_success_rate.sql` | The big number |
| `queries\02_trend_30day.sql` | Sparkline |
| `queries\03_volume_today.sql` | Volume KPIs |
| `queries\04_failures_24h.sql` | Failure category breakdown |
| `queries\05_success_by_event_type.sql` | Per-type success rate |
| `queries\06_hourly_heatmap.sql` | Time-of-day patterns |
| `queries\07_recent_failures.sql` | Drill-down list |
| `queries\08_per_load_investigation.sql` | Calls usp_GetLoadAuditTrail |
| `queries\09_lookup_by_requestid.sql` | FK support investigations |
| `queries\10_webhook_health.sql` | Inbound side health |
| `queries\11_monthly_sla_report.sql` | Contract compliance report |

---

*End of Dashboard deliverable.*
