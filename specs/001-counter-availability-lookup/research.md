# Phase 0 Research: Counter Availability Lookup

**Date**: 2026-08-28
**Spec**: [spec.md](./spec.md)

Every unknown in the plan's Technical Context is resolved below. Each decision
records what was chosen, why, and what was rejected.

---

## R1. How the application reaches MultiValue data

**Decision**: The application reads the ERP through an MCP server
(`u2-mcp`, hardened fork), not through a direct database driver. The .NET API is
an MCP client.

**Rationale**: The MCP server already carries the safety controls this feature
depends on — a read-verb allowlist, a blocklist covering shell and account
escapes, read-only enforcement, per-caller identity, and an audit trail that names
the person and the database login. Reaching past it to a driver would mean
rebuilding all of that inside the API, and rebuilding it worse. FR-021 to FR-028
are satisfied by a component that already has tests proving them.

**Alternatives considered**:

- *A direct MultiValue driver from .NET.* Rocket ships a .NET toolkit for U2. It
  would remove a hop, and it would also remove every control listed above, none of
  which the toolkit provides. Rejected.
- *A thin HTTP shim of our own over the driver.* This is the MCP server, minus its
  tests. Rejected as duplication.

---

## R2. Running without a Universe server

**Decision**: Build `mvstore` — a demonstration MultiValue store that holds
records in genuine attribute/value/subvalue format and answers the subset of
RetrieVe the feature uses (`LIST`, `SELECT`, `SSELECT`, `COUNT`). The MCP fork
gains a documented driver seam (`U2_DRIVER=uopy|demo`) to sit on top of it.

**Rationale**: Article VII requires confirming no existing component provides a
capability before building it. There is no freely obtainable Universe instance
that can be deployed to a cloud host for evaluation, and licensing prevents
redistributing one. That is the documented gap.

Holding the records in the real delimiter format rather than as JSON is what makes
User Story 4 truthful: the raw record shown to the user is genuinely the stored
form, not a rendering invented for the screen. A store that faked it would make
that screen a lie.

The driver seam is small, is worth having upstream on its own merits — it lets
anyone evaluate the server without a database — and keeps the demonstration
concern out of the hardened code paths.

**Alternatives considered**:

- *Reuse the test mocks from the fork.* They exist to make assertions, hold data
  only in memory, and answer no queries. Rejected.
- *Store demo data as JSON and format it as MultiValue on read.* The raw-record
  view would then display something the store never held. Rejected: it defeats the
  point of the screen.
- *Point at a real Universe instance.* Cannot be deployed for a reviewer to click.
  The seam leaves this available for anyone who does have one.

---

## R3. Web API shape

**Decision**: ASP.NET Core 9 Web API using controllers, one controller per
resource, returning DTOs distinct from domain types.

**Rationale**: Controllers rather than minimal APIs because the routing,
filters and model binding are the conventional shape a maintainer of an
ASP.NET MVC codebase will recognise, and because filters give a clean place to
hang the correlation and audit concerns. DTOs separate from domain types keep the
wire contract stable while the domain moves.

**Alternatives considered**:

- *Minimal APIs.* Less ceremony, fewer conventions a newcomer can rely on, and no
  natural home for cross-cutting filters. Rejected for a codebase meant to look
  familiar.
- *GraphQL.* One screen, one shape of question. Rejected as unwarranted.

---

## R4. Where the application's own data lives

**Decision**: SQL Server — Azure SQL Database when deployed, SQL Server in a
container locally — accessed through EF Core 9.

**Rationale**: The ERP data is not ours to write to, and the read-only rule means
we never will. But the application still has state of its own: sign-in sessions,
the selected customer, recently viewed parts, and a mirror of the activity record
that FR-026 requires users to browse. That state is relational and belongs in a
relational store.

This is also the honest architecture rather than a contrivance: distributors
routinely run modern applications against SQL Server while the ERP stays on
MultiValue, and the split is precisely where the interesting engineering lives.

**Alternatives considered**:

- *SQLite.* Simpler to run, and wrong: it is not what this class of application
  runs against, and it would not exercise EF Core's SQL Server provider.
- *No relational store; keep state in memory.* Loses the activity mirror on
  restart, which is the same defect the MCP fork was hardened against. Rejected.

---

## R5. Front-end stack

**Decision**: React 19 with TypeScript 5.7, built by Vite 6, with TanStack Query
for server state.

**Rationale**: TypeScript is named in the requirements this feature must satisfy.
TanStack Query is chosen specifically because FR-033 to FR-036 demand honest
failure behaviour: it distinguishes loading, error and empty states as first-class
concerns, which is exactly the distinction the spec forbids blurring. Hand-rolled
fetch state is where "no stock" and "could not reach the data" get conflated.

**Alternatives considered**:

- *Server-rendered Razor views.* Fewer moving parts, and no TypeScript, which the
  requirements call for. Rejected.
- *Redux or similar.* This application has almost no client state — the selected
  customer and the current part. A store would be scaffolding around nothing.

---

## R6. Keyboard-first interaction and accessibility

**Decision**: Build on Radix UI primitives, which ship correct focus management
and ARIA semantics, and add an application-level keyboard layer for the shortcuts
the counter needs (`/` to search, `Escape` to clear, `Enter` to open, `R` for the
record view). Verify with `axe-core` in the Cypress suite.

**Rationale**: The clarification made keyboard operation a requirement rather than
a nicety, and the parts of it that are hard — focus trapping in the record drawer,
roving focus in the branch grid, announcing changed results to a screen reader —
are exactly what Radix has already solved and tested. Article VII points the same
way.

`axe-core` turns SC-012 from a claim into a check that fails the build.

**Alternatives considered**:

- *Hand-built components.* Focus management is subtle, and getting it wrong is
  invisible until someone who relies on it cannot use the application. Rejected.
- *A full component library such as MUI.* Brings a design language this interface
  does not want and a great deal that will never be used. Rejected.

---

## R7. Failing fast when the ERP does not answer

**Decision**: A five-second budget on every ERP-backed request, enforced in the
API with a linked cancellation token, surfaced as HTTP 504 carrying a typed
problem detail. The client renders it as a distinct "could not reach the data"
state with a retry, never as an empty result.

**Rationale**: FR-033 gives the user a few seconds; five is the outer edge of what
someone on a phone call will tolerate and comfortably inside the one-second target
for the healthy path. The cancellation token means an abandoned request stops the
work rather than merely ignoring it — the same defect that was fixed in the MCP
fork's query timeout, and the same fix.

A distinct status code matters: a reachability failure that arrives as an empty
200 is indistinguishable from "no stock", which FR-029 forbids.

**Alternatives considered**:

- *Retry automatically before failing.* Multiplies the wait the user experiences.
  The user retries if they want to; the machine should not decide that for them.
- *Serve the last known figures.* Ruled out by the clarification and by FR-035.

---

## R8. Search that answers in under a second

**Decision**: Load the catalogue's searchable projection — part number,
description, manufacturer — into memory in the API at startup, and search it
there. Availability, pricing and commitments are always read live from the ERP.

**Rationale**: SC-003 requires a sub-second answer over ~3,000 parts. The
catalogue changes rarely and is small; stock changes constantly and is the thing a
stale answer would misreport. Splitting them means search is instant while every
number the user acts on is live.

This is a deliberate line: caching *what exists* is safe, caching *how much there
is* is the failure FR-035 forbids.

**Alternatives considered**:

- *Query the ERP on every keystroke.* Slow, and it hammers a system this
  application does not own.
- *Mirror the catalogue into SQL Server and search there.* Reasonable, and a
  round trip we do not need at this size. The in-memory projection is rebuilt at
  startup and refreshed on demand.

---

## R9. Identity for the demonstration

**Decision**: Cookie-based sign-in against a small set of demonstration personas
with different branches and permissions. The sign-in screen states plainly that
these are demonstration accounts.

**Rationale**: The spec assumes users arrive already identified and does not make
establishing identity part of this feature. What the feature needs is a real
identity flowing through to the audit trail, which personas provide. Standing up
an identity provider would add a dependency, a secret, and a failure mode for no
gain.

The MCP fork already bridges to Duo, Auth0 or any OIDC provider, so the path to
real single sign-on exists and is documented; it is simply not exercised here.

**Alternatives considered**:

- *No sign-in at all.* Then FR-021 to FR-026 cannot be demonstrated, and the
  governance strip has nothing to show. Rejected.
- *A real identity provider.* Adds an external dependency to a demonstration and
  proves nothing this feature is about.

---

## R10. Deployment

**Decision**: Azure Container Apps — one app for the API (serving the built
front-end assets), one internal-only app for the MCP server and its demonstration
store — with Azure SQL Database for application state. Both images built from
Dockerfiles in the repository.

**Rationale**: Two containers with one internal is the smallest arrangement that
keeps the MCP server unreachable from the internet, which matters because the fork
refuses to serve unauthenticated traffic on a public interface. Container Apps
scales to zero, so an idle demonstration costs nothing.

**Alternatives considered**:

- *App Service.* Comfortable for the API alone, awkward for a second private
  service.
- *A single container running everything.* Collapses the boundary the architecture
  is meant to demonstrate, and puts the MCP server on a public port.
- *Kubernetes.* Enormously more machinery than two containers justify.

---

## R11. Testing approach

**Decision**: Three separated layers, per Article V.

| Layer | Tool | Scope |
| --- | --- | --- |
| Unit | xUnit; Vitest; pytest | Fully isolated, no I/O |
| Integration | xUnit with Testcontainers (SQL Server); pytest against a live `mvstore` | Real infrastructure, never a mocked driver |
| User experience | Cypress with `cypress-real-events`, plus `axe-core` | Real browser, real events |

**Rationale**: Article V requires exactly this separation and names Cypress with
real events. `cypress-real-events` matters more than usual here: a keyboard-first
interface tested with synthetic events proves nothing, because synthetic events
bypass the focus behaviour that is the requirement.

Testcontainers, likewise, is the difference between testing EF Core against SQL
Server and testing it against a mock that agrees with us.

**Alternatives considered**:

- *Playwright.* Capable, and not what the constitution names. Rejected without
  further argument.
- *In-memory EF provider for integration tests.* Explicitly the mocked driver
  Article V forbids.

---

## R12. Order state and its effect on availability

**Decision**: Model order state as a closed set — `QUOTE`, `CONFIRMED`,
`ALLOCATED`, `PICKING`, `SHIPPED`, `CANCELLED` — and compute committed quantity
from the middle three only.

**Rationale**: The clarification fixed which states hold stock. Making the set
closed rather than free text means an unrecognised state cannot silently be
counted as holding stock, and the seed data cannot drift from the rule.

The demonstration data deliberately includes quotations and shipped orders against
parts that also have live commitments, so that a reviewer checking FR-018 sees
states that are correctly *excluded* rather than a data set where the rule never
bites.

**Alternatives considered**:

- *A boolean "holds stock" flag on each order.* Moves the rule into the data,
  where it can be seeded wrong. Rejected.
