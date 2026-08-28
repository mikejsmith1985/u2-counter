# Implementation Plan: Counter Availability Lookup

**Branch**: `001-counter-availability-lookup` | **Date**: 2026-08-28 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-counter-availability-lookup/spec.md`

## Summary

A counter representative on a phone call must say, within about thirty seconds,
whether a part is available, at which branches, what the customer on the line
pays, and what is already committed elsewhere.

The approach is a keyboard-driven web application over a REST API, which reads the
MultiValue ERP through a hardened MCP server rather than through a database driver
— because that server already carries the read-only enforcement, command
allowlist, per-caller identity and audit trail the feature requires, with tests
that prove them. The application's own state lives in SQL Server; the ERP's data
is never written to. Availability, pricing and commitments are always read live,
while the catalogue's searchable projection is held in memory so search answers
instantly: caching *what exists* is safe, caching *how much there is* is the
failure the specification forbids.

## Technical Context

**Language/Version**: C# 13 on .NET 9 (API); TypeScript 5.7 (web); Python 3.12
(demonstration MultiValue store and the MCP server fork)

**Primary Dependencies**: ASP.NET Core 9 Web API with controllers; EF Core 9;
React 19; Vite 6; TanStack Query 5; Radix UI primitives; the hardened `u2-mcp`
fork

**Storage**: SQL Server for application state — sessions, preferences, and the
mirror of the activity record. Azure SQL Database when deployed, SQL Server 2022
in a container locally. ERP data is read-only and never mirrored.

**Testing**: xUnit and Testcontainers (API); Vitest (web units); Cypress with
`cypress-real-events` and `axe-core` (user experience); pytest (store)

**Target Platform**: Linux containers on Azure Container Apps; evergreen desktop
browsers

**Project Type**: Web application — a browser front end, a REST API, and two
supporting services

**Performance Goals**: Search and part open each answer in under one second at the
95th percentile against the full ~3,000-part catalogue (SC-003). A request the ERP
has not answered within five seconds is abandoned and reported as unreachable
(FR-033, SC-013).

**Constraints**: Read-only against the ERP — no mutating call may exist in the
API. Keyboard operation for every action, with no WCAG 2.1 AA violations (FR-037
to FR-040, SC-011, SC-012). Never serve a previously retrieved figure in place of
a live one (FR-035). No credential in any log or audit record (FR-027).

**Scale/Scope**: ~3,000 parts across 12 branches, 150 customers, 800 orders. Five
user stories; four screens and one drawer; ten people using it at once (SC-010).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Article | Requirement | Status | How it is met |
| --- | --- | --- | --- |
| I | Best route, not fastest; production-readiness over speed | ✅ Pass | Real SQL Server rather than SQLite; Testcontainers rather than in-memory providers; a genuine delimiter-format store rather than JSON dressed up on read |
| II | Never kill processes by name pattern | ✅ Pass | `scripts/run-dev-clean.ps1` writes a PID file and stops only those PIDs |
| III | Feature branches, PR to main | ✅ Pass | Work proceeds on `001-counter-availability-lookup` |
| IV | Self-documenting names, no magic numbers, functions under 40 lines, comments explain why | ✅ Pass | Enforced by review, by analyzer settings in `.editorconfig`, and by the ESLint configuration |
| V | Three separated test layers; Cypress with real events; Red → Green → Refactor | ✅ Pass | See research R11. Real events matter more than usual here: synthetic events bypass the focus behaviour that *is* the requirement |
| VI | `CHANGELOG.md` is the single source of truth; no ad-hoc status documents | ✅ Pass | `CHANGELOG.md` at the repository root; the `specs/` tree is exempt as a pipeline artefact |
| VII | Framework-first: build custom only against a documented gap | ⚠️ Justified | One component is custom. See Complexity Tracking |
| VIII | Releases run locally, never through GitHub Actions | ✅ Pass | Deployment is a local script against Azure; no release workflow exists |
| IX | Secrets injected by the vault, never handled in plaintext | ✅ Pass | Connection strings and keys come from the environment or Azure configuration; none in the repository |
| X | "It compiles" is not proof; verify behaviour with evidence | ✅ Pass | Every success criterion has a corresponding automated check; see `quickstart.md` |
| XI | At most one dashboard file; no unrequested Markdown summaries | ✅ Pass | No dashboard; documentation is the `specs/` tree and `CHANGELOG.md` |

**Gate result**: PASS, with one justified deviation recorded below.

**Post-design re-check (after Phase 1)**: PASS. The design added no further
deviation. Article IV was re-examined against the data model, where the temptation
to encode availability rules as bare arithmetic in a controller was strongest;
those rules live in `Counter.Domain` as named operations instead. Article V was
re-examined against the contracts, and each endpoint's failure shape is asserted at
the integration layer rather than assumed.

## Project Structure

### Documentation (this feature)

```text
specs/001-counter-availability-lookup/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── rest-api.md
│   ├── multivalue-files.md
│   └── mcp-usage.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
api/                                  # ASP.NET Core Web API (C#)
├── src/
│   ├── Counter.Api/                  # Controllers, DTOs, filters, composition
│   ├── Counter.Domain/               # Entities, availability and pricing rules
│   └── Counter.Infrastructure/       # MCP client, EF Core, catalogue projection
└── tests/
    ├── Counter.UnitTests/            # Isolated, no I/O
    └── Counter.IntegrationTests/     # Testcontainers SQL Server, live mvstore

web/                                  # React + TypeScript front end
├── src/
│   ├── features/
│   │   ├── search/                   # Story 1 — finding a part
│   │   ├── availability/             # Story 1 — the branch grid
│   │   ├── pricing/                  # Story 2 — customer net price
│   │   ├── commitments/              # Story 3 — what holds the stock
│   │   ├── record/                   # Story 4 — the raw record drawer
│   │   └── governance/               # Story 5 — identity and activity
│   ├── components/                   # Shared primitives, keyboard layer
│   └── api/                          # Generated client, query definitions
├── tests/                            # Vitest unit tests
└── cypress/                          # Real-event user-experience tests

mvstore/                              # Demonstration MultiValue store (Python)
├── src/mvstore/
│   ├── store.py                      # Records in genuine AM/VM/SM format
│   ├── query.py                      # LIST / SELECT / SSELECT / COUNT
│   ├── seed.py                       # Generates the demonstration data set
│   └── driver.py                     # The uopy-compatible surface
└── tests/

deploy/
├── api.Dockerfile
├── mcp.Dockerfile
└── azure/                            # Container Apps and SQL provisioning

scripts/
├── run-dev-clean.ps1                 # PID-file based; never a name pattern
└── seed-demo-data.ps1
```

**Structure Decision**: A web application with a separated front end and API, plus
two supporting services. The front-end and API split is required by the feature's
technology constraints. `mvstore` is separate from both because it stands in for a
database and carries no business logic. The MCP server fork stays in its own
repository and is consumed as a dependency, so that hardening work and application
work do not entangle.

## Phase 1 Design Artefacts

| Artefact | Contents |
| --- | --- |
| [data-model.md](./data-model.md) | Entities, the parallel multivalue layout, availability and pricing rules, order-state transitions |
| [contracts/rest-api.md](./contracts/rest-api.md) | Endpoints, DTOs, error shapes, the unreachable-data contract |
| [contracts/multivalue-files.md](./contracts/multivalue-files.md) | Field-by-field layout of each ERP file as the store holds it |
| [contracts/mcp-usage.md](./contracts/mcp-usage.md) | Which MCP tools the API calls, with what arguments, and what it must never call |
| [quickstart.md](./quickstart.md) | Running it, seeding it, and the checks that prove each success criterion |

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
| --- | --- | --- |
| A custom MultiValue store (`mvstore`) rather than an existing database | Article VII requires a documented gap. There is no freely obtainable Universe instance that can be deployed to a cloud host for evaluation, and licensing prevents redistributing one. Without a stand-in, nothing about this feature can be demonstrated or tested end to end | *Reusing the MCP fork's test mocks*: they hold data only in memory and answer no queries. *Storing the data as JSON and formatting it as MultiValue on read*: the raw-record view of Story 4 would then display a rendering the store never held, which makes that screen dishonest. The store holds genuine delimiter-format records precisely so that screen tells the truth |

Recorded rather than justified: the store is deliberately narrow. It answers the
four read verbs this feature uses and nothing else. It is a test fixture that
happens to be deployable, not an attempt at a database.
