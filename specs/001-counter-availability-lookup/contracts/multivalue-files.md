# Contract: MultiValue file layouts

How the demonstration store holds each ERP file. This is a contract because two
components depend on it independently — the seed generator writes it, and the API
parses it — and because the raw-record view shows it to a user, which means a
mistake here is visible rather than merely wrong.

## Separators

| Mark | Code | Written here as | Separates |
| --- | --- | --- | --- |
| Attribute mark | `CHAR(254)` | `<AM>` | Fields |
| Value mark | `CHAR(253)` | `<VM>` | Values within a field |
| Subvalue mark | `CHAR(252)` | `<SM>` | Sub-items within a value |

Records are stored with the real characters. `<AM>` notation appears only in this
document, and never in stored data or in a response.

## Rules that apply to every file

1. **Fields are one-based.** Field 1 is the first field after the key. The key is
   not a field.
2. **Trailing empty fields are not stored.** A record ending in three empty fields
   is stored without them, exactly as a real system would, so parsers must treat a
   missing trailing field as empty rather than an error.
3. **Interior empty fields are preserved**, because dropping one would shift every
   field after it.
4. **Parallel fields align by position.** Where a file declares fields parallel,
   value *n* of each belongs to the same subject.
5. **A short parallel field is padded, never truncated.** If field 5 has three
   values where field 1 has four, the fourth is empty. Truncating the others
   instead would silently drop a branch.
6. **Dates are `YYYY-MM-DD`.** Universe's internal date format is deliberately not
   used: it would add a conversion whose failure mode is a wrong date on screen,
   for no benefit to this feature.
7. **Amounts are decimal strings**, two places, no currency symbol and no
   thousands separator.

---

## `PRODUCT`

**Key**: part number, uppercase, e.g. `SQD-QO120`

| Field | Contents | Example |
| --- | --- | --- |
| 1 | Description | `QO 20A 1-Pole Circuit Breaker` |
| 2 | Manufacturer | `Square D` |
| 3 | Manufacturer part number | `QO120` |
| 4 | Unit of measure | `EA` |
| 5 | Category code | `BRK` |
| 6 | List price | `12.40` |
| 7 | Status — `A` active, `D` discontinued | `A` |

```text
QO 20A 1-Pole Circuit Breaker<AM>Square D<AM>QO120<AM>EA<AM>BRK<AM>12.40<AM>A
```

---

## `INVENTORY`

**Key**: part number · **Fields 1–5 are parallel**

| Field | Contents |
| --- | --- |
| 1 | Branch code |
| 2 | Quantity on hand |
| 3 | Quantity committed |
| 4 | Quantity on order from supplier |
| 5 | Bin location |

```text
DEN<VM>AUR<VM>BOU<AM>142<VM>38<VM>0<AM>40<VM>12<VM>0<AM>0<VM>0<VM>200<AM>A-12<VM>C-04<VM>
```

Boulder holds nothing, has nothing committed, has 200 on order, and has no bin —
the trailing value of field 5 is empty and must stay empty rather than shortening
the field.

**Validation**

- Field 1 MUST be at least as long as every other field. A quantity for an unnamed
  branch is malformed and the record is rejected.
- Branch codes MUST NOT repeat within a record.
- Quantities MUST be non-negative integers. Committed MAY exceed on hand; the
  application clamps free-to-sell at zero rather than the store rejecting it,
  because real systems do produce this.

---

## `BRANCH`

**Key**: branch code, e.g. `DEN`

| Field | Contents | Example |
| --- | --- | --- |
| 1 | Name | `Denver` |
| 2 | City | `Denver` |
| 3 | Region code | `CO-FRONT` |
| 4 | Telephone | `303-555-0142` |

---

## `CUSTOMER`

**Key**: account number, e.g. `C-10442`

| Field | Contents | Structure |
| --- | --- | --- |
| 1 | Name | Single |
| 2 | Address lines | Multivalued |
| 3 | Contact names | Multivalued |
| 4 | Contact telephone numbers | Multivalued, **subvalued by type** |
| 5 | Payment terms | Single |
| 6 | Price class | Single |
| 7 | Home branch code | Single |

```text
Front Range Electric<AM>1450 Wazee St<VM>Suite 200<AM>Ray Delgado<VM>Marta Quinn<AM>303-555-0188<SM>720-555-0913<VM>303-555-0190<AM>NET30<AM>C2<AM>DEN
```

Fields 3 and 4 are parallel: contact *n* owns the telephone numbers at position
*n* of field 4. Ray Delgado has two numbers separated by a subvalue mark; Marta
Quinn has one. This is the only subvalued field in the data set, and it exists so
the raw-record view demonstrates all three levels on real data rather than a
contrived example.

---

## `PRICING`

**Key**: price class, then `*`, then category code, e.g. `C2*BRK` ·
**Fields 1–3 are parallel**

| Field | Contents |
| --- | --- |
| 1 | Multiplier against list price |
| 2 | Effective from |
| 3 | Effective to |

```text
0.70<VM>0.62<AM>2026-01-01<VM>2026-05-01<AM>2026-12-31<VM>2026-06-30
```

Two sets of terms: a standing one at 0.70 through the year, and a promotion at
0.62 that expired on 30 June. The application applies the first and reports the
second as disregarded, which is what makes FR-015 demonstrable rather than
theoretical.

**Validation**

- Multipliers MUST be greater than zero and no greater than one.
- The from date MUST NOT be after the to date.
- Overlapping windows are permitted; the lowest applicable multiplier wins.

---

## `ORDER`

**Key**: order number, e.g. `SO-104882` · **Fields 4–7 are parallel line items**

| Field | Contents | Structure |
| --- | --- | --- |
| 1 | Customer account number | Single |
| 2 | Order date | Single |
| 3 | Order state | Single |
| 4 | Part number | Multivalued |
| 5 | Quantity | Multivalued |
| 6 | Fulfilling branch | Multivalued |
| 7 | Promised date | Multivalued |

```text
C-10442<AM>2026-08-24<AM>ALLOCATED<AM>SQD-QO120<VM>SQD-QO220<AM>25<VM>10<AM>DEN<VM>DEN<AM>2026-09-02<VM>2026-09-02
```

**Validation**

- Field 3 MUST be one of `QUOTE`, `CONFIRMED`, `ALLOCATED`, `PICKING`, `SHIPPED`,
  `CANCELLED`. Anything else is treated as not holding stock and is logged.
- Fields 4 to 7 MUST be the same length.
- Quantities MUST be positive.

---

## Seed data obligations

The generated data set MUST contain, so that each rule is exercised rather than
merely written down:

| Condition | Why |
| --- | --- |
| A part stocked at every branch, and one stocked at a single branch | Both ends of the grid |
| A part with no `INVENTORY` record | Distinguishes unknown from zero |
| A branch with stock entirely committed | `AllCommitted` state (FR-009) |
| A branch where committed exceeds on hand | Free-to-sell clamped at zero (FR-008) |
| A branch code absent from `BRANCH` | Displayed as a bare code, not dropped |
| A customer with lapsed and current terms | Disregarded terms are visible (FR-015) |
| A customer with no terms at all | List-price path (FR-014) |
| Quotations and shipped orders against a committed part | The excluded states are visibly excluded (FR-018) |
| A branch whose commitments do not account for its committed total | `unaccounted` row (FR-019) |
| Names with accented characters and a currency symbol in a description | Text survives intact (FR-031) |
| A discontinued part with stock | Shown, marked discontinued |
