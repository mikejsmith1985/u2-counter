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
# One command starts the MCP server, the API and the front end, recording each
# process id, its start time and the port it was given in .run/pids.json.
./scripts/run-dev-clean.ps1

# Without the SQL Server container -- everything works except surviving a
# restart, which is a supported way to run the demonstration.
./scripts/run-dev-clean.ps1 -SkipSql
```

The store is not a service. It is a driver the MCP server loads in process,
presenting the same objects `uopy` presents, so the server runs the code it would
run against a real Universe rather than a second path written for demonstrations.

Stopping is the same script:

```powershell
./scripts/run-dev-clean.ps1 -Stop
```

It stops the recorded process **and its tree**, then whatever still holds the
port it was given — never by matching a process name (Article II). Both steps are
needed: `dotnet run` and `npm.cmd` each start a child and exit, so the recorded id
is often already gone while the service it started is still listening.

| Service | Address |
| --- | --- |
| Front end | `http://127.0.0.1:5173` |
| API | `http://127.0.0.1:5080` |
| MCP server | `http://127.0.0.1:5081` (loopback only) |
| SQL Server | `localhost:1433` (container, optional) |

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
# The .NET suites start their own MCP server and SQL Server, so stop the
# development services first or the two sets compete for ports.
./scripts/run-dev-clean.ps1 -Stop
dotnet test api/Counter.sln                    # 83 unit, 59 integration

cd mvstore; .\.venv\Scripts\python.exe -m pytest tests -q; cd ..   # 109 store
npm --prefix web test                          # 17 Vitest units

# Cypress drives the running application, so the services go back up first.
./scripts/run-dev-clean.ps1 -SkipSql
npm --prefix web run cypress                   # 61 browser tests
```

The integration suite needs Docker (SQL Server) and the hardened fork checked out
beside this repository, or `U2_MCP_ROOT` pointing at it. It starts the fork's own
installed entry point against a **copy** of the demonstration data, which is what
lets `ErpImmutabilityTests` hash every file before and after and prove read-only
by outcome rather than by assertion.

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
| **SC-013** — unreachable within the budget | `TimeoutBudgetTests` makes the store slow *after* the application is healthy, then asserts a typed `504` inside the budget and that the application recovers on its own. Slowing it from startup measured an application still loading its catalogue — a state no user is ever in |
| **SC-014** — commitments reconcile | `CommitmentReconciliationTests` checks, for every branch of every part, that listed commitments plus `unaccounted` equal the committed total |

Real events in the Cypress suite are not a stylistic preference. Synthetic events
bypass the browser's focus behaviour, which is the substance of SC-011 — a
keyboard test built on synthetic events would pass against an interface no one
could operate by keyboard.

### Proving the read-only claim

```powershell
dotnet test api/Counter.sln --filter "FullyQualifiedName~ReadOnlyRouteTests|FullyQualifiedName~ForbiddenToolTests|FullyQualifiedName~ErpImmutabilityTests"
```

Three assertions, each closing a different route to a write:

1. No route is registered for `POST`, `PUT`, `PATCH` or `DELETE` against ERP data.
2. No forbidden tool name from [contracts/mcp-usage.md](./contracts/mcp-usage.md)
   appears anywhere in the infrastructure source outside the one file that lists
   them — and every permitted tool is one the reader actually calls, so a
   permission nobody uses fails the build rather than sitting unquestioned.
3. Every ERP record is byte-identical before and after the full suite runs.

The third is the one that matters. The first two assert intent; only the third
asserts outcome.

## Deploying

```powershell
# Once. Writes the names it created to deploy/azure/environment.json, which
# deploy.ps1 reads rather than deriving them again.
./deploy/azure/provision.ps1 -ResourceGroup counter-demo -SqlAdminUser counteradmin

# Each release. Runs every suite first and refuses to deploy if any fails.
./deploy/azure/deploy.ps1
```

Local scripts against Azure, never a hosted pipeline (Article VIII) — so what
deploys is what was on the machine that deployed it, and the gate is the same
suite the author was running a minute earlier.

The API receives public ingress; the MCP server receives internal ingress only
and exactly one replica, because it holds the database session and a second
replica would reintroduce by deployment the connection multiplication the fork
was hardened against. Secrets are Key Vault references resolved by the container
app's own identity, and never pass through the script (Article IX).

## What the run recorded

Every suite, run against the seeded data set on 28 August 2026. The figures are
what the run reported, not what it was expected to report.

| Suite | Result | What it covers |
| --- | --- | --- |
| `Counter.UnitTests` | **83 passed** | The domain rules, the parser, search matching, mark descriptions |
| `Counter.IntegrationTests` | **59 passed** | The whole application against the hardened MCP server and SQL Server, nothing mocked |
| `mvstore` | **109 passed** | The store, its query verbs, the seeder's obligations, the delay switch |
| Vitest | **17 passed** | The copied summary and the record rendering |
| Cypress | **61 passed** | Every journey in a real browser with real events, `axe-core` on every screen |
| `u2-mcp` (the fork) | **464 passed, 4 skipped** | The hardened server this reads through, run from its own repository |

**793 tests in total**, all passing.

Two figures from inside those runs are worth quoting, because they are the two a
reviewer would otherwise have to take on trust:

- **Search, 95th percentile: under one second** across 200 searches against the
  full 3,000-part catalogue (SC-003), with the terms a person actually types —
  growing prefixes, because type-ahead fires on every keystroke and the query
  that has to be fast is the two-character one matching half the catalogue.
- **Every ERP file byte-identical** before and after the suite exercised every
  screen (SC-013's neighbour, and the read-only claim's only real proof).

### Nine defects the suites found

Listed because a suite that never fails is a suite nobody should believe. Each of
these was found by a test written before the defect was known to exist, and each
is recorded in [CHANGELOG.md](../../CHANGELOG.md) with what it would have done.

| Found by | Defect |
| --- | --- |
| `CommitmentReconciliationTests` | Committed stock was generated independently of the orders holding it, so a branch could show nothing committed while three orders held thirty-nine units |
| `ConcurrentCallerTests` | Signing in did not survive the next request: two cookies were issued and the browser kept the wrong one |
| `TimeoutBudgetTests` | A request budget that stopped the waiting but not the query — and then, once fixed, a filter that truncated every slow response to an empty `200` |
| `FailureShapeTests` | A refused ERP connection arrived as a server error rather than as unreachable |
| `PartSearchTests` | Search matched the raw string typed, so a term with a space either side found nothing |
| `keyboard-journeys.cy.ts` | The `R` shortcut printed in the header did nothing, because selecting a part left focus in the search box |
| `keyboard-journeys.cy.ts` | Closing a drawer left nothing focused, stranding a keyboard user at the top of the document |
| `axe-core`, on the empty screen | The faint text token failed AA in both themes — the text it was used for is the keyboard hints |
| `axe-core`, on the answered screen | A branch with no stock was dimmed to 3.4:1, and those are the rows a representative reads to confirm there is none |

## Troubleshooting

| Symptom | Cause | Remedy |
| --- | --- | --- |
| Search empty, part known present | Catalogue not built yet | `GET /health` says so plainly. The next search rebuilds it; a restart is not needed |
| Everything returns `504` | The MCP server is not running, or is refusing | `GET /health` names the endpoint the API believes it is using |
| `dotnet: No .NET SDKs were found` | The `dotnet` first on PATH is the shared host, not the SDK | `run-dev-clean.ps1` finds one with an SDK; run through the script, or put an SDK earlier on PATH |
| Port already in use after a failed start | A service outlived the launcher that started it | `./scripts/run-dev-clean.ps1 -Stop` clears the recorded tree and the port holder |
| MCP server exits at startup | It refuses an unauthenticated public bind | Confirm `U2_HTTP_HOST=127.0.0.1` locally |
| Integration tests cannot start | Docker not running | Start Docker; Testcontainers needs it |
| Raw record shows no marks | Delimiters stripped in transit | The response must carry them as stored; check the serializer, not the client |
