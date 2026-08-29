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
| `read_records` | A batch of orders holding stock at a branch | `file_name`, `record_ids` |
| `execute_query` | Selecting orders that reference a part | `query`, `max_rows` |

That is the whole list. Anything absent from it is absent by decision.

## Tools the API must never call

| Tool | Why |
| --- | --- |
| `write_record`, `delete_record` | The feature is read-only (FR-022). Their absence is asserted by test, not left to discipline |
| `execute_tcl` | System-level commands are not needed to answer any question in this specification. The blocklist protects the server; not calling it at all protects us from needing the blocklist to be perfect |
| `call_subroutine` | Runs business logic this feature does not use |
| `begin_transaction`, `commit_transaction`, `rollback_transaction` | Nothing is written, so nothing needs a transaction |
| `save_knowledge`, `delete_knowledge` | Writes to the server's own store; not this feature's concern |

An integration test asserts that the MCP client exposes no method binding to any
tool in this table. A blocklist that lives only in a document is a comment; one
that fails a build is a control.

---

## Query patterns

Only two query shapes are ever sent. Both start with an allowlisted read verb, and
both are built from parameters that have been validated first — a part number
matched against the catalogue projection, a branch code matched against `BRANCH`.
Nothing typed by a user is concatenated into a query.

**Orders holding stock for a part**

```text
SELECT ORDER WITH PART.NUMBER = "SQD-QO120" AND WITH STATE = "CONFIRMED" "ALLOCATED" "PICKING"
```

**Counting matches for a part**

```text
COUNT ORDER WITH PART.NUMBER = "SQD-QO120"
```

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

The API sends the signed-in user's subject with every call, so the fork's audit
trail names the person rather than the service. The fork records the database
login and whether it was shared; the API mirrors both into `ActivityRecord` and
surfaces them in the governance strip.

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
