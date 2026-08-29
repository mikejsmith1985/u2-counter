# Changelog

All notable changes to this project are recorded here. This file is the single
source of truth for what changed (Article VI). Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- **The governance strip says how this is hosted.** The deployment powers itself
  down when nobody is using it, so a first visit after a quiet period waits about
  twenty seconds. The page is served by that same container, so for most of that
  wait the browser has nothing to show — the application's own waking screen
  cannot render until the thing serving it is running. That makes the strip the
  only place a reviewer who waited can learn the delay was chosen rather than
  suffered, and a deliberate trade that goes unsaid reads exactly like a system
  that is merely slow. The waking screen now names the trade too, rather than
  only reporting the fact.

- **The deploy script now proves the deployment instead of announcing it.**
  Everything it did previously checked that Azure had accepted what it was given,
  which is not the same as the application working — a broken audit trail
  survived seven deployments, each reporting success because each asked nothing
  after the update was accepted. The pre-deploy gate did check `/health`, but
  against the local build, where the database sits on a local disk and works; the
  one environment where it was broken was the one nothing asked. The script now
  waits for the deployed application to become ready, fails the deployment if it
  reports a non-durable audit trail, and runs one real search — because a
  catalogue count proves the catalogue was read, not that a question can be
  answered from it.

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

- **A per-request budget on every ERP call**, enforced in `ErpReader` by racing
  the call against the budget and dropping the connection when it expires. A
  timeout that only stops the waiting leaves the query running, which under load
  is how a system dies — and cancelling a token does not end a call already in
  flight, so the race is what actually bounds it.

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

### Changed

- **A cold start is 22 seconds rather than 52.** Measured against the deployment
  after seven minutes idle, which is how a first visitor arrives. The whole
  difference is the thirty seconds the failing audit migration spent waiting on a
  lock it could never take: it blocked start-up, so the platform logged thirty
  consecutive failed start-up probes and held the container out of rotation while
  it waited. The page is served by the same container that scales to zero, so
  this is time a visitor spends looking at an empty browser tab before the
  application's own waking screen can render — which is why it was worth chasing
  rather than accepting.

### Fixed

- **The audit share's mount options could be set once and never changed.** The
  block that configures them ran only when the app was created, with a comment
  saying a routine redeploy never had to touch it. That made the setting
  write-once: adding `nobrl` changed the script, reported success, and left the
  deployment exactly as it was, because the patch looked for `volumes: null` and
  there was no longer a null to replace. A second attempt then matched nothing
  because the CLI renders the volume as a list item — `- mountOptions:` — and the
  pattern was anchored on whitespace alone. Both failures were silent, which is
  the same shape as the defect they were trying to fix. The mount is now
  reconciled on every deployment, written only when it differs, and **read back
  afterwards** — a configuration change that reports success and does nothing is
  the thing this is guarding against.

- **The deployed application reported an audit trail it did not have.** Found by
  reading the running container's logs rather than by any test. `/health` said
  `isAuditDurable: true` while every write failed with "no such table:
  ActivityRecord": the schema had never been created, because SQLite cannot take
  its write lock on the Azure Files share the database sits on. Bringing the
  schema up to date issued `CREATE TABLE IF NOT EXISTS "__EFMigrationsLock"`,
  waited the full thirty-second command timeout and failed with "database is
  locked" — which also blocked start-up for those thirty seconds, so the platform
  recorded thirty consecutive failed start-up probes and every cold start was
  half a minute slower than it needed to be. One unsupported lock, three faults.

  Three separate corrections, because each piece was defensible alone and only
  the combination was dangerous:

  - `locking_mode=EXCLUSIVE` and an explicit `journal_mode=DELETE` are now set on
    every connection. The first is what SMB can honour, and is correct rather
    than convenient at one replica: a second writer would be refused rather than
    corrupt anything. The second is SQLite's own default, pinned because
    write-ahead logging needs shared memory a network filesystem does not have,
    and switching it on later would break this in a way that looks unrelated.
  - The statement budget is eight seconds rather than thirty, so a database
    problem can no longer hold the port closed long enough to look like a slow
    application.
  - `isAuditDurable` now reports whether anything is being **stored** rather than
    whether a database has been **configured**. Those are different questions,
    and answering the second while appearing to answer the first is what let this
    run for hours: the one field a reviewer would check to find the problem was
    the field concealing it. The claim is now withdrawn by evidence — a migration
    that could not run says so, and so does the first write that fails.

- **A search that was only punctuation returned the whole catalogue.** Matching
  ignores punctuation so that a part number read off a box is found however its
  hyphens fared, which means a query of `*`, `-`, `...` or `%` normalises to the
  empty string — and every part number starts with the empty string, so the
  ranking scored all three thousand parts as part-number prefix matches. Found by
  throwing hostile input at the deployed application: `q=*` returned a full page
  of real parts. This is the failure the application exists to prevent rather
  than an untidy edge, because nothing about the answer looks wrong — real parts
  with real quantities, presented as strong matches for something nobody
  searched for. A visibly missing answer gets questioned; this one gets read out
  to a customer.

- **A MultiValue file name could name a file outside the store.** The store keeps
  one file per MultiValue file and built the path by joining the caller's name
  onto its root without checking it. That name is reachable input rather than a
  constant the application chooses: the MCP server exposes it as a tool
  parameter. It escaped two ways, and only one of them looked like an escape —
  `../` walked up, and an absolute path did not join at all, because
  `Path("/srv/data") / "C:/Windows"` is `C:/Windows`, the root discarded silently
  by code that reads like ordinary path joining. Reading, writing, listing keys,
  testing existence and deleting were all affected. The only thing limiting it
  was the `.mv` suffix the store appends, which is a real limit and an accident
  rather than a control. A file name is now required to be a single plain name,
  refused rather than sanitised — stripping the dangerous parts out invites an
  encoding nobody thought of, and no legitimate caller has ever needed a path.

Each of these was found by a test written before the defect was known to exist,
or by reading the repository as a hostile reviewer rather than as its author.

The second kind is worth separating, because they were not failures of the code
so much as failures of the writing about it: the documentation ran ahead of the
implementation and nothing pulled it back. They are listed under *Documentation
that had stopped being true*, below.

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

- **The `R` shortcut printed in the header did nothing.** Selecting a part left
  focus in the search box, and the shortcuts are ignored while a text field has
  focus — correctly, or typing an R into a part number would open a drawer. The
  person was shown a hint, pressed the key, and was ignored.

- **Focus was then stolen on every data refresh**, one of which happens when a
  customer is chosen: someone selecting a customer and immediately typing a part
  number lost their first keystrokes to a heading, silently.

- **Closing a drawer left nothing focused**, stranding a keyboard user at the top
  of the document with the whole page to tab through to get back.

- **A branch with no stock was dimmed to 3.4:1 contrast** — and those are exactly
  the rows a representative reads to confirm there is genuinely none before
  telling a customer so.

- **A slow ERP was reported after the query finished, not after the budget.**
  Cancelling the token stops the next call and does not reliably end one already
  in flight, so a ten-second query against a two-second budget took ten seconds
  and then failed. The call is now raced against the budget and the connection
  dropped. Then the filter written to enforce it turned out to truncate every
  slow response to an empty `200` — the single worst answer this application can
  give — and was removed.

- **`run-dev-clean.ps1` recorded the launcher rather than the service.**
  `dotnet run` and `npm.cmd` each start a child and exit, so stopping the recorded
  id left the actual service holding its port. The stop path now ends the recorded
  process tree and, separately, whatever still holds the port this script
  assigned — still one specific id at a time, never a name pattern (Article II).
  A partial failure to start is recorded as it happens, so services that did start
  remain stoppable.

### Documentation that had stopped being true

Found by an adversarial read of both repositories. The test counts were honest;
several of the architecture claims around them were not.

- **Seven documents still said SQL Server and Testcontainers** hours after the
  audit trail moved to SQLite. The plan's own constitution-compliance table
  certified "real SQL Server rather than SQLite" as an Article I pass — a
  self-audit that passed itself on a statement the code contradicted. The
  decision is corrected in place with the original text left visible.

- **The launcher started a SQL Server container nothing connected to.** The
  connection string was never set, so local development had never once exercised
  the durable path — the one place a durability bug would have shown up was the
  one place it could not.

- **`execute_query` was permitted and called by nothing.** The arbitrary-query
  tool, the most capability any entry on that list could grant, left dangling —
  and the test written to catch exactly this could not, because it searched the
  reader's source for a constant that sat in a method nobody invoked. The
  permission is gone and the test now looks for callers.

- **`isComplete` could never be false.** Every response was built with
  `Complete()`; `Partial` was written, documented, rendered by the front end and
  never called, while three real caps truncated answers silently. Fifteen
  customers with the sixteenth invisible is a price quoted against the wrong
  contract.

- **Article IX was cited by name in the file that broke it.** No Key Vault, two
  credentials in script variables. Recorded now as a known gap with what closing
  it would take, rather than claimed as kept.

- **The store said it rejected tabs on write and rejected nothing.** A tab or a
  line break does not fail on write — it succeeds, and reads back as a different
  record, or as two.

- **A malformed request rendered as "the system could not reach the stock
  data"**, sending a representative after an outage that was not happening.

- **About twenty comments described code that was not there**, including
  `SessionController` calling itself "the one route that is not a GET" with a
  `POST` thirty-five lines above it, and a contract section describing the API
  sending the signed-in user's subject to the fork — a mechanism that has never
  existed, and the reason the API keeps an audit trail of its own.

- **The hardened fork logged OAuth token bodies at INFO** — access tokens,
  refresh tokens and client secrets, in cleartext, on the production HTTP path,
  in a server whose README says credentials are never logged. Upstream code the
  first hardening pass walked past.

- **Read-only mode did not cover `call_subroutine`**, which executes arbitrary
  cataloged BASIC. Read-only disabled the tools that announce themselves as
  writes and left open the one that could do anything without saying so.

