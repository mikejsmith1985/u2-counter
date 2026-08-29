# Contract: REST API

**Base path**: `/api/v1` · **Format**: JSON, UTF-8 · **Auth**: session cookie

Every endpoint here is a read. No verb other than `GET` exists in this API, and an
integration test asserts that no route is registered for `POST`, `PUT`, `PATCH` or
`DELETE` against ERP data (FR-022).

---

## Cross-cutting rules

### Failure shapes

Errors use `application/problem+json` (RFC 9457). The `type` is what the client
switches on; the `detail` is written for a person to read aloud.

| Status | `type` | When | Client shows |
| --- | --- | --- | --- |
| `400` | `invalid-request` | Malformed parameter | Field-level message |
| `404` | `not-found` | The key does not exist | "No such part" |
| `502` | `erp-refused` | The ERP refused the request | "The system declined that request" |
| `504` | `erp-unreachable` | No answer within the budget | **Unreachable state with retry** |

`504` versus an empty `200` is the whole of FR-029. A reachability failure that
arrives as an empty success is indistinguishable from "no stock", and the user
would tell a customer the wrong thing. The client MUST NOT render `504` as an
empty result.

### Timing

Every ERP-backed request carries a five-second budget (FR-033). On expiry the API
cancels the work — not merely stops waiting for it — and returns `504`.

### Truthfulness envelope

Any response that could be partial carries:

```json
{
  "isComplete": true,
  "warning": null,
  "isDemonstrationData": true,
  "retrievedAt": "2026-08-28T21:14:07Z"
}
```

`isComplete: false` with a populated `warning` means a limit was applied (FR-030).
`isDemonstrationData` is always `true` in this deployment and is rendered wherever
figures appear (FR-032).

---

## `GET /api/v1/parts?q={text}&limit={n}`

Search the catalogue. Answers from the in-memory projection; touches no ERP data.

**Query**: `q` required, 1–100 characters. `limit` optional, default 20, maximum
50.

**200**

```json
{
  "results": [
    {
      "partNumber": "SQD-QO120",
      "description": "QO 20A 1-Pole Circuit Breaker",
      "manufacturer": "Square D",
      "unitOfMeasure": "EA",
      "isDiscontinued": false,
      "totalFreeToSell": 265
    }
  ],
  "isComplete": true,
  "warning": null,
  "isDemonstrationData": true,
  "retrievedAt": "2026-08-28T21:14:07Z"
}
```

`totalFreeToSell` is summed live across branches, so this endpoint does touch the
ERP for the parts it returns — which is why `limit` is capped.

An empty `results` array with `200` means nothing matched (FR-005). It never means
the ERP was unreachable.

---

## `GET /api/v1/parts/{partNumber}/availability?customerAccount={account}`

The money screen. Everything Story 1 and Story 2 need, in one call — because two
calls means two chances to show half an answer.

**200**

```json
{
  "part": {
    "partNumber": "SQD-QO120",
    "description": "QO 20A 1-Pole Circuit Breaker",
    "manufacturer": "Square D",
    "unitOfMeasure": "EA",
    "isDiscontinued": false
  },
  "stockIsKnown": true,
  "totalFreeToSell": 265,
  "branches": [
    {
      "branchCode": "DEN",
      "branchName": "Denver",
      "addressLine": "Denver",
      "onHand": 142,
      "committed": 40,
      "freeToSell": 102,
      "onOrder": 0,
      "bin": "A-12",
      "stockState": "Available"
    }
  ],
  "pricing": {
    "listPrice": 12.40,
    "netPrice": 8.68,
    "multiplier": 0.70,
    "basis": "Contract",
    "termsDescription": "Price class C2, category BRK, effective to 2026-12-31",
    "disregardedTerms": [
      { "termsDescription": "Promotional, expired 2026-06-30", "reason": "Not effective today" }
    ]
  },
  "isComplete": true,
  "warning": null,
  "isDemonstrationData": true,
  "retrievedAt": "2026-08-28T21:14:07Z"
}
```

**`stockState`** — one of `Available`, `AllCommitted`, `None`. Three states, not a
boolean, because "we have twelve but they are all spoken for" is a different
answer to a customer than "we have none" (FR-009).

**`stockIsKnown: false`** means the part exists but has no inventory record. The
`branches` array is then empty and the client says stock information is
unavailable — not that stock is zero.

**`pricing.basis`** — `Contract` or `List`. With no `customerAccount`, `basis` is
`List`, `multiplier` is `null`, and the client prompts for a customer (FR-014).

**`disregardedTerms`** exists to satisfy FR-015 visibly: the user is told which
terms were *not* applied and why, so an unexpectedly high price can be explained
on the call rather than escalated.

---

## `GET /api/v1/parts/{partNumber}/commitments?branchCode={code}`

What is holding the stock at one branch (Story 3).

**200**

```json
{
  "branchCode": "DEN",
  "committedTotal": 40,
  "accountedFor": 40,
  "commitments": [
    {
      "orderNumber": "SO-104882",
      "customerName": "Front Range Electric",
      "quantity": 25,
      "state": "ALLOCATED",
      "promisedDate": "2026-09-02"
    }
  ],
  "unaccounted": 0,
  "isComplete": true,
  "isDemonstrationData": true,
  "retrievedAt": "2026-08-28T21:14:07Z"
}
```

`unaccounted` is `committedTotal − accountedFor`, and it is in the contract rather
than hidden because FR-019 requires the listed commitments to account for the
whole committed quantity. When they do not, the client shows the shortfall as its
own row. A visible discrepancy is worth more than a tidy screen.

An empty `commitments` array with `committedTotal: 0` means nothing is committed,
which the client states in words (FR-017).

---

## `GET /api/v1/parts/{partNumber}/record`

The raw record drawer (Story 4).

**200**

```json
{
  "fileName": "INVENTORY",
  "recordId": "SQD-QO120",
  "rawRecord": "DENýAURýBOUþ142ý38ý0þ40ý12ý0",
  "marks": [
    { "character": "þ", "code": 254, "name": "Attribute mark", "separates": "Fields" },
    { "character": "ý", "code": 253, "name": "Value mark", "separates": "Values within a field" },
    { "character": "ü", "code": 252, "name": "Subvalue mark", "separates": "Sub-items within a value" }
  ],
  "parsed": { "1": ["DEN", "AUR", "BOU"], "2": ["142", "38", "0"], "3": ["40", "12", "0"] },
  "query": "LIST INVENTORY SQD-QO120",
  "isDemonstrationData": true,
  "retrievedAt": "2026-08-28T21:14:07Z"
}
```

`rawRecord` carries the delimiters **as stored**, not stripped and not
pre-rendered. The `marks` array tells the client how to display them visibly and
label them (FR-021). Rendering is the client's job; the contract's job is not to
lie about what the record contains.

---

## `GET /api/v1/customers?q={text}`

Customer selector for the header (FR-012). Same envelope; returns account number,
name, city and price class.

---

## `GET /api/v1/session`

**200**

```json
{
  "displayName": "Dana Whitfield",
  "userSubject": "demo|dana",
  "homeBranchCode": "DEN",
  "selectedCustomerAccount": "C-10442",
  "isReadOnly": true,
  "databaseLogin": "u2demo@DEMO",
  "databaseLoginIsShared": true,
  "isDemonstrationData": true
}
```

Feeds the governance strip (FR-025, FR-026). `databaseLoginIsShared` is surfaced
to the user rather than kept in the log, because a shared login is a limitation
the person acting under it should know about.

---

## `PUT /api/v1/session/customer`

The single exception to "no verb but `GET`" — and it writes to the application's
own session, never to the ERP. Body: `{ "customerAccount": "C-10442" }`. Returns
the updated session.

---

## `GET /api/v1/activity?limit={n}`

Recent activity for the current user (FR-026). Default 20, maximum 100. Returns
the `ActivityRecord` fields from the data model. Never returns another user's
activity, and never a credential.

---

## Client obligations

The contract constrains the client too, because these are the rules a well-formed
response can still be rendered wrongly against:

1. `504` renders as **unreachable with a retry**, never as empty (FR-029, FR-034).
2. `stockIsKnown: false` renders as **unknown**, never as zero.
3. `isComplete: false` renders the `warning` prominently (FR-030).
4. `unaccounted > 0` renders as its own row (FR-019).
5. Every screen showing figures shows `isDemonstrationData` (FR-032).
6. Copied text carries `retrievedAt` and the demonstration marker (FR-034).
