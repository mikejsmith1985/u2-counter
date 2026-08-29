# Changelog

All notable changes to this project are recorded here. This file is the single
source of truth for what changed (Article VI). Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- **Project scaffolding for the counter availability lookup.** A .NET 9 solution
  in `api/` with domain, infrastructure and API projects and two test projects; a
  React and TypeScript workspace in `web/`; a Python package in `mvstore/` for the
  demonstration MultiValue store.

- **`scripts/run-dev-clean.ps1`** — starts every service and records each process
  id with its start time in `.run/pids.json`, stopping only those recorded
  processes (Article II). Start times are compared before stopping, because
  operating systems reuse process ids and an id alone is not identity.

- **Code standards enforced as build failures rather than review notes**:
  `api/.editorconfig` makes the Article IV naming rules errors and treats nullable
  warnings and unpassed cancellation tokens as errors — the latter because a
  timeout that does not carry its token abandons the wait without stopping the
  work. `web/eslint.config.js` applies the same standards to TypeScript.

- **A demonstration MultiValue store (`mvstore/`)** holding records in genuine
  attribute, value and subvalue mark format rather than in JSON with marks
  mentioned in a comment. It presents the objects `uopy` presents — `connect`,
  `File`, `Command`, `List`, `Subroutine`, `UOError` — so the MCP server runs the
  code it would run against a real Universe rather than a second path written for
  demonstrations. Write, delete and subroutine calls raise `UOError`: the server's
  read-only enforcement is tested against a store that would refuse anyway.

- **A seeded data set of 3000 parts across 12 branches, 150 customers and 800
  orders**, generated from a fixed seed so it can be recreated exactly. Fourteen
  conditions the data must contain are named in `SEED_OBLIGATIONS` and checked
  after generation, so an edge case cannot quietly disappear from the data while
  the tests that need it keep passing against nothing.

- **The domain rules**, each with the reasoning at the code: free-to-sell is
  on-hand minus committed floored at zero; stock on order from a supplier is
  reported and never counted as available; the lowest applicable price multiplier
  wins and the terms that did not apply are reported rather than dropped; the
  order-state set is closed and an unrecognised value holds no stock.

- **A read path through the hardened MCP server** rather than through a database
  driver. The server already enforces read-only access, an allowlist of query
  verbs, per-caller identity and an audit trail, with tests proving each; reaching
  past it to a driver would mean rebuilding all of that, worse.

- **The counter screen**: type-ahead search, availability by branch, customer
  pricing with its reasoning, the orders holding committed stock, the stored
  record beside its parsed form, and a governance strip on every screen. Every
  journey is completable from the keyboard, because the person using it has a
  telephone in one hand.

- **A record view showing separators as labelled badges.** The marks are
  invisible characters; rendering them raw shows one unbroken run of text and
  teaches nobody anything, while hiding them decides for the reader that they do
  not need to know how the data is shaped.

- **A durable audit trail** (`UserSession`, `ActivityRecord`) in SQLite via EF
  Core, written for every request including the ones that fail — a failure nobody
  recorded is indistinguishable from a request nobody made. No ERP data is stored;
  what is stored is the record of asking. The application also runs with no
  database at all, which is a supported configuration rather than a degraded one:
  everything works except surviving a restart.

- **`SecretRedactor`**, which removes configured secrets from anything on its way
  into a stored record. The audit trail stores what a person typed, and a person
  can type anything — including, eventually, a password pasted into the wrong
  window.

- **`ErpTimeoutFilter`**, a per-request budget carried as a linked cancellation
  token so it stops the work rather than only the waiting. A timeout that stops
  the wait alone leaves the query running, which under load is how a system dies.

- **`/health` and `/health/live`.** Readiness means the catalogue is built, not
  that the port is open: the application serves requests while it reads the
  catalogue, so a port check would route traffic to an instance whose search
  returns nothing. Liveness answers without consulting anything, because no
  failure of the ERP is improved by restarting the container.

- **A sign-in panel for the demonstration personas**, stating plainly on the
  screen that they are not accounts and that choosing one proves nothing. An
  interface that looked like a login would be claiming a control this
  demonstration does not have.

- **`ReadOnlyGate`**, which renders a disabled control with its reason where the
  writing control would sit. Left absent, the boundary reads as a feature nobody
  got round to; shown, it reads as a decision.

- **An integration suite against real infrastructure** (Article V): the hardened
  MCP server started as a process against a copy of the data, and the audit
  trail on the engine that ships. Nothing is mocked — a mock of the MCP server
  would prove only that the application can talk to a mock.

- **`ErpImmutabilityTests`**, which hashes every ERP file before and after
  exercising every journey. Every other read-only guarantee here describes intent;
  this one describes what happened.

- **`ForbiddenToolTests`**, which fails the build if a forbidden tool name appears
  anywhere in the infrastructure source outside the file that lists them, and if a
  permitted tool is never called.

- **A Cypress suite using `cypress-real-events`** throughout. Synthetic events do
  not move focus, so a suite built on them would report that keyboard navigation
  works without ever having exercised it — and keyboard operation is the
  requirement. `axe-core` sweeps every screen for serious and critical violations.

- **Deployment as local scripts** (Article VIII): `deploy/api.Dockerfile`,
  `deploy/mcp.Dockerfile`, `deploy/azure/provision.ps1` and
  `deploy/azure/deploy.ps1`. The MCP server is deployed with internal ingress only
  and one replica — it holds the database session, and a second replica would
  reintroduce by deployment the connection multiplication the fork was hardened
  against.

### Fixed

Each of these was found by a test written before the defect was known to exist.

- **Committed stock was generated independently of the orders holding it.** A
  branch could show nothing committed while three orders held thirty-nine units of
  it, which left the commitments screen — whose entire purpose is to explain the
  committed figure — unable to explain a figure that was never derived from
  anything. Orders are now written first and committed quantities come from them,
  with the shortfall placed deliberately on one position so the unaccounted row
  still has something real to show.

- **Signing in did not survive the next request.** The session key was minted
  fresh each time it was asked for, so signing in issued one cookie and the filter
  that records the request issued another; the browser kept the second, and the
  person was signed in under a key they no longer held.

- **An ERP refusing connections arrived as a server error rather than as
  unreachable**, collapsing the one distinction this application is built to keep.
  A refused or dropped connection now produces the same typed 504 a timeout does,
  and the shared connection is discarded so the next request opens a new one.

- **Search matched the raw string typed**, so a term with a space either side
  found nothing and "breaker gfci" failed where "gfci breaker" worked. Matching is
  now on words, in any order, with every word required.

- **The customer selector was mouse-only** while the part search was not, making
  the customer step the one place a representative had to reach for the mouse — in
  the middle of a call. It now has the same arrow-key and Enter handling.

- **`--text-faint` failed AA contrast in both themes** (3.2:1 light, 3.6:1 dark).
  The text it was used for is the keyboard hints, which is to say the part of the
  interface that tells a keyboard user how to drive it.

- **A customer field named `city` held a street address.** The `CUSTOMER` file has
  address lines and no city, so the field is now `addressLine`, and the selector
  shows it — which is what tells two customers of the same name apart.

- **One MCP tool was permitted that nothing calls.** Removed rather than left
  standing: a permission nobody uses is one nobody questions when it starts to
  matter.

- **`run-dev-clean.ps1` recorded the launcher rather than the service.**
  `dotnet run` and `npm.cmd` each start a child and exit, so stopping the recorded
  id left the actual service holding its port. The stop path now ends the recorded
  process tree and, separately, whatever still holds the port this script
  assigned — still one specific id at a time, never a name pattern (Article II).
  A partial failure to start is recorded as it happens, so services that did start
  remain stoppable.
