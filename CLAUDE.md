# Counter — Agent Instructions

A counter-sales availability lookup for an electrical distributor, reading a
MultiValue ERP through a hardened MCP server.

@.specify/memory/constitution.md

## Workflow rules (binding)

- Read every rule in `.specify/memory/constitution.md` before starting a task.
- Apply `workflow-enforcer` to every task, then `code-quality`, `framework-first`,
  `branching-strategy`.
- Never kill processes by name pattern. `scripts/run-dev-clean.ps1` stops
  processes by id from `.run/pids.json` only (Article II).
- Follow Red → Green → Refactor. The failing test comes first.

<!-- SPECKIT START -->

## Active Feature

**Counter Availability Lookup** — `specs/001-counter-availability-lookup/`

- Spec: `specs/001-counter-availability-lookup/spec.md`
- Plan: `specs/001-counter-availability-lookup/plan.md`
- Research: `specs/001-counter-availability-lookup/research.md`
- Data model: `specs/001-counter-availability-lookup/data-model.md`
- Contracts: `specs/001-counter-availability-lookup/contracts/`
- Quickstart: `specs/001-counter-availability-lookup/quickstart.md`

**Stack**: C# 13 on .NET 9 (ASP.NET Core Web API, controllers) · TypeScript 6.0
with React 19.2, Vite 8.2, TanStack Query, Radix UI · Python 3.12 (demonstration
MultiValue store, hardened `u2-mcp` fork) · SQL Server via EF Core 9 · xUnit with
Testcontainers, Vitest, Cypress with `cypress-real-events` and `axe-core`, pytest
· Docker on Azure Container Apps

<!-- SPECKIT END -->

## Non-negotiables carried from the spec

- **Read-only against the ERP.** No mutating verb may exist in the API, and three
  tests assert it — including one that compares every ERP record byte-for-byte
  before and after the suite.
- **Never serve a stale figure in place of a live one.** Caching *what exists* is
  safe; caching *how much there is* is the failure FR-035 forbids.
- **"Could not reach the data" is never rendered as "no stock".** A `504` has its
  own state with a retry; an empty `200` means nothing matched.
- **Every action reachable by keyboard**, with a visible focus ring and no WCAG
  2.1 AA violation. Cypress uses real events, because synthetic ones bypass the
  focus behaviour that is the requirement.
- **Position *n* of every parallel field belongs to the same branch.** A branch
  shown against another branch's figures is a silent defect — the screen still
  looks right.
- **An order holds stock only while confirmed, allocated or being picked.** The
  state set is closed; an unrecognised state holds nothing and is logged.
- **Demonstration data is labelled wherever it appears**, including in text the
  user copies, which also carries the time it was taken.
