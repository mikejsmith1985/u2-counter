# Feature Specification: Counter Availability Lookup

**Feature Branch**: `001-counter-availability-lookup`

**Created**: 2026-08-28

**Status**: Draft

**Input**: User description: "A counter-sales lookup for an electrical distributor. A customer phones a branch asking for a quantity of a part today. The person answering must say, within about thirty seconds, whether the part is available, at which branches, what this customer pays for it, and what quantity is already committed to other orders. Stock is held in a MultiValue ERP where one inventory record carries every branch's position in parallel repeating fields."

## Clarifications

### Session 2026-08-28

- Q: What interaction model and accessibility standard must the application meet? → A: Keyboard-first and WCAG 2.1 AA — every action reachable by keyboard, visible focus, AA contrast, screen-reader labels on data.
- Q: How large should the demonstration data set be? → A: Realistic-small — approximately 3,000 parts, 12 branches, 150 customers, 800 open orders.
- Q: Can a user take the answer out of the application, and how? → A: Copy to clipboard — one action copies a plain-text summary of the part, its availability, and the customer's net price. No file export or email.
- Q: Which order states hold stock against a branch's on-hand quantity? → A: Confirmed through picking — orders that are confirmed, allocated or being picked hold stock; quotations, shipped and cancelled orders do not.
- Q: What should the application do when the ERP does not answer promptly? → A: Fail fast and say so — give up after a few seconds and tell the user the data could not be reached, with a retry. Never serve stale figures.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Answer "have you got it?" (Priority: P1)

A counter representative is on the phone with a customer who names a part and a
quantity. The representative types the part number, or part of its description,
into a single search box and opens the first matching result. Within one screen
they can see how many units are free to sell at their own branch, and at every
other branch in the region, without scrolling.

**Why this priority**: This is the entire reason the feature exists. Every other
story on this list only matters once this question can be answered. On its own it
is already a usable product: a representative who can answer availability
correctly and quickly has replaced the majority of what they currently phone
around for.

**Independent Test**: Give a representative a part number and a branch. They can
state the free-to-sell quantity at that branch, and name the nearest branch that
could fill the order, using only this screen.

**Acceptance Scenarios**:

1. **Given** a part that is stocked at several branches, **When** the
   representative opens it, **Then** every stocking branch is listed with its
   on-hand, committed, and free-to-sell quantities.
2. **Given** a part with no stock anywhere, **When** the representative opens it,
   **Then** the screen states plainly that nothing is available, rather than
   showing an empty grid.
3. **Given** a part number typed with different punctuation or casing than the
   catalogue uses, **When** the representative searches, **Then** the part is
   still found.
4. **Given** a search that matches nothing, **When** the representative searches,
   **Then** they are told nothing matched and what they could try instead.
5. **Given** a branch holding stock that is entirely committed to other orders,
   **When** the representative views it, **Then** the free-to-sell figure reads
   zero and is visually distinct from a branch with stock available.

---

### User Story 2 - Quote the right price for this customer (Priority: P2)

Before quoting, the representative selects the customer they are speaking to.
The part screen then shows what that specific customer pays: the list price, the
contract terms that apply to them, and the resulting net price, presented
together so the representative can see how the number was arrived at and repeat
it aloud with confidence.

**Why this priority**: Availability without price answers only half the call, and
quoting list price to a contract customer is a commercial error that is expensive
to unwind. It is second because availability alone is still useful, whereas price
alone is not.

**Independent Test**: Select a customer with contract terms and a part covered by
those terms. The representative can state the net price and explain which
contract produced it.

**Acceptance Scenarios**:

1. **Given** a customer with contract terms covering a part, **When** the
   representative views that part, **Then** list price, the applied terms, and
   the net price are all shown together.
2. **Given** a customer with no special terms, **When** the representative views a
   part, **Then** the list price is shown and identified as list.
3. **Given** no customer has been selected, **When** the representative views a
   part, **Then** list price is shown, labelled as list, with a clear prompt to
   select a customer for contract pricing.
4. **Given** a customer whose contract has lapsed, **When** the representative
   views a covered part, **Then** the lapsed terms are not applied and the reason
   is stated.

---

### User Story 3 - See what the committed stock is promised to (Priority: P3)

When a branch shows stock on hand but little free to sell, the representative can
see which orders are holding the difference, including the customer, the
quantity, the order's state, and the promised date. This lets them tell the caller
when stock frees up, or judge whether another order could reasonably be moved.

**Why this priority**: It converts a "no" into a "not until Thursday", which is a
materially better answer for the customer. It depends on availability already
being visible, so it comes third.

**Independent Test**: Open a part whose stock is partly committed. The
representative can name the orders holding it and the date the stock is expected
to free up.

**Acceptance Scenarios**:

1. **Given** a branch with committed stock, **When** the representative expands
   that branch, **Then** each committed order is listed with customer, quantity,
   and promised date.
2. **Given** a branch with nothing committed, **When** the representative expands
   it, **Then** they are told there are no commitments rather than shown an empty
   list.

---

### User Story 4 - Show the record as the ERP holds it (Priority: P4)

From any part, the representative or a technical colleague can reveal the
underlying inventory record exactly as the ERP stores it, alongside the
structured form the screen is built from, and the query that retrieved it.

**Why this priority**: It is not needed to answer a customer call, so it ranks
last for the counter. It earns its place because it is how anyone maintaining the
system verifies that nothing was lost or invented in translation, and how a
newcomer to MultiValue data learns to read it.

**Independent Test**: Open the record view for a part stocked at several branches
and confirm that each branch's position appears in the raw record in the same
order as on the screen.

**Acceptance Scenarios**:

1. **Given** any part, **When** the record view is opened, **Then** the raw record
   and the structured form are shown side by side.
2. **Given** the raw record, **When** it is displayed, **Then** the separators
   between fields and between repeated values are visible and labelled rather
   than hidden or stripped.
3. **Given** the record view, **When** it is opened, **Then** the query that
   produced the data is shown alongside it.

---

### User Story 5 - Know who is asking and what they may do (Priority: P5)

Everyone using the system can see, at all times, who they are signed in as, which
database account their requests run under, and whether the session can change
data. Anyone reviewing activity afterwards can see who ran what, and when.

**Why this priority**: It changes no answer given to a customer, which is why it
is last. It is included because a lookup tool that reads live commercial data
cannot be adopted without it, and it is far cheaper to build in now than to add
after a review demands it.

**Independent Test**: Sign in, perform several lookups, and confirm that the
activity record names the person, the database account, and each action taken.

**Acceptance Scenarios**:

1. **Given** a signed-in user, **When** any screen is shown, **Then** their
   identity and the read-only state of the session are visible without navigating
   away.
2. **Given** a session that cannot change data, **When** the user views any
   screen, **Then** no control that would change data is offered.
3. **Given** several completed lookups, **When** the activity record is reviewed,
   **Then** each entry names the person, the action, the time, and how long it
   took.
4. **Given** a database account shared by more than one person, **When** activity
   is reviewed, **Then** each entry states that the account was shared.

---

### Edge Cases

- A part exists in the catalogue but has no inventory record at all — the part is
  shown with a stated absence of stock information, not treated as out of stock.
- A branch appears in the inventory record but is not in the branch list — the
  branch code is shown as-is rather than the row being dropped.
- Committed quantity exceeds on-hand quantity, which happens in real ERP data —
  free-to-sell is reported as zero and never as a negative number.
- Two parts match a search equally well — both are shown; the system does not
  guess.
- A customer name or address contains accented characters or currency symbols —
  they display correctly rather than being stripped.
- A single part is stocked at an unusually large number of branches — all are
  shown, and the screen remains readable.
- The ERP is unreachable or slow to answer — the request is abandoned after a few
  seconds and the user is told the data could not be reached, distinguishable from
  a genuine "no stock" answer, and never replaced with an earlier reading.
- A result set is capped for size — the user is told the answer may be partial
  rather than being shown a truncated list presented as complete.
- Two people look up the same part at the same moment — both receive correct
  answers.

## Requirements *(mandatory)*

### Functional Requirements

**Finding a part**

- **FR-001**: Users MUST be able to find a part by its part number, by words from
  its description, or by manufacturer, from a single input.
- **FR-002**: The system MUST match part numbers regardless of casing, spacing, or
  punctuation differences between what is typed and what the catalogue holds.
- **FR-003**: The system MUST show results as the user types, without requiring a
  separate action to run the search.
- **FR-004**: Each result MUST show enough to choose between candidates: part
  number, description, manufacturer, and total quantity free to sell across all
  branches.
- **FR-005**: The system MUST state clearly when a search matched nothing, and
  suggest what to try instead.

**Availability**

- **FR-006**: The system MUST show, for one part, every branch holding an
  inventory position for it.
- **FR-007**: For each branch the system MUST show quantity on hand, quantity
  committed, quantity free to sell, and the storage location.
- **FR-008**: The system MUST calculate free-to-sell as on-hand less committed,
  and MUST report zero rather than a negative number when commitments exceed
  stock.
- **FR-009**: The system MUST distinguish, at a glance, branches with stock
  available, branches with stock but none free, and branches with no stock.
- **FR-010**: The system MUST show the total free to sell across all branches.
- **FR-011**: The system MUST preserve the correspondence between a branch and its
  quantities; a branch MUST never be shown against another branch's figures.

**Pricing**

- **FR-012**: Users MUST be able to select the customer they are serving, and
  change that selection without losing the part they are viewing.
- **FR-013**: When a customer is selected, the system MUST show the price that
  customer pays, together with the list price and the terms that produced it.
- **FR-014**: When no customer is selected, the system MUST show the list price
  and identify it as such.
- **FR-015**: The system MUST NOT apply contract terms that are not in effect on
  the current date, and MUST say when terms were disregarded for that reason.

**Commitments**

- **FR-016**: Users MUST be able to see, for a branch with committed stock, the
  orders holding it, with customer, quantity, order state, and promised date.
- **FR-017**: The system MUST state when a branch has no commitments, rather than
  showing an empty list.
- **FR-018**: The system MUST treat an order as holding stock only while it is
  confirmed, allocated, or being picked. Quotations hold nothing, because a quote
  is not a promise; shipped and cancelled orders hold nothing, because the stock
  has either left or was never taken.
- **FR-019**: The commitments listed for a branch MUST account for that branch's
  entire committed quantity, so that a user can see what the whole of it is
  promised to.

**Showing the underlying record**

- **FR-020**: Users MUST be able to view the inventory record as the ERP stores
  it, alongside the structured form the screen uses.
- **FR-021**: The system MUST render the separators within the stored record
  visibly and label them, rather than hiding or removing them.
- **FR-022**: The system MUST show the query that retrieved the data being
  displayed.

**Identity, permission and record-keeping**

- **FR-023**: The system MUST show the signed-in user, the database account in
  use, and whether the session may change data, on every screen.
- **FR-024**: The system MUST NOT offer any control that would change data when
  the session is read-only.
- **FR-025**: The system MUST record every request against the person who made it,
  the database account used, the time, and the duration.
- **FR-026**: The system MUST record whether the database account used was shared
  by more than one person.
- **FR-027**: The system MUST NOT record credentials of any kind.
- **FR-028**: Users MUST be able to review recent recorded activity from within
  the application.

**Truthfulness of answers**

- **FR-029**: The system MUST distinguish "there is no stock" from "the stock
  position could not be retrieved", and never present the second as the first.
- **FR-030**: When results have been limited for size, the system MUST say so
  rather than presenting a partial answer as complete.
- **FR-031**: The system MUST display text in any alphabet, including accented
  characters and currency symbols, without alteration.
- **FR-032**: All demonstration data MUST be identified as demonstration data
  wherever it is displayed.

**When the data cannot be reached**

- **FR-033**: The system MUST abandon a request that the ERP has not answered
  within a few seconds, and tell the user it could not reach the data. A
  representative on a live call can excuse themselves and ring back; what they
  cannot do is wait, or repeat a figure they are unsure of.
- **FR-034**: The failure message MUST offer an immediate retry.
- **FR-035**: The system MUST NOT show previously retrieved figures in place of an
  answer it could not obtain. Stock quoted from a stale reading is worse than no
  answer, because the caller acts on it.
- **FR-036**: A failure to reach the data MUST be recorded in the activity record
  in the same way as a successful request.

**Carrying the answer elsewhere**

- **FR-037**: Users MUST be able to copy a plain-text summary of what they are
  looking at — the part, its availability by branch, and the selected customer's
  net price — to the clipboard in a single action, so it can be pasted into an
  order, an email or a note without retyping.
- **FR-038**: The copied summary MUST identify itself as demonstration data and
  carry the time it was taken, so a figure pasted elsewhere cannot later be
  mistaken for a live or current one.
- **FR-039**: The system MUST confirm to the user that the copy succeeded.
- **FR-040**: The copied summary MUST NOT contain anything the user could not see
  on screen.

**Operating the application**

- **FR-041**: Every action MUST be reachable and completable using the keyboard
  alone, without a pointing device. A representative holding a telephone has one
  hand, and looking away to find a mouse costs the seconds this feature exists to
  save.
- **FR-042**: The element with keyboard focus MUST be visibly distinct at all
  times, so a user can see where they are without moving the pointer.
- **FR-043**: The application MUST meet WCAG 2.1 Level AA, including contrast
  ratios and text alternatives.
- **FR-044**: Quantities, prices and branch identifiers MUST carry labels that
  identify them when read aloud by assistive technology, so that a figure is never
  announced as a bare number without saying what it counts.

### Key Entities

- **Part**: An item in the catalogue. Identified by part number; carries
  description, manufacturer, unit of measure, category, and list price.
- **Inventory position**: What one branch holds of one part — on hand, committed,
  free to sell, and storage location. One part's positions across all branches are
  held together in a single record, with each branch's figures in the same
  position within parallel repeating fields.
- **Branch**: A stocking location, identified by a short code, with a name and
  region.
- **Customer**: The account being served. Carries name, addresses, contacts, and
  the pricing terms that apply to them.
- **Contract terms**: The agreement that turns a list price into a customer's net
  price for a part or category, effective between two dates.
- **Order**: A customer's request for parts — customer, part, quantity, branch,
  promised date, and state. An order holds stock only while it is confirmed,
  allocated, or being picked; a quotation holds nothing, and shipped or cancelled
  orders hold nothing.
- **Activity record**: One recorded request — who made it, what they asked for,
  which database account served it, when, and how long it took.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A representative given a part number and a customer can state
  availability and net price within 30 seconds, without leaving the application.
- **SC-002**: The complete availability picture for a part is legible without
  scrolling on a standard branch workstation screen.
- **SC-003**: Searching, and opening a part, each return an answer in under one
  second for 95% of attempts, measured against the full demonstration catalogue of
  approximately 3,000 parts rather than a reduced subset.
- **SC-004**: A person unfamiliar with the application can answer a phoned
  availability question on their first attempt without being shown how.
- **SC-005**: Every branch position shown matches the ERP record it came from,
  verified across the whole demonstration data set with no mismatches.
- **SC-006**: Every request made through the application can be traced to the
  person who made it and the database account that served it, with no gaps.
- **SC-007**: No credential appears anywhere in the recorded activity, verified
  across the whole activity record.
- **SC-008**: A reviewer given only the application can determine, for any part
  shown, how the underlying record maps to the displayed figures.
- **SC-009**: Every failure a user can provoke produces a message stating what
  went wrong and what to do next; none produce a blank screen or a raw error.
- **SC-010**: Ten people using the application at the same moment each receive
  correct answers, with no degradation a user would notice.
- **SC-011**: Every user journey in this specification can be completed end to end
  using the keyboard alone, with the focused element visible at every step.
- **SC-012**: An automated accessibility audit of every screen reports no WCAG 2.1
  Level AA violations.
- **SC-013**: When the underlying data cannot be reached, the user is told so
  within five seconds, on every occasion, and is never shown an earlier reading in
  its place.
- **SC-014**: Free-to-sell figures reconcile exactly with the orders listed as
  holding stock, across the whole demonstration data set, with no branch where the
  listed commitments fail to account for the committed quantity.

## Assumptions

- **Read-only for this release.** The application answers questions; it does not
  place orders, reserve stock, or amend records. Everything a counter
  representative would then do happens in the ERP as it does today. The answer
  leaves the application only as text the user copies deliberately; there is no
  file export and no outbound email.
- **Demonstration data.** The system runs against a generated data set shaped like
  a distributor's, not against any real company's records. This is stated in the
  application itself. The set holds roughly 3,000 parts across 12 branches, with
  150 customers and 800 open orders — large enough that search has to be built
  properly rather than faked, and that a branch grid looks like real distribution,
  while remaining small enough to verify exhaustively.
- **A single region.** Availability is shown across the branches of one region,
  which is the set a representative would realistically transfer stock between.
- **Desktop use.** The primary user is at a branch workstation with a keyboard and
  a wide screen, and is frequently holding a telephone. The application is driven
  from the keyboard; a pointing device is supported but never required. It should
  remain usable on a smaller screen, but is not designed for phone-first use.
- **Sign-in exists.** Users arrive already identified; this feature consumes that
  identity rather than establishing it.
- **Availability is defined as on hand less committed.** Stock on order from
  suppliers is shown where known, but is not counted as available.
- **Pricing is a percentage of list.** Contract terms are expressed as a
  multiplier against list price, which is the common arrangement in electrical
  distribution. Quantity breaks and promotional pricing are out of scope.
- **One user at a time per session.** No collaborative or shared-cursor behaviour
  is required.
- **Reachability of the ERP is not guaranteed.** The application fails fast and
  says so when the ERP is slow or unavailable, and is not responsible for its
  availability. No figure is cached and re-served in place of a live answer.
