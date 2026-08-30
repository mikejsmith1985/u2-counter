# What it doesn't show

The demonstration answers one question — *can I promise this part to this
customer today?* — from a MultiValue database, through an MCP server, with every
call the assistant makes shown on screen.

This is the other half: **what it does not prove, and what would settle each
one.** Written down because they are the questions a demonstration cannot answer
about itself, and because finding them yourself would be worse than reading them
here.

**Try it:** <https://counterst92782.z13.web.core.windows.net> — it powers down
when nobody is using it, so the first load takes about twenty seconds. That page
explains itself while you wait. A tour opens on arrival.

---

## The limits

| What is not proven | Why | What would settle it |
|---|---|---|
| **It has never run against a real U2 instance** | The store behind it is synthetic. A Personal Edition licence did not arrive in time. | Four environment variables and one command against a restored copy. See [Run it against your own instance](#run-it-against-your-own-instance). |
| **The security fixes are not production-validated** | They are measured against the original code — the same test fails there and passes on the fork. That is not the same as a scan. | A production-grade scan, and the same suite run against a real instance. |
| **The counter screens assume this schema** | They are compiled to the demonstration's layout. Every file name and field position lives in one class, `ErpFiles.cs`. | A mapping pass. Real work, and no amount of tidy code makes it trivial. |
| **The assistant is only partly schema-agnostic** | Four of its eight tools are built for this counter and would fail elsewhere. The other four ask the database what it holds and work anywhere. | Pointing it at an unfamiliar account and asking about a file nobody wrote code for. |
| **One shared database login** | The database sees a single account however many people use it. | Per-caller credentials. The fork adds the identity plumbing; this deployment does not use it. |
| **Built for a counter workstation** | It works on a phone and is not good on one. | Nothing, deliberately. The job is done standing at a counter. |

---

## The MCP server, and what "fixed" means here

An open-source U2 MCP server, run from [a fork](https://github.com/mikejsmith1985/u2-mcp).
Ten defects, and the word *fixed* is doing measured work: each one below was
tested against the original checkout and against the fork.

| Defect | What it cost | Evidence |
|---|---|---|
| Non-ASCII data silently dropped | Accented names and currency symbols vanished from results with no error. | fails upstream |
| Auto-reconnect never reconnected | After a dropped session every later request got a null session, with no watchdog to clear it. | fails upstream |
| Truncated answers looked complete | A row limit was appended silently. "How many do we have?" could return a capped number that reads like the real one. | fails upstream |
| Cancelled queries kept running | A timed-out query carried on against the database. | fails upstream |
| Field names lost to prefix matching | `LIST.PRICE` and `LIST.COST` — ordinary MultiValue names — vanished from a dictionary listing with no error. | fails upstream |
| Files lost to substring matching | A file called `RECORDS` was invisible, in the one call whose job is saying what exists. | fails upstream |
| Writes permitted by default | You had to remember to opt out. Now you opt in. | fails upstream |
| No per-caller identity, session, or audit subject | Every caller reached the database as the same account, and OAuth state was lost on restart. | new module |

**fails upstream** — the same test was run against the original checkout and
against the fork: it fails there and passes here.

**new module** — the test could not run against the original at all, which is a
weaker claim and is labelled as one. `scripts/verify_hardening.py` in the fork
regenerates the whole comparison.

---

## The part worth arguing about

In a MultiValue record, position *n* of every multi-valued field describes the
same thing — the same branch. Writing into a position a field does not have means
padding it, and a padded field is still a well-formed record: no error, every
later read agrees with it, and the branch alignment is quietly wrong.

So the one write path refuses to pad. It reads the record back and compares how
many values each field held before and after, and if any of them moved it reports
**failure** rather than success — because "written" would be true, and would
mislead.

That is the whole reason this was interesting to build. The trap is not that the
operation fails; it is that it succeeds.

---

## Run it against your own instance

The only thing here that would actually settle any of it. Nothing points at my
hosting or my API key.

```powershell
$env:U2_HOST     = 'your host'
$env:U2_USER     = 'a login on that machine'
$env:U2_PASSWORD = '...'
$env:U2_ACCOUNT  = 'YOUR.ACCOUNT'

.venv\Scripts\python scripts\try-it-here.py
```

That connects, **attempts a write in order to prove the refusal**, lists your
files and reads one dictionary. It prints a count of passed checks, or the exact
step that failed and what to do about it. Writes are off by default in the fork.

Point it at a restored copy the first time — not because it writes, but because
"point a new tool at production" deserves that answer regardless of who is
asking.

- The MCP server, forked and hardened: <https://github.com/mikejsmith1985/u2-mcp>
- This application: <https://github.com/mikejsmith1985/u2-counter>

---

## If you have not opened it yet

In four minutes it will show you, without being asked twice:

- An assistant answering in plain words, with **every call it made** underneath —
  the tool, the file, the key, and the record as stored.
- It reading a file nobody wrote code for: asking the account what it holds,
  reading that file's own dictionary, and querying it.
- Records with **real attribute and value marks**, parallel fields lined up by
  position, and a panel handing you the bytes to check somewhere else.
- A refusal, with its reason, when you ask it to delete something.

---

Built over one weekend, starting from no prior knowledge of MultiValue. 68
browser journeys, 71 front-end unit tests, 99 unit and 95 integration tests on
the API, 503 on the MCP server. The interesting number is none of those — it is
the ten defects, most of which were found by using the thing rather than by
reading it.
