# Changelog

All notable changes to this project are recorded here. This file is the single
source of truth for what changed (Article VI). Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- **A page that covers the cold start, because nothing inside the application
  can.** The API container serves the web application as well as the API, so the
  first request after a quiet period has no server to answer it: the browser
  waits on a socket and the screen stays blank for about twenty seconds. Every
  "waking up" message in the application is unreachable at precisely the moment
  it would be useful, which is how a deliberate cost decision came to look like a
  broken deployment.

  The new entry page is served from storage, which is always on and costs a
  fraction of a cent a month. It paints at once, says what is happening and what
  the alternative would cost, wakes the application by asking it for `/health`,
  and hands over the moment it answers. It carries no credentials — the one
  cross-origin request is an anonymous health check — so the session cookie stays
  same-site on the application's own origin and sign-in is untouched.

- **The parsed record shows its parallel fields as a grid.** "Position three of
  every field describes the same branch" was written on the screen and then
  contradicted by it: fields were listed one per line with values joined by dots,
  and single-valued fields sat among them looking as though they ought to have a
  third position too. Parallel fields are now columns and positions are rows, so
  reading across a row *is* the claim. Fields holding one value are shown apart,
  as describing the record rather than a position in it. A record whose fields
  disagree on length says so and marks the gaps, because that is the damage a
  careless write does and hiding it here would hide the point.

- **An answer to "how would anybody know this is real?"** Asked of the record
  view in almost those words, and it is the right question: every pixel is drawn
  by this application, so a picture of separators is a picture. The panel says
  that outright rather than answering with a better picture, then does the two
  things that are worth more — hands over the bytes to be counted in tools we do
  not control, and renders a record pasted in from somewhere else, which a
  renderer written around its own fixture could not. It ends by naming the only
  thing that settles it, which is not on this screen.

### Fixed

- **The explore screen was unreachable.** It rendered only while no part was
  selected, so picking one made it vanish with nothing offering a way back — and
  it holds the file list, the dictionaries and the only editor in the
  application. Two statements elsewhere kept pointing at it. It is now opened
  from the header at any time, and the tour opens it rather than relying on
  nothing being selected.

- **The setup prompt carried a stale claim and unusable characters.** It still
  said the tool list had "no arbitrary query" — untrue since the selection tool
  was added — and its em dashes arrived as replacement characters when pasted
  into a terminal. It is plain ASCII now, and accurate.

- **Two more claims about the cold start were false.** The tour and the
  deployment summary both said the screen explains the wait. It cannot: the
  server that would draw that message is the one starting.

- **The tour says what the assistant cannot do, before somebody finds out.** Four
  of its tools are built for this counter and four ask the database what it holds,
  so it answers about files nobody wrote code for — just more slowly. Asked
  something outside all eight it says so rather than guessing, and a reader who
  has not been told that reads a refusal as a fault. The suggested questions cover
  both halves for the same reason: the first question somebody types decides what
  they think the thing is.

- **The assistant can answer against a schema nobody anticipated.** It had three
  tools, all compiled to this demonstration's layout, so a question about anything
  else came back as "I don't have a tool for that" — which is honest and useless.
  Three more ask the database what it holds instead: `list_files` names what is
  there, `describe_file` reads a file's own dictionary for its field names,
  positions and whether each holds one value or many, and `query` runs a selection
  against any of them. A MultiValue database describes itself, so this is the
  difference between an assistant that answers the questions somebody anticipated
  and one that can be pointed at an unfamiliar ERP.

  `query` refuses any verb that is not SELECT or SSELECT, in this code and again
  at the MCP server. Widening that takes a deliberate change in two places.

- **Pricing, which the assistant could not see at all.** Asked which customer had
  the best price on a part whose price was on the screen, it correctly reported
  that it had no tool for pricing: the screen reaches the price through a
  different path. `read_price` quotes one customer, `compare_prices` covers every
  price class at once — one call, not one per account, because in this kind of ERP
  a price belongs to a price class and customers are assigned to one.

- **The assistant is told what is on the screen.** People at a counter do not
  repeat themselves; with a part in front of them they ask "how quickly can we get
  twenty of these to Denver?". That came back as "which part did you mean?".

  The context carries the referent and nothing else — a part number, a customer
  account, and the words to read a pronoun as. No quantity, price or branch
  travels with it, the type has no field capable of holding one, and the
  instructions say plainly that no figures came with it. Every number in an answer
  still comes back from a tool call. The transcript shows what it was told, so a
  resolved pronoun cannot be mistaken for the model remembering the data.

- **A tour step for where the activity log lives.** The tour showed the panel and
  never the button, so it read as though the record of who asked what opened from
  the header. It is in the strip along the bottom.

- **The guided tour has browser coverage, which it did not before.** It is the
  first thing anybody sees and nothing tested it. Five journeys, each pinning a
  failure that actually happened: it opens on a first visit and not the second,
  the assistant and MCP steps are present (asserted against the API rather than
  guessed at), the card never overlaps the ring it points at, every step that
  describes a drawer has that drawer on screen, and Escape leaves from anywhere.

  Two of them passed while checking nothing on the first attempt. Cypress queues
  a walk in one tick, so every "is the card there?" resolved before React had
  painted, the walk skipped all twelve steps, and the tests went green having
  visited none of them. They now count what they compared and fail if they
  compared nothing — which is how both of the defects above were found.

- **Every other spec now starts as a returning visitor.** The tour opens by itself
  on a first visit, Cypress clears local storage between tests, so every test was
  a first visit with a tour laid over whatever it was asserting. Twenty-four
  failures across six specs, every one reported as a CSS property rather than as
  the tour.

- **Two paths on the "use your own data" panel, and the second one is the point.**
  The page used to describe one setup and leave the reader to work out whether it
  applied to them. It now separates looking at the hosted demonstration — my data,
  my API key, capped to Claude Haiku — from running it against your own database,
  which is the only version that proves anything about *your* files. Both
  repositories are linked, every command has a copy button, and three boxes for
  host, user and account rewrite the commands as you type. Nothing typed there
  leaves the page, and there is deliberately no password box: a password belongs
  in the environment, not somewhere a screenshot or a browser's saved form data
  can reach it.

- **The setup, written as an instruction for somebody else's coding agent.** One
  button copies a paragraph that names both repositories, the four variables, the
  verification command and the two things that make the setup safe — do not guess
  at credentials, and stop at the first step that misbehaves rather than working
  around it. The setup is only four variables and one command, and it is still
  where most evaluations stop; this removes the gap between reading about a tool
  and having one running.

### Fixed

- **The tour claimed the assistant had three tools.** It has eight, and had for
  most of a day. A tour that miscounts the thing it is explaining is worse than no
  tour: the reader checks, finds five more, and stops believing the rest of it.

- **Two places still said there was no query tool.** There is one — added because
  an assistant holding only tools compiled to one layout is useful exactly as far
  as somebody's foresight went. Both now say what is actually true: no write, no
  delete, and a selection tool that refuses any verb but SELECT or SSELECT, in the
  application and again at the server.

- **The governance strip fell off the bottom of the screen.** It is the one thing
  that must be on screen whatever else is — read-only, demonstration data, one
  shared login — and at 1024x900 it sat at y=989 in a 900-pixel window. It used
  `position: sticky`, which cannot move outside the box it sits in, and it sits in
  a wrapper the tour added to spotlight it: a wrapper exactly the strip's own
  height. The stickiness had nowhere to go, so on any screen where the branch grid
  wrapped, the disclosure silently disappeared. The application is now exactly the
  viewport tall with the middle row taking the scrolling, which makes the claim
  structural rather than a side effect of the content happening to be short.

- **The tour card still covered what it pointed at.** On the explore step the
  spotlight was 615 by 1213 — near enough the whole screen — so none of the four
  placements fit and the card docked to the bottom, over the table the step exists
  to describe. A spotlight taller than the room left over is now trimmed so the
  card always has somewhere to go: the top of a long region is highlighted and the
  card sits below it. A table is read downwards, so its headings and first rows
  are what the reader needs while the card is open.

- **A deploy could not stop the services blocking it.** The port sweep spared any
  process older than the current session, which protected other people's dev
  servers — the point — but also protected our own from an earlier session, and
  those hold the assemblies the build writes. The sweep now asks where a process
  runs from rather than when it started: out of this repository or the fork the
  MCP server is installed from, it is ours whenever it started; from anywhere
  else it is not ours however recently. Start time decays into the wrong answer;
  location does not.

- **Stopping only stopped what was recorded.** The PID file is written when a
  service starts and deleted when one stops, so a session that ended badly left
  services running with nothing tracking them — `-Stop` reported "nothing
  recorded as running" while three orphans held all three ports. It now sweeps
  the ports themselves as well, which is only safe because the test above is
  location rather than age.

- **A build failure was reported as a failing test suite.** `dotnet test` builds
  before it runs and returns the same exit code either way, so a locked assembly
  came back as "the .NET suites failed" — a false statement that sends the reader
  hunting a broken test that does not exist. The build is now its own step with
  its own message, which names the likely cause and the command that fixes it.

- **A hardcoded port list disagreed with itself.** The three ports are named once
  and both the pre-run check and the stop sweep read that list. The first attempt
  keyed it by port number in an ordered dictionary, where an integer index
  selects by *position* — so every message read "Stopping  on port 5081" with the
  name missing.

- **Both pickers stayed open at once.** Each had a container ref and neither used
  it, so opening the parts list over the customers list left the one behind still
  catching the mouse. Closing now watches pointer and focus both — a keyboard user
  tabs away without ever generating a click, and a list left open behind them is
  one their next Enter might select from.

- **Find, review, update — the write half, kept where it cannot weaken the read
  half.** Clicking a value in the explore screen offers to change it: it names the
  record and the position, asks for confirmation, writes one value in place, then
  reads the record back and shows how many values each field held before and
  after. If any of those numbers changed, a parallel field has moved and the panel
  says so as a failure rather than reporting "written" — which would be a true
  statement that misleads, because the record is still valid and every later read
  agrees with it.

  The write lives on `IErpWriter`, never on `IErpReader`, with its own one-tool
  allowlist. That is what keeps "the reader has no write path" true of the reader
  rather than true of a condition somebody wrote carefully — a test asserts it
  against the type. `IErpWriter` is registered only when `Erp:Writable` says so;
  the deployment does not, so the endpoint answers 501 with a reason and the
  screen offers no editor at all rather than a disabled one.

- **A write path, in a module of its own.** `mvstore/driver.py` still refuses
  every write unconditionally and is unchanged; `mvstore/writable_driver.py`
  subclasses it and adds one. A deployment picks by name — `U2_DRIVER=demo` or
  `U2_DRIVER=mvstore.writable_driver` — and selecting the writable one is still
  not permission to write, because `MVSTORE_WRITABLE` has to agree. Two switches,
  answering different questions: which code is loaded, and whether it may act.

  `update_value` changes one value in place and refuses to pad. That refusal is
  the point. A parallel field may legitimately be shorter than its siblings — bins
  are recorded for some branches and not others — so setting a value by index and
  padding to reach it invents positions, and in an inventory record a position is
  a claim about a branch. Measured: padding to write one bin produced a field of
  length three where every sibling was four, with no error and a well-formed
  record. Dictionaries are never writable, because changing one is a schema
  change.

- **"Use your own data" — how to point this at your database and your key.** The
  question anybody serious asks within a minute, and one the demonstration cannot
  answer by itself: nobody evaluating software wants to send their inventory
  through somebody else's API key, or judge a system on data they have never
  seen. Four environment variables, one command that proves the connection and
  the write refusal, and an honest table of which parts fit an unfamiliar schema —
  the MCP server and the explore screen do; the counter screens need a mapping,
  and mapping a real ERP schema is a real job no amount of tidy code makes
  trivial. Offered from the header rather than buried, because an answer somebody
  has to hunt for reads as no answer.

- **A screen with no field names in it, built from the database's own
  dictionary.** Every other screen here is compiled to one layout, which is right
  for a counter — the people using it want the same four numbers in the same
  place every time. It is wrong for anybody evaluating this against their own
  system: their files are not called PRODUCT and INVENTORY and their fields are
  not in these positions, so a fixed layout tells them nothing about their own
  data. This reads the account instead. The file list comes from the account, the
  field list from each file's dictionary, and the column headings from what that
  dictionary calls them — including its own warning about which fields are
  multi-valued. Searching picks a field from the dictionary and matches on the
  position it reported, so the selection is parameterised rather than composed
  and nothing typed becomes part of the query's structure. The selection that ran
  is shown underneath: a screen that says what it asked is one whose answer can
  be checked. There is no mapping to write, because a MultiValue file already
  carries one.

- **A question in words, answered from the ERP through the MCP server.** This is
  what makes the MCP server load-bearing rather than decorative: without a model
  in the loop, a protocol designed for a model to call tools does nothing an
  ordinary function call would not, and anybody who works with this software
  would notice. Every answer is assembled from tool calls, and every one of those
  calls is returned with it — including the raw record, separators intact.
  That transcript is the point: a reader looking at "299 free to sell at Grand
  Junction" cannot otherwise tell a MultiValue record from a relational row
  wearing separators.

  The tool list *is* the read-only guarantee. There is no write tool, no
  arbitrary query tool, and the file a record may be read from is one of four
  named ones — so a model that decided to change something has nothing to decide
  it with, and the guarantee rests on the tool surface rather than on the model
  behaving or a prompt asking it to.

  Cost is bounded in five independent places, because the demonstration is public
  and the key behind it is personal: the model is a constant with no code path
  that can point it elsewhere, output is capped per answer, tool calls are capped
  per round **and in total**, questions are capped per session, and the whole
  deployment has a daily token ceiling. The total-call cap exists because rounds
  alone were not a limit — Claude asks for tools in parallel, so four rounds
  bounded the conversation and bounded nothing about the work.

- **A guided tour, which drives the application rather than describing it.** Six
  steps, shown once on a first visit and replayable from the header afterwards.
  Everything this application is for — the branch grid, the contract price, the
  stored record, the audit trail — sits behind a part number a first-time visitor
  has no way to guess, so the steps that talk about those things select a part
  and open the drawer themselves. The reader watches it happen instead of being
  told it would. The spotlight cuts a hole in a dimmed page around the real
  control, which stays usable; nothing is a mock-up. Escape leaves, arrows and
  Enter move, and the whole thing is operable without a mouse.

- **Both pickers now show what is there before anything is typed.** Found by
  using the application rather than by testing it: opened cold, there was a search
  box for parts and a search box for customers and no way to answer the question
  anyone actually arrives with — what is in here? A counter representative learns
  their catalogue over months and types a part number from memory; nobody meeting
  the system for the first time can, and that includes everyone it gets
  demonstrated to. Clicking either box now lists the catalogue or the account
  file, says how many there are altogether, and narrows as you type. Browsing is
  a separate route from searching (`/parts/browse`, `/customers/browse`) because
  they answer different questions — "what is here" against "where is this" — and
  only browsing has any business reporting a total. Keeping them apart also
  leaves the search guard intact: a search with an empty term is still refused.
  A browsed row carries no quantity, because stock is read live when a part is
  opened and a number standing in for "no number" is the plausible wrong answer
  this application exists to prevent.

- **The demonstration store has dictionaries, so it describes itself.** A
  MultiValue file has two parts — the data, and a dictionary saying what each
  field means. This store had only the first, so the MCP server's discovery tools
  returned nothing against it: the path a stranger takes first was the one path
  never exercised end to end. Each of the six files now carries real D-type
  dictionary items with locations, conversion codes, headings and the
  single/multi flag — including the five parallel `INVENTORY` fields marked
  multi-valued, which is the only warning a reader gets that position *n* of each
  belongs to the same branch.

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

- **Both pickers look like the dropdowns they always were.** They opened a list
  on click before this, and nobody clicked — they looked like plain text boxes,
  and a control whose behaviour has to be discovered by accident is one most
  people never find. Each now carries a caret, a pointer cursor and a placeholder
  that says to choose rather than to search.

- **The tour covers what a reader would otherwise have to find by accident**: the
  assistant, how it reaches the database through MCP and that all three of its
  tools are reads, the audit trail, pointing it at your own database, and the
  keyboard shortcuts. Twelve stops.

- **The tour now covers the assistant and the dictionary-driven screen**, and
  skips a step whose target is genuinely absent. The distinction is which
  absences the tour can fix: a step declaring it needs a part is one whose target
  the tour creates itself when it gets there, so it must never be filtered —
  doing so dropped the branch grid and the stored record, two of the steps most
  worth showing. Absences it cannot fix are skipped: no API key means no
  assistant, and on such a deployment the tour would otherwise dim the screen and
  advertise a feature that is not there.

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

- **The tour silently dropped the two steps about the assistant.** It decided
  which steps to show by looking for each one's element a third of a second after
  opening — a race it lost, because the assistant's panel renders nothing until
  its own status request comes back. On a deployment slower than the timer, the
  tour concluded there was no assistant and skipped the steps explaining the
  whole point of the thing. It now asks the API instead, which is deterministic.

- **A tour step could point at nothing.** The steps that need a part select one
  when they open, and the branch grid does not exist until its own request
  returns — so the spotlight measured an element that was not there yet and
  centred the card over the step whose purpose was to point at that grid. It now
  keeps looking until the target appears.

- **The assistant's transcript reported a count instead of the data.** A step
  reading "8 parts matched" tells a reader that a number was produced and nothing
  about which parts, so somebody checking whether the answer follows from the
  data had been handed the answer twice and the data never. Every call now shows
  what it returned.

- **The tour's record step spotlit a twelve-pixel strip.** It pointed at the
  element wrapping the drawer, and the drawer is positioned fixed — so the
  wrapper had almost no height. It now points at the panel, which has one.

- **A test was coupled to a part number the data generator happened to produce.**
  Regenerating the demonstration data broke it: it asked for `S-BRK00000`, which
  no longer existed, and reported that the application had failed to recover from
  a timeout when what had actually happened is that the part was gone. The
  fixture now reads a key out of the copied data. Tests about failure paths still
  name a key that does not exist, which is clearer there than borrowing a real
  one.

- **The post-deploy search check failed on a cold start.** The request
  immediately after a deployment can meet a replica that has not finished
  starting and come back as a gateway timeout from the platform rather than an
  answer from the application — which is the cold start this deployment is
  designed around, not a fault. It is now waited out across four attempts. An
  answer that arrives and is wrong still fails the deployment.

- **`run-dev-clean.ps1 -Stop` could stop a process it never started.** The port
  sweep assumed "the port is ours because this script assigned it", which holds
  only while the port was free. Vite falls back to the next port when its own is
  taken, so on a machine already running another project the script reported
  5173, bound 5174, and then stopped whatever else was listening on 5173 — which
  it did, to an unrelated development server. The sweep now leaves any process
  that started before this session, the same start-time test the recorded
  processes already used and for the same reason: an id alone is not identity.
  Starting also refuses outright when a port it needs is taken, rather than
  moving to another one and printing an address that answers with somebody
  else's application.

- **A listing of a dictionary read the wrong file, and said so by returning
  nothing.** `LIST DICT INVENTORY` was parsed by taking the second word, which is
  right for `LIST PRODUCT` and wrong here — it read the file as `DICT`, found no
  records under that name and printed an empty listing. Not an error: an empty
  answer, which is the kind that gets believed. The parser had already resolved
  the name correctly and the formatter worked it out again, differently, which is
  what let them disagree. It is now passed the name the parser resolved.

- **`LIST X @ID` printed whole records instead of keys.** Universe prints keys
  alone when asked for `@ID`, and the tools that ask this way parse the result by
  line — so receiving whole records meant every line began with attribute marks
  and a reader taking the first word took an entire record as a key.

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

