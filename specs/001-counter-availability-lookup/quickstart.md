# Quickstart: Counter Availability Lookup

Running the feature locally, and the checks that prove it works. Article X applies
throughout: compiling is not evidence, and neither is a screenshot.

## Prerequisites

| Tool | Version | Used for |
| --- | --- | --- |
| .NET SDK | 9.0 | API |
| Node.js | 22 or later | Front end |
| Python | 3.12 | Store and MCP server |
| Docker | Any current release | SQL Server, and Testcontainers |
| PowerShell | 7 | The run and seed scripts |

## Running it

```powershell
# One command starts the store, the MCP server, the API and the front end,
# recording every process id in .run/pids.json.
./scripts/run-dev-clean.ps1
```

The script stops anything from a previous run **by process id from that file**,
never by matching a process name (Article II). Stopping is the same script:

```powershell
./scripts/run-dev-clean.ps1 -Stop
```

| Service | Address |
| --- | --- |
| Front end | `http://127.0.0.1:5173` |
| API | `http://127.0.0.1:5080` |
| MCP server | `http://127.0.0.1:5081` (loopback only) |
| SQL Server | `localhost:1433` (container) |

Loopback binding is not incidental: the MCP fork refuses to serve unauthenticated
traffic on a reachable interface, and this development configuration has no
identity provider.

## Seeding

```powershell
./scripts/seed-demo-data.ps1
```

Generates roughly 3,000 parts across 12 branches, 150 customers and 800 orders,
and verifies every condition listed in the seed obligations table of
[contracts/multivalue-files.md](./contracts/multivalue-files.md). The script fails
if any required condition is absent, so a data set that cannot exercise a rule
never reaches a test run.

## Sign in

Demonstration personas, stated as such on the sign-in screen:

| Persona | Home branch | Purpose |
| --- | --- | --- |
| Dana Whitfield | Denver | Everyday counter use |
| Marcus Oyelaran | Boulder | A branch that stocks little |
| Priya Raghavan | Denver | Read-only, for the governance strip |

---

## Verifying it

Each success criterion has a check. None of them is "look at it".

### The whole suite

```powershell
./scripts/run-dev-clean.ps1 -Stop     # a clean field first
dotnet test api/Counter.sln           # unit and integration
npm --prefix web test                 # Vitest units
npm --prefix web run cypress:run      # real-event user experience
python -m pytest mvstore/tests        # store
```

### Criterion by criterion

| Criterion | How it is proven |
| --- | --- |
| **SC-001** — answer in 30 seconds | Cypress `counter-call.cy.ts` walks part number to net price and asserts the elapsed time |
| **SC-002** — no scrolling | Cypress asserts the branch grid is fully within the viewport at 1920×1080 |
| **SC-003** — sub-second, 95th percentile | `Counter.IntegrationTests/SearchPerformanceTests` runs 200 searches against the full catalogue and asserts the 95th percentile |
| **SC-004** — usable untaught | Not automatable. Recorded as an observed walkthrough with someone who has not seen it |
| **SC-005** — figures match the record | `AvailabilityReconciliationTests` reads every inventory record and compares each parsed branch position against the raw record, across the whole data set |
| **SC-006** — every request traceable | `ActivityAttributionTests` issues requests as each persona and asserts one record per request naming the person and the database login |
| **SC-007** — no credential recorded | `ActivityRedactionTests` scans every written record for the seeded passwords |
| **SC-008** — record maps to display | Cypress opens the record drawer and asserts each branch appears in the raw record at the position it occupies on screen |
| **SC-009** — every failure explains itself | `FailureShapeTests` provokes each failure and asserts a typed problem with a readable detail; Cypress asserts each renders as its own state |
| **SC-010** — ten at once | `ConcurrentCallerTests` runs ten simultaneous sessions and compares each answer against the single-caller answer |
| **SC-011** — keyboard end to end | Cypress with `cypress-real-events` completes all five journeys using only keyboard events, asserting a visible focus ring at every step |
| **SC-012** — no accessibility violations | `axe-core` runs against every screen in the Cypress suite and fails on any WCAG 2.1 AA violation |
| **SC-013** — unreachable within five seconds | `mvstore` is put into a delaying mode; the API is asserted to return `504` within five seconds, and Cypress asserts the unreachable state with a retry — never an empty result |
| **SC-014** — commitments reconcile | `CommitmentReconciliationTests` checks, for every branch of every part, that listed commitments plus `unaccounted` equal the committed total |

Real events in the Cypress suite are not a stylistic preference. Synthetic events
bypass the browser's focus behaviour, which is the substance of SC-011 — a
keyboard test built on synthetic events would pass against an interface no one
could operate by keyboard.

### Proving the read-only claim

```powershell
dotnet test api/Counter.sln --filter Category=ReadOnly
```

Three assertions, each closing a different route to a write:

1. No route is registered for `POST`, `PUT`, `PATCH` or `DELETE` against ERP data.
2. The MCP client exposes no binding to any tool in the forbidden table of
   [contracts/mcp-usage.md](./contracts/mcp-usage.md).
3. Every ERP record is byte-identical before and after the full suite runs.

The third is the one that matters. The first two assert intent; only the third
asserts outcome.

## Deploying

```powershell
./deploy/azure/provision.ps1 -Environment demo    # once
./deploy/azure/deploy.ps1 -Environment demo       # each release
```

Local scripts against Azure, never a hosted pipeline (Article VIII). The API
receives public ingress; the MCP server receives internal ingress only; secrets
come from Azure configuration and never from the repository (Article IX).

## Troubleshooting

| Symptom | Cause | Remedy |
| --- | --- | --- |
| Search empty, part known present | Catalogue projection not built | `POST /api/v1/admin/refresh-catalogue`, or restart the API |
| Everything returns `504` | Store not running | Check `.run/pids.json`; re-run the script |
| MCP server exits at startup | It refuses an unauthenticated public bind | Confirm `U2_HTTP_HOST=127.0.0.1` locally |
| Integration tests cannot start | Docker not running | Start Docker; Testcontainers needs it |
| Raw record shows no marks | Delimiters stripped in transit | The response must carry them as stored; check the serializer, not the client |
