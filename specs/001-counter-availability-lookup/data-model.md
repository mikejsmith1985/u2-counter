# Phase 1 Data Model: Counter Availability Lookup

**Date**: 2026-08-28
**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)

Two stores, with a hard line between them.

| Store | Holds | Written by this application |
| --- | --- | --- |
| MultiValue ERP | Parts, inventory, customers, orders, pricing, branches | **Never** |
| SQL Server | Sessions, preferences, the activity mirror | Yes |

The ERP is the system of record for everything a user asks about. SQL Server holds
only what the application itself needs to function.

---

## The shape that matters

MultiValue records use three separators, in a hierarchy:

| Mark | Character | Separates |
| --- | --- | --- |
| Attribute mark (AM) | `CHAR(254)` | Fields within a record |
| Value mark (VM) | `CHAR(253)` | Repeated values within a field |
| Subvalue mark (SM) | `CHAR(252)` | Sub-items within a value |

An inventory record carries **every branch's position for one part** as parallel
repeating fields. Position *n* in each field belongs to the same branch:

```text
INVENTORY record, key "SQD-QO120"

field 1  DEN ᵛ AUR ᵛ BOU ᵛ COS      ← branch
field 2  142 ᵛ  38 ᵛ   0 ᵛ  85      ← on hand
field 3   40 ᵛ  12 ᵛ   0 ᵛ   6      ← committed
field 4    0 ᵛ   0 ᵛ 200 ᵛ   0      ← on order from supplier
field 5 A-12 ᵛ C-04 ᵛ     ᵛ B-21    ← bin
```

Reading position 2 across the fields gives Aurora: 38 on hand, 12 committed, none
on order, bin C-04. One record, one read, no join.

**This correspondence is the integrity rule of the whole feature.** FR-011 forbids
a branch ever appearing against another branch's figures, and every defect that
could produce that outcome is a silent one — the screen still looks correct. Three
mechanisms guard it:

1. Parsing produces a single `BranchPosition` per index; the fields are never
   carried forward as separate lists that could be zipped wrongly.
2. A short field is padded to the length of field 1 rather than truncating the
   others, so a missing bin never shifts the branch it belongs to.
3. A record whose field 1 is shorter than any other field is rejected as
   malformed, because that means a quantity exists for a branch that was not
   named.

---

## ERP entities

### Part

**File**: `PRODUCT` · **Key**: part number

| Field | Meaning | Notes |
| --- | --- | --- |
| 1 | Description | |
| 2 | Manufacturer | |
| 3 | Manufacturer's own part number | |
| 4 | Unit of measure | `EA`, `FT`, `BOX`, `CTN` |
| 5 | Category code | Joins to pricing |
| 6 | List price | Currency, two decimals |
| 7 | Status | `A` active, `D` discontinued |

**Rules**

- The part number is the key and is unique.
- Search matches against the part number, description and manufacturer, ignoring
  case, spacing and punctuation (FR-002).
- A discontinued part is still shown, marked as discontinued; it is not hidden,
  because customers ask for parts that were discontinued last month.

### Inventory position

**File**: `INVENTORY` · **Key**: part number · **Parallel multivalues**

| Field | Meaning |
| --- | --- |
| 1 | Branch code |
| 2 | Quantity on hand |
| 3 | Quantity committed |
| 4 | Quantity on order from supplier |
| 5 | Bin location |

**Rules**

- `free to sell = max(0, on hand − committed)` (FR-008). Real ERP data does
  produce committed exceeding on-hand; a negative figure would be nonsense on a
  screen, so it is clamped rather than shown.
- Quantity on order is displayed but **never** added to free to sell: it is not
  stock, it is a promise from a supplier.
- A part with no `INVENTORY` record has *unknown* stock, which is a different
  state from zero and is displayed differently (edge case, FR-029).
- A branch code appearing here but absent from `BRANCH` is displayed as the bare
  code rather than dropped, so data no one has cleaned up still tells the truth.

### Branch

**File**: `BRANCH` · **Key**: branch code

| Field | Meaning |
| --- | --- |
| 1 | Name |
| 2 | City |
| 3 | Region code |
| 4 | Telephone |

### Customer

**File**: `CUSTOMER` · **Key**: account number

| Field | Meaning | Notes |
| --- | --- | --- |
| 1 | Name | |
| 2 | Address lines | Multivalued |
| 3 | Contact names | Multivalued |
| 4 | Contact telephone numbers | Multivalued, **subvalued by type** |
| 5 | Payment terms | |
| 6 | Price class | Joins to pricing |
| 7 | Home branch | |

Field 4 is the only place subvalues appear, and they are there deliberately: one
contact may have an office number and a mobile, so contact *n* holds its numbers
separated by subvalue marks. It gives the raw-record view a genuine three-level
example rather than a contrived one.

### Contract terms

**File**: `PRICING` · **Key**: price class + category code · **Parallel multivalues**

| Field | Meaning |
| --- | --- |
| 1 | Multiplier against list price |
| 2 | Effective from date |
| 3 | Effective to date |

**Rules**

- `net price = round(list price × multiplier, 2)`.
- Only terms effective on today's date apply (FR-015). Terms outside their window
  are disregarded, and the user is told they were.
- Where several terms are in force, the lowest multiplier applies — the customer
  gets their best agreed price.
- With no applicable terms, list price applies and is labelled as list.

### Order

**File**: `ORDER` · **Key**: order number · **Header plus parallel multivalued lines**

| Field | Meaning |
| --- | --- |
| 1 | Customer account number |
| 2 | Order date |
| 3 | Order state |
| 4 | Line: part number *(multivalued)* |
| 5 | Line: quantity *(multivalued)* |
| 6 | Line: branch fulfilling the line *(multivalued)* |
| 7 | Line: promised date *(multivalued)* |

**Order states**

| State | Holds stock | Meaning |
| --- | --- | --- |
| `QUOTE` | No | Priced, not promised |
| `CONFIRMED` | **Yes** | Customer has committed |
| `ALLOCATED` | **Yes** | Stock earmarked |
| `PICKING` | **Yes** | Being picked |
| `SHIPPED` | No | Stock has left |
| `CANCELLED` | No | Never taken |

```text
QUOTE ──▶ CONFIRMED ──▶ ALLOCATED ──▶ PICKING ──▶ SHIPPED
  │            │             │            │
  └────────────┴─────────────┴────────────┴──────▶ CANCELLED
```

**Rules**

- Only `CONFIRMED`, `ALLOCATED` and `PICKING` count toward committed quantity
  (FR-018).
- The state set is closed. An unrecognised state is treated as **not** holding
  stock and is logged, so a typo in data can never inflate what appears committed.
- Commitments listed for a branch must account for its whole committed quantity
  (FR-019). Where they do not, the shortfall is shown as an explicit
  "unaccounted" row rather than silently omitted — a discrepancy the user can see
  beats one they cannot.

---

## Application entities (SQL Server)

### UserSession

| Column | Type | Meaning |
| --- | --- | --- |
| `Id` | `uniqueidentifier` | Primary key |
| `UserSubject` | `nvarchar(200)` | Identity from sign-in |
| `DisplayName` | `nvarchar(200)` | Shown in the governance strip |
| `HomeBranchCode` | `nvarchar(10)` | Which branch's figures lead |
| `SelectedCustomerAccount` | `nvarchar(20)` | Nullable; survives navigation (FR-012) |
| `CreatedAt` / `LastSeenAt` | `datetimeoffset` | |

### ActivityRecord

Mirrors what the MCP server records, so FR-026 can be satisfied without exposing
the server's own log files to the browser.

| Column | Type | Meaning |
| --- | --- | --- |
| `Id` | `bigint` identity | Primary key |
| `OccurredAt` | `datetimeoffset` | Indexed descending |
| `UserSubject` / `DisplayName` | `nvarchar(200)` | Who acted |
| `Action` | `nvarchar(100)` | What was asked for |
| `TargetKey` | `nvarchar(100)` | Part number, account number |
| `DatabaseLogin` | `nvarchar(100)` | Which ERP account served it |
| `DatabaseLoginIsShared` | `bit` | FR-024 |
| `DurationMs` | `int` | |
| `Outcome` | `nvarchar(20)` | `Success`, `NotFound`, `Unreachable`, `Refused` |

**Rules**

- Every request writes exactly one record, including failures (FR-036).
- No column may hold a credential (FR-027), enforced by an integration test that
  scans every written record against the seeded passwords.
- `Unreachable` is a distinct outcome from `NotFound`, which is what makes FR-029
  auditable rather than merely visible.

### Not stored

Deliberately absent, and each for a reason:

- **No cached stock figures.** FR-035 forbids serving a previous reading; storing
  one creates the temptation and the bug.
- **No mirrored catalogue.** The searchable projection is built in memory at
  startup and rebuilt on demand, so it cannot drift silently in a table.
- **No customer or pricing copy.** The ERP is the system of record; a second copy
  is a second answer.
