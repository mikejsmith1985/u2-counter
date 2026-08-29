# Contract: MCP server usage

How the API talks to the hardened `u2-mcp` fork, and — more importantly — what it
must never ask for.

**Transport**: Streamable HTTP, over the container environment's internal network.
The MCP server has no public ingress, because the fork refuses to serve
unauthenticated traffic on a reachable interface and this deployment does not put
an identity provider in front of it.

---

## Tools the API may call

| Tool | Used for | Arguments |
| --- | --- | --- |
| `read_record` | One inventory, product, customer or order record | `file_name`, `record_id` |
| `read_records` | The catalogue at startup, in batches | `file_name`, `record_ids` |
| `get_select_list` | Selecting the orders that reference a part | `query`, `max_ids` |

That is the whole list, and it is checked: `ErpTools.Permitted` holds exactly
these three, and a test fails the build if any of them stops having a caller.

`execute_query` was on this list and has been removed. It ran arbitrary query
text, and nothing called it — every question this application asks is a keyed
read or a parameterised selection. Of all the permissions to leave dangling, the
arbitrary-query one is the worst, and it survived because the test meant to catch
it searched the reader's source for the constant name: the constant sat in a
method nobody invoked, so the check passed. The test now looks for callers.

## Tools the API must never call

| Tool | Why |
| --- | --- |
| `write_record`, `delete_record` | The feature is read-only (FR-022). Their absence is asserted by test, not left to discipline |
| `execute_tcl` | System-level commands are not needed to answer any question in this specification. The blocklist protects the server; not calling it at all protects us from needing the blocklist to be perfect |
| `call_subroutine` | Runs business logic this feature does not use |
| `begin_transaction`, `commit_transaction`, `rollback_transaction` | Nothing is written, so nothing needs a transaction |
| `save_knowledge`, `delete_knowledge` | Writes to the server's own store; not this feature's concern |

An integration test asserts that no name in this table appears anywhere in the
infrastructure source outside the single file that declares them. That is a
source scan rather than a check on the compiled assembly, and deliberately so:
string constants are inlined, so a scan of the assembly finds the permitted and
the forbidden names mixed together with no way to tell which came from a call.

A blocklist that lives only in a document is a comment; one that fails a build is
a control. This one is a control, and a reviewer can run the same grep by hand.

**This table is not the server's whole surface.** The fork exposes more tools than
appear on either list here — exports, catalog listings, account information. They
are not called and not permitted, and `ErpTools.Permitted` is what the code
enforces: anything absent from it is refused by the reader before a request is
made, whether or not it is named below.

---

## Query patterns

Only two query shapes are ever sent, both through `get_select_list`, and both
returning keys rather than formatted output — a `LIST` renders records for a
person to read, turning the separators into newlines and losing the structure
they carried.

Both start with an allowlisted read verb, and both are built from parameters
validated first: a part number matched against the catalogue projection, a branch
code matched against `BRANCH`. Nothing typed by a user is concatenated into a
query.

**Every product, at startup**

```text
SELECT PRODUCT
```

**Orders holding stock for a part**

```text
SELECT ORDER WITH F4 = "SQD-QO120" AND WITH F3 = "CONFIRMED" "ALLOCATED" "PICKING"
```

Fields are named by position rather than by dictionary name, because the
demonstration store holds no dictionaries — inventing one would be a second
description of the record layout to keep in step with the first. Against a real
UniVerse account with dictionaries, these become `PART.NUMBER` and `STATE`.

The state filter is applied in the query rather than after retrieval so that the
rule from FR-018 lives in one place. Filtering afterwards would leave two
opportunities to get it wrong and no test that they agree.

---

## Reading the response envelope

The fork's responses carry fields this API must not discard:

| Field | Obligation |
| --- | --- |
| `is_complete` | Propagated to the client as `isComplete`. Never assumed true |
| `warning` | Propagated verbatim. Never summarised away |
| `error` | Distinguished from an empty result. An error is never rendered as "none found" |

FR-029 and FR-030 are only satisfiable if these survive the trip. The API adds no
interpretation of its own: if the server says the answer may be partial, so does
the screen.

---

## Identity

**The API does not send the signed-in user's subject to the fork.** This section
said it did; it does not, and the distinction changes where the audit trail comes
from.

`ErpReader` sends tool arguments and nothing else — no header, no subject, no
credential. Every call reaches the fork as the same shared database login. So the
record that names a person is written by the API, into `ActivityRecord`, and the
governance strip shows it. The fork's own log can say only that `u2demo` asked.

That is the honest arrangement for a deployment with one shared ERP account, and
it is why the API keeps an audit trail at all. Passing the subject through to the
fork — so its `mapped` identity mode could give each person their own database
login — is the next thing this would need in production, and it is not built.

In this deployment the ERP account is shared — there is one demonstration login —
and the application says so plainly rather than letting the shared account read as
a per-user one. The fork's `mapped` identity mode exists for deployments that give
each person their own database login; this one does not, and pretending otherwise
would be the exact defect the fork was hardened against.

---

## Failure handling

| Condition | API behaviour |
| --- | --- |
| No answer within five seconds | Cancel the request; return `504 erp-unreachable` |
| Server returns an error | Return `502 erp-refused` with the server's message |
| Record not found | Return `404 not-found` |
| Result marked incomplete | Return `200` with `isComplete: false` and the warning |

The five-second budget is enforced on the API side as well as the server side.
Two independent timeouts are deliberate: the server's protects the database from
an abandoned query, and the API's protects the user from an unbounded wait. Either
one alone leaves a gap the other covers.
