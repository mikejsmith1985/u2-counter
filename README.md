# Counter availability lookup

A branch counter representative has a customer on the telephone. The customer
reads a part number off a box and asks two questions: have you got it, and what
does it cost me. This answers both on one screen, from the keyboard, in about
five seconds — reading live from a MultiValue ERP and writing nothing back.

```
browser ──▶ ASP.NET Core API ──▶ MCP server ──▶ MultiValue store
              + React front end     (hardened fork)   (AM / VM / SM records)
```

## Why it is built this way

**It reads the ERP through an MCP server, not a database driver.** The server
already enforces read-only access, an allowlist of query verbs, per-caller
identity and an audit trail, with tests proving each. Reaching past it to a
driver would mean rebuilding all of that here, worse. The server is a hardened
fork; the fixes it carries are documented in that repository.

**The store holds real MultiValue records.** Attribute, value and subvalue marks
— `CHAR(254)`, `CHAR(253)`, `CHAR(252)` — not JSON with the marks mentioned in a
comment. Fields 1 to 5 of an `INVENTORY` record are *parallel*: position three of
each field belongs to the same branch. That property is the one a parser gets
wrong in a way nobody notices, because reading position three of one field beside
position four of another produces a screen that is entirely plausible and
entirely wrong. There is a test that compares every parsed figure against the raw
record, and it reads the raw record with different code, because a parser used to
check itself agrees with itself whatever it does.

**Three different answers never look the same.** "Nothing matched", "the system
could not be reached" and "stock is not recorded for this part" collapse into an
identical empty result if you let them — and a representative reading that would
tell a customer there is no stock. Each has its own state, wording and colour, and
failures arrive typed rather than as a shape the screen has to guess from.

**It says whose figures these are, everywhere they appear.** A governance strip
on every screen, and the same badges inside the record drawer, because that is
the screen most likely to be photographed and a MultiValue record with no badge
on it is an image anyone could present in good faith as production data.

## Read-only, proved rather than asserted

Three checks, closing three different routes to a write:

1. No route accepts a mutating verb against ERP data.
2. No forbidden tool name appears anywhere in the infrastructure source outside
   the single file that lists them — and every permitted tool is one the reader
   actually calls, so an unused permission fails the build.
3. **Every ERP file is byte-identical before and after the suite exercises every
   screen.**

The third is the one that matters. The first two describe intent; only the third
describes what happened.

## Running it

```powershell
./scripts/seed-demo-data.ps1      # 3,000 parts, 12 branches, 150 customers, 800 orders
./scripts/run-dev-clean.ps1       # everything, on loopback
```

Then <http://127.0.0.1:5173>. Full instructions, the verification table and what
the suites actually reported are in
[specs/001-counter-availability-lookup/quickstart.md](./specs/001-counter-availability-lookup/quickstart.md).

The launcher records each process id with its start time and its port, and stops
only those — never by matching a process name. Process ids get reused and process
names are shared, so an id alone is not identity and a name is not ownership.

## The suites

| Suite | What it runs against |
| --- | --- |
| `Counter.UnitTests` | The domain rules, in isolation |
| `Counter.IntegrationTests` | The real MCP server as a process, and SQL Server in a container. Nothing mocked |
| `mvstore` | The store, its verbs, and the seeder's own obligations |
| Vitest | What the copy button puts on the clipboard, and how a record renders |
| Cypress | A real browser with real events, and `axe-core` on every screen |

The seeder names fourteen conditions the data must contain and checks each one
after generating, so an edge case cannot quietly disappear from the data while
the tests that need it keep passing against nothing.

The defects these suites found are listed in [CHANGELOG.md](./CHANGELOG.md) with
what each would have done to somebody reading the screen. A suite that never
fails is a suite nobody should believe.

## Deploying

Local scripts against Azure Container Apps, never a hosted pipeline — so what
deploys is what was on the machine that deployed it, and the gate is the same
suite its author was running a minute earlier. See [deploy/](./deploy).

The MCP server is deployed with internal ingress only and exactly one replica: it
holds the database session, and a second replica would reintroduce by deployment
the connection multiplication the fork was hardened against.
