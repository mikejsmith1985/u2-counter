# Tasks: Counter Availability Lookup

**Input**: Design documents from `/specs/001-counter-availability-lookup/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)

**Tests**: Included. Article V of the constitution requires Red → Green → Refactor, so the failing test is a task in its own right and precedes its implementation.

**Organization**: Grouped by user story so each can be implemented, tested and delivered independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel — different files, no dependency on unfinished work
- **[Story]**: Which user story the task serves (US1–US5)

## Path Conventions

Per the structure in plan.md: `api/src/`, `api/tests/`, `web/src/`, `web/cypress/`, `mvstore/src/`, `deploy/`, `scripts/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: An empty repository becomes one where every tool runs.

- [X] T001 Create the directory structure from plan.md — `api/`, `web/`, `mvstore/`, `deploy/`, `scripts/`
- [X] T002 [P] Create the .NET solution and three projects in `api/` — `Counter.Api`, `Counter.Domain`, `Counter.Infrastructure` — targeting .NET 9
- [X] T003 [P] Initialise the Vite + React 19 + TypeScript 5.7 workspace in `web/`
- [X] T004 [P] Initialise the Python package in `mvstore/` with `pyproject.toml`, targeting 3.12
- [X] T005 [P] Add `api/.editorconfig` enforcing the Article IV naming and function-length rules as analyzer severities
- [X] T006 [P] Configure ESLint and Prettier in `web/` with the same naming rules and an import-order convention
- [X] T007 [P] Configure ruff and mypy for `mvstore/` in `mvstore/pyproject.toml`, matching the fork's settings
- [X] T008 Write `scripts/run-dev-clean.ps1` — starts all four services, records every process id in `.run/pids.json`, and on `-Stop` terminates **only those ids** (Article II)
- [X] T009 [P] Write `CHANGELOG.md` at the repository root with an `Unreleased` section (Article VI)
- [X] T010 [P] Write `.gitignore` covering `.run/`, build output, `node_modules/`, `.venv/`, and any `.env`

**Checkpoint**: Every toolchain runs; nothing does anything yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The data, the pipe to it, and the cross-cutting concerns every story needs.

**⚠️ CRITICAL**: No user story can begin until this phase completes.

### The demonstration MultiValue store

- [X] T011 [P] Write failing tests for delimiter round-tripping in `mvstore/tests/test_store.py` — records with empty interior fields, missing trailing fields, and subvalues survive write-then-read unchanged
- [X] T012 Implement `mvstore/src/mvstore/store.py` — file-backed records held in genuine AM/VM/SM format, keyed reads and writes
- [X] T013 [P] Write failing tests for the query subset in `mvstore/tests/test_query.py` — `LIST`, `SELECT`, `SSELECT`, `COUNT`, including a `WITH` clause over a multivalued field
- [X] T014 Implement `mvstore/src/mvstore/query.py` — the four read verbs and nothing else
- [X] T015 [P] Write failing tests for record validation in `mvstore/tests/test_validation.py` — a parallel field longer than field 1 is rejected; a short field is padded, never truncating the others
- [X] T016 Implement validation in `mvstore/src/mvstore/store.py` per the rules in `contracts/multivalue-files.md`
- [X] T017 Implement `mvstore/src/mvstore/driver.py` — the uopy-compatible surface the MCP fork's driver seam expects

### Seed data

- [X] T018 [P] Write failing tests in `mvstore/tests/test_seed.py` asserting every condition in the seed obligations table of `contracts/multivalue-files.md`
- [X] T019 Implement `mvstore/src/mvstore/seed.py` — ~3,000 parts, 12 branches, 150 customers, 800 orders, satisfying every obligation
- [X] T020 Write `scripts/seed-demo-data.ps1`, which fails loudly if any seed obligation is unmet

### The MCP fork's driver seam

- [X] T021 Add `U2_DRIVER=uopy|demo` to the hardened `u2-mcp` fork, defaulting to `uopy`, with tests proving the default is unchanged
- [X] T022 Wire the `demo` driver to `mvstore` in the fork, and document the seam in the fork's `docs/hardening.md`

### API foundations

- [ ] T023 [P] Write failing integration tests in `api/tests/Counter.IntegrationTests/McpClientTests.cs` — the client reaches a live `mvstore` and returns a parsed record
- [ ] T024 Implement the MCP client in `api/src/Counter.Infrastructure/Mcp/McpClient.cs`, calling only the tools permitted by `contracts/mcp-usage.md`
- [ ] T025 Write `api/tests/Counter.IntegrationTests/ForbiddenToolTests.cs` asserting no binding exists to any tool in the forbidden table — the list must fail a build, not sit in a document
- [ ] T026 [P] Write failing tests in `api/tests/Counter.UnitTests/MultiValueParserTests.cs` — parallel fields align by position; a short field pads; a record with a quantity for an unnamed branch is rejected
- [ ] T027 Implement `api/src/Counter.Infrastructure/MultiValue/RecordParser.cs` producing one `BranchPosition` per index, never zipping separate lists
- [ ] T028 [P] Implement the EF Core context and migrations for `UserSession` and `ActivityRecord` in `api/src/Counter.Infrastructure/Data/`
- [ ] T029 [P] Write failing tests in `api/tests/Counter.IntegrationTests/ActivityRedactionTests.cs` asserting no seeded credential ever reaches a written record
- [ ] T030 Implement the activity-recording action filter in `api/src/Counter.Api/Filters/ActivityRecordingFilter.cs` — one record per request including failures, naming the person and the database login
- [ ] T031 Implement the five-second request budget in `api/src/Counter.Api/Filters/ErpTimeoutFilter.cs` — a linked cancellation token that stops the work, not merely the waiting
- [ ] T032 Implement problem-detail mapping in `api/src/Counter.Api/Filters/ProblemDetailsMapper.cs` for the four error types in `contracts/rest-api.md`

### Web foundations

- [ ] T033 [P] Implement the application shell, routing and TanStack Query provider in `web/src/app/`
- [ ] T034 [P] Implement the keyboard layer in `web/src/components/keyboard/` — `/` focus search, `Escape` clear, `Enter` open, `R` record drawer
- [ ] T035 [P] Implement the shared response envelope types and the typed API client in `web/src/api/`
- [ ] T036 Configure Cypress with `cypress-real-events` and `axe-core` in `web/cypress/`, with a smoke test that the shell renders

**Checkpoint**: Data exists, the API can read it, every request is timed and recorded. User stories can now proceed.

---

## Phase 4: User Story 1 — Answer "have you got it?" (Priority: P1) 🎯 MVP

**Goal**: A representative types a part number and sees, on one screen without scrolling, what is free to sell at every branch.

**Independent test**: Given a part number and a branch, the representative can state the free-to-sell quantity there and name the nearest branch that could fill the order, using only this screen.

### Tests first

- [ ] T037 [P] [US1] Write failing tests in `api/tests/Counter.UnitTests/AvailabilityRulesTests.cs` — free-to-sell is on-hand less committed, clamped at zero; on-order never counts toward it
- [ ] T038 [P] [US1] Write failing tests in `api/tests/Counter.UnitTests/StockStateTests.cs` — `Available`, `AllCommitted` and `None` are three distinct states, not a boolean
- [ ] T039 [P] [US1] Write failing tests in `api/tests/Counter.UnitTests/PartSearchTests.cs` — matching ignores case, spacing and punctuation; no match returns empty rather than an error
- [ ] T040 [P] [US1] Write failing integration tests in `api/tests/Counter.IntegrationTests/AvailabilityEndpointTests.cs` — the contract shape, `stockIsKnown: false` for a part with no inventory record, and a `504` when the store delays

### Domain and services

- [ ] T041 [P] [US1] Implement `Part`, `BranchPosition` and `StockState` in `api/src/Counter.Domain/Catalogue/`
- [ ] T042 [US1] Implement availability rules as named operations in `api/src/Counter.Domain/Availability/AvailabilityCalculator.cs` — not arithmetic inline in a controller
- [ ] T043 [US1] Implement the in-memory catalogue projection in `api/src/Counter.Infrastructure/Catalogue/CatalogueProjection.cs`, built at startup and refreshable on demand
- [ ] T044 [US1] Implement `api/src/Counter.Infrastructure/Erp/AvailabilityReader.cs` reading `INVENTORY` and `BRANCH` through the MCP client

### Endpoints

- [ ] T045 [US1] Implement `GET /api/v1/parts` in `api/src/Counter.Api/Controllers/PartsController.cs` per `contracts/rest-api.md`
- [ ] T046 [US1] Implement `GET /api/v1/parts/{partNumber}/availability` in the same controller, returning the full envelope

### Front end

- [ ] T047 [P] [US1] Implement the search box and type-ahead results in `web/src/features/search/`, auto-focused on load
- [ ] T048 [P] [US1] Implement the branch grid in `web/src/features/availability/BranchGrid.tsx` — colour-coded by stock state, with roving keyboard focus
- [ ] T049 [US1] Implement the part detail screen in `web/src/features/availability/PartDetail.tsx`, leading with the single large free-to-sell figure
- [ ] T050 [US1] Implement the unreachable, unknown-stock and no-match states in `web/src/features/availability/states/` — each visually distinct, none rendering as an empty grid

### Story verification

- [ ] T051 [US1] Write `web/cypress/e2e/us1-availability.cy.ts` — keyboard-only journey from search to branch grid, with `axe-core` assertions
- [ ] T052 [US1] Write `api/tests/Counter.IntegrationTests/AvailabilityReconciliationTests.cs` — every parsed branch position matches the raw record, across the whole data set (SC-005)

**Checkpoint**: User Story 1 is independently shippable. This alone answers the phone call.

---

## Phase 5: User Story 2 — Quote the right price (Priority: P2)

**Goal**: With a customer selected, the screen shows list price, the terms applied, and the net price together.

**Independent test**: Select a customer with contract terms and a covered part; the representative can state the net price and explain which contract produced it.

### Tests first

- [ ] T053 [P] [US2] Write failing tests in `api/tests/Counter.UnitTests/PricingRulesTests.cs` — lapsed terms are disregarded with a reason; the lowest applicable multiplier wins; no terms means list price labelled as list
- [ ] T054 [P] [US2] Write failing integration tests in `api/tests/Counter.IntegrationTests/PricingEndpointTests.cs` — `basis` is `Contract` or `List`, and `disregardedTerms` is populated for an expired promotion

### Implementation

- [ ] T055 [P] [US2] Implement `Customer` and `ContractTerms` in `api/src/Counter.Domain/Pricing/`
- [ ] T056 [US2] Implement `api/src/Counter.Domain/Pricing/PriceCalculator.cs` — net price, applicable terms, and the terms deliberately not applied
- [ ] T057 [US2] Implement `api/src/Counter.Infrastructure/Erp/PricingReader.cs` reading `CUSTOMER` and `PRICING`
- [ ] T058 [US2] Extend the availability endpoint with the `pricing` block, and implement `GET /api/v1/customers` in `api/src/Counter.Api/Controllers/CustomersController.cs`
- [ ] T059 [US2] Implement `PUT /api/v1/session/customer` in `api/src/Counter.Api/Controllers/SessionController.cs` — the only non-`GET` route, and it writes to the session, never the ERP
- [ ] T060 [P] [US2] Implement the customer selector in `web/src/features/pricing/CustomerSelector.tsx`, keyboard-reachable from the header
- [ ] T061 [US2] Implement the pricing panel in `web/src/features/pricing/PricingPanel.tsx` — list, multiplier and net shown together, with disregarded terms visible
- [ ] T062 [US2] Implement the no-customer-selected state in `web/src/features/pricing/ListPriceNotice.tsx` — prompts for a customer while showing list price

### Story verification

- [ ] T063 [US2] Write `web/cypress/e2e/us2-pricing.cy.ts` — selecting a customer changes the price, the selection survives navigating to another part, and an expired promotion is shown as disregarded

**Checkpoint**: The call can now be answered completely — availability and price.

---

## Phase 6: User Story 3 — What the committed stock is promised to (Priority: P3)

**Goal**: A branch showing little free to sell can be expanded to reveal the orders holding the difference.

**Independent test**: Open a part whose stock is partly committed; the representative can name the orders holding it and when stock frees up.

### Tests first

- [ ] T064 [P] [US3] Write failing tests in `api/tests/Counter.UnitTests/OrderStateTests.cs` — only `CONFIRMED`, `ALLOCATED` and `PICKING` hold stock; an unrecognised state holds nothing and is logged
- [ ] T065 [P] [US3] Write failing integration tests in `api/tests/Counter.IntegrationTests/CommitmentsEndpointTests.cs` — the contract shape, and `unaccounted` populated where listed commitments fall short

### Implementation

- [ ] T066 [P] [US3] Implement `Order` and `OrderState` as a closed set in `api/src/Counter.Domain/Orders/`
- [ ] T067 [US3] Implement `api/src/Counter.Infrastructure/Erp/CommitmentReader.cs` — the state filter applied in the query, so the rule lives in one place
- [ ] T068 [US3] Implement `GET /api/v1/parts/{partNumber}/commitments` in `api/src/Counter.Api/Controllers/PartsController.cs`
- [ ] T069 [P] [US3] Implement the expandable branch row in `web/src/features/commitments/BranchCommitments.tsx`, expandable by keyboard
- [ ] T070 [US3] Implement the unaccounted row in `web/src/features/commitments/UnaccountedRow.tsx` — a visible discrepancy beats a tidy screen
- [ ] T071 [US3] Implement the no-commitments state in `web/src/features/commitments/NoCommitments.tsx` — stated in words rather than shown as an empty list

### Story verification

- [ ] T072 [US3] Write `web/cypress/e2e/us3-commitments.cy.ts` — expanding a branch by keyboard reveals its commitments; a branch with none says so
- [ ] T073 [US3] Write `api/tests/Counter.IntegrationTests/CommitmentReconciliationTests.cs` — listed commitments plus `unaccounted` equal the committed total, for every branch of every part (SC-014)

**Checkpoint**: "No" has become "not until Thursday".

---

## Phase 7: User Story 4 — Show the record as the ERP holds it (Priority: P4)

**Goal**: The raw inventory record, the structured form, and the query that retrieved it, side by side.

**Independent test**: Open the record view for a part stocked at several branches; each branch appears in the raw record in the same order as on screen.

### Tests first

- [ ] T074 [P] [US4] Write failing integration tests in `api/tests/Counter.IntegrationTests/RecordEndpointTests.cs` — the response carries delimiters **as stored**, never stripped and never pre-rendered
- [ ] T075 [P] [US4] Write failing tests in `api/tests/Counter.UnitTests/MarkDescriptionTests.cs` — every mark present in a record is described in the `marks` array

### Implementation

- [ ] T076 [US4] Implement `GET /api/v1/parts/{partNumber}/record` in `api/src/Counter.Api/Controllers/PartsController.cs`, returning raw, parsed, marks and query
- [ ] T077 [US4] Ensure the serializer preserves the delimiter characters unaltered in `api/src/Counter.Api/Serialization/`
- [ ] T078 [P] [US4] Implement the record drawer in `web/src/features/record/RecordDrawer.tsx` with focus trapping, opened by `R` and closed by `Escape`
- [ ] T079 [P] [US4] Implement visible, labelled mark rendering in `web/src/features/record/MarkedRecord.tsx`
- [ ] T080 [US4] Implement the side-by-side raw and parsed panes in `web/src/features/record/RecordPanes.tsx`, with the query shown beneath them

### Story verification

- [ ] T081 [US4] Write `web/cypress/e2e/us4-record.cy.ts` — each branch appears in the raw record at the position it occupies in the grid (SC-008)
- [ ] T082 [US4] Write `web/tests/MarkedRecord.test.tsx` asserting marks are rendered visibly and labelled, never hidden

**Checkpoint**: The claim that nothing was flattened is now demonstrable rather than asserted.

---

## Phase 8: User Story 5 — Who is asking, and what they may do (Priority: P5)

**Goal**: Identity, read-only state and the database login are always visible; recent activity is reviewable.

**Independent test**: Sign in, perform several lookups, and confirm the activity record names the person, the database login, and each action.

### Tests first

- [ ] T083 [P] [US5] Write failing integration tests in `api/tests/Counter.IntegrationTests/ActivityAttributionTests.cs` — one record per request per persona, naming the person and the login (SC-006)
- [ ] T084 [P] [US5] Write failing integration tests in `api/tests/Counter.IntegrationTests/ReadOnlyRouteTests.cs` — no route accepts `POST`, `PUT`, `PATCH` or `DELETE` against ERP data

### Implementation

- [ ] T085 [P] [US5] Implement cookie sign-in against the three demonstration personas in `api/src/Counter.Api/Controllers/AuthController.cs`
- [ ] T086 [US5] Implement `GET /api/v1/session` returning identity, read-only state, and whether the database login is shared
- [ ] T087 [US5] Implement `GET /api/v1/activity` in `api/src/Counter.Api/Controllers/ActivityController.cs` — never another user's activity, never a credential
- [ ] T088 [P] [US5] Implement the sign-in screen in `web/src/features/governance/SignIn.tsx`, stating plainly that the personas are for demonstration
- [ ] T089 [P] [US5] Implement the governance strip in `web/src/features/governance/GovernanceStrip.tsx` — identity, database login, shared-login notice, read-only badge
- [ ] T090 [US5] Implement the activity panel in `web/src/features/governance/ActivityPanel.tsx`
- [ ] T091 [US5] Gate every data-changing control behind the read-only flag in `web/src/features/governance/ReadOnlyGate.tsx`, so none is rendered in a read-only session

### Story verification

- [ ] T092 [US5] Write `web/cypress/e2e/us5-governance.cy.ts` — the strip is visible on every screen and the activity panel lists the actions just performed
- [ ] T093 [US5] Write `api/tests/Counter.IntegrationTests/ErpImmutabilityTests.cs` — every ERP record is byte-identical before and after the full suite. This proves read-only by outcome; T084 only proves intent

**Checkpoint**: All five stories complete.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [ ] T094 [P] Implement copy-to-clipboard in `web/src/features/availability/CopySummary.ts` — carrying the demonstration marker and the retrieval time (FR-033 to FR-036)
- [ ] T095 [P] Write `web/tests/CopySummary.test.ts` asserting the copied text contains nothing not on screen
- [ ] T096 [P] Write `api/tests/Counter.IntegrationTests/SearchPerformanceTests.cs` — 200 searches against the full catalogue, 95th percentile under one second (SC-003)
- [ ] T097 [P] Write `api/tests/Counter.IntegrationTests/ConcurrentCallerTests.cs` — ten simultaneous sessions each get the single-caller answer (SC-010)
- [ ] T098 [P] Write `api/tests/Counter.IntegrationTests/FailureShapeTests.cs` — every provoked failure returns a typed problem with a readable detail (SC-009)
- [ ] T099 [P] Add the `axe-core` sweep across every screen to the Cypress suite (SC-012)
- [ ] T100 [P] Add keyboard-only completion of all five journeys in `web/cypress/e2e/keyboard-journeys.cy.ts` (SC-011)
- [ ] T101 [P] Implement light and dark themes in `web/src/styles/`, both meeting AA contrast
- [ ] T102 [P] Implement the demonstration-data marker in `web/src/components/DemonstrationBadge.tsx` and place it on every screen showing figures (FR-032)
- [ ] T103 Write `deploy/api.Dockerfile` — API serving the built front-end assets
- [ ] T104 Write `deploy/mcp.Dockerfile` — the hardened fork plus `mvstore`, bound to loopback
- [ ] T105 Write `deploy/azure/provision.ps1` — Container Apps environment, two apps with the MCP server on internal ingress only, and Azure SQL
- [ ] T106 Write `deploy/azure/deploy.ps1` — build, push and revise; local script only, never a hosted pipeline (Article VIII)
- [ ] T107 Update `CHANGELOG.md` with everything this feature delivered (Article VI)
- [ ] T108 Run the whole verification table in `quickstart.md` and record the results

---

## Dependencies & Execution Order

### Phase order

```text
Setup (Phase 1)
   ↓
Foundational (Phases 2–3)  ← blocks everything
   ↓
US1 (Phase 4) ── MVP, independently shippable
   ↓
US2 (Phase 5) ── extends the availability endpoint
   ↓
US3 (Phase 6) ─┐
US4 (Phase 7) ─┼─ independent of one another
US5 (Phase 8) ─┘
   ↓
Polish (Phase 9)
```

### Story dependencies

| Story | Depends on | Why |
| --- | --- | --- |
| US1 | Foundational | Needs data, the MCP client and the parser |
| US2 | US1 | Extends the availability endpoint and its screen |
| US3 | US1 | Expands a branch row the grid provides |
| US4 | Foundational | Reads a record directly; does not need US1's screen |
| US5 | Foundational | The recording filter is foundational; this exposes it |

US3, US4 and US5 do not depend on one another and may proceed in any order once US1 is done. US4 could in principle start straight after Foundational.

### Parallel opportunities

| Phase | Tasks that may run together |
| --- | --- |
| Setup | T002, T003, T004, T005, T006, T007, T009, T010 |
| Foundational | T011/T013/T015/T018 (store tests); T023/T026/T029 (API tests); T033/T034/T035 (web shell) |
| US1 | T037–T040 (tests); T041 with T047/T048 (domain and front end) |
| US2 | T053/T054; T055 with T060 |
| US3 | T064/T065; T066 with T069 |
| US4 | T074/T075; T078 with T079 |
| US5 | T083/T084; T085 with T088/T089 |
| Polish | T094–T102 all independent |

---

## Implementation Strategy

### Minimum viable scope

**Phases 1, 2, 3 and 4 — through T052.** That is 52 tasks and it delivers a
working product: a representative can find a part and answer the availability
question with live data, keyboard-only, with every request recorded.

Everything after it makes the answer better, not possible.

### Incremental delivery

| Increment | Adds | Value |
| --- | --- | --- |
| Through T052 | US1 | Answers the call |
| Through T063 | + US2 | Answers it completely |
| Through T073 | + US3 | Turns "no" into "not until Thursday" |
| Through T082 | + US4 | Makes the data claim verifiable |
| Through T093 | + US5 | Makes it adoptable |
| Through T108 | Polish | Deployable and proven |

### If scope must be cut

Cut from the bottom of the priority order, never from the middle of a story. A
half-built story leaves untested paths in the product; a story not started leaves
none. In order of what goes first: US5, US4, US3.

Two things are never cut, because cutting them changes what the product claims to
be rather than how much it does:

- **T093** — the byte-for-byte immutability test. Without it the read-only claim
  is an assertion rather than a result.
- **T050** — the distinct unreachable state. Without it a failure to reach the
  data renders as "no stock", and the representative tells the customer something
  untrue.

### Task-format validation

All 108 tasks carry a checkbox, a sequential identifier, a file path, a `[P]`
marker where genuinely parallel, and a `[US*]` label in story phases only.
