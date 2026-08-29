"""Generates demonstration data shaped like an electrical distributor's.

The generator is deterministic given a seed, so a reviewer investigating a defect
can recreate the exact data that produced it.

Every condition in the seed obligations table of contracts/multivalue-files.md is
placed deliberately rather than left to chance. A data set where a rule never
bites proves nothing about that rule: someone checking that quotations do not
hold stock needs a quotation against a part that also has live commitments, and
random generation cannot be relied on to produce one.
"""

from __future__ import annotations

import random
from collections.abc import Callable
from datetime import date, timedelta
from typing import Any

from .dictionaries import DICTIONARIES, store_name_for
from .store import MultiValueStore

# Branches of one region, which is the realistic radius for transferring stock.
BRANCHES = [
    ("DEN", "Denver", "Denver", "303-555-0142"),
    ("AUR", "Aurora", "Aurora", "303-555-0198"),
    ("BOU", "Boulder", "Boulder", "720-555-0117"),
    ("COS", "Colorado Springs", "Colorado Springs", "719-555-0164"),
    ("PUE", "Pueblo", "Pueblo", "719-555-0173"),
    ("FTC", "Fort Collins", "Fort Collins", "970-555-0129"),
    ("GRJ", "Grand Junction", "Grand Junction", "970-555-0186"),
    ("LOV", "Loveland", "Loveland", "970-555-0151"),
    ("LKW", "Lakewood", "Lakewood", "303-555-0135"),
    ("WMR", "Westminster", "Westminster", "303-555-0177"),
    ("CAS", "Castle Rock", "Castle Rock", "303-555-0190"),
    ("GRE", "Greeley", "Greeley", "970-555-0148"),
]

# Categories and the products that belong to them, as an electrical wholesaler
# would carry them.
CATEGORIES = {
    "BRK": ("Circuit Breakers", ["Square D", "Eaton", "Siemens", "Schneider Electric"]),
    "WIR": ("Wire and Cable", ["Southwire", "Encore Wire", "Cerrowire"]),
    "BOX": ("Boxes and Covers", ["Ideal", "Raco", "Appleton"]),
    "CON": ("Conduit and Fittings", ["Allied Tube", "Wheatland", "Bridgeport"]),
    "LTG": ("Lighting", ["Lithonia", "Acuity Brands", "Cree"]),
    "WDV": ("Wiring Devices", ["Leviton", "Hubbell", "Pass & Seymour"]),
    "TRA": ("Transformers", ["Acme Electric", "Square D", "Eaton"]),
    "TOO": ("Tools", ["Klein Tools", "Ideal", "Milwaukee"]),
}

PRODUCT_PATTERNS = {
    "BRK": ["{amp}A {pole}-Pole Circuit Breaker", "{amp}A GFCI Breaker", "{amp}A AFCI Breaker"],
    "WIR": ["{gauge} AWG THHN Stranded — {colour}", "{gauge}/2 NM-B Cable with Ground"],
    "BOX": ["4in Square Box {depth}in Deep", "Single-Gang Device Box", "Weatherproof Cover"],
    "CON": ["{size}in EMT Conduit", "{size}in EMT Compression Connector", "{size}in Rigid Elbow"],
    "LTG": ["{lumen}lm LED High Bay", "2x4 LED Troffer {lumen}lm", "LED Wall Pack {lumen}lm"],
    "WDV": ["{amp}A Duplex Receptacle", "{amp}A Single-Pole Switch", "GFCI Receptacle {amp}A"],
    "TRA": ["{kva}kVA Dry-Type Transformer", "{kva}kVA Buck-Boost Transformer"],
    "TOO": ["Wire Stripper", "Fish Tape 100ft", "Conduit Bender {size}in"],
}

# Customer names, including several that would break if text were stripped to
# plain ASCII — which is exactly why they are here (FR-031).
CUSTOMER_NAMES = [
    "Front Range Electric",
    "Mesa Verde Contracting",
    "Peña Brothers Electric",
    "Müller Industrial Services",
    "Højgaard Systems",
    "Summit Ridge Electrical",
    "Cañon City Power",
    "Aspen Grove Builders",
    "Rocky Mountain Mechanical",
    "Continental Divide Electric",
    "Platte Valley Contractors",
    "Bergström & Sons",
    "Alpine Electrical Supply",
    "Sangre de Cristo Services",
    "Boulder Creek Electric",
]

PRICE_CLASSES = ["A1", "B1", "B2", "C1", "C2", "D1"]
PAYMENT_TERMS = ["NET30", "NET45", "NET60", "COD", "2/10 NET30"]

ORDER_STATES_WITH_WEIGHT = [
    ("CONFIRMED", 25),
    ("ALLOCATED", 20),
    ("PICKING", 15),
    ("QUOTE", 20),
    ("SHIPPED", 15),
    ("CANCELLED", 5),
]

# Each obligation names a condition the data must contain, and how to check it.
SEED_OBLIGATIONS: dict[str, str] = {
    "part_at_every_branch": "A part stocked at every branch",
    "part_at_one_branch": "A part stocked at a single branch",
    "part_without_inventory": "A part with no INVENTORY record",
    "branch_entirely_committed": "A branch whose stock is entirely committed",
    "committed_exceeds_on_hand": "A branch where committed exceeds on hand",
    "unknown_branch_code": "A branch code absent from BRANCH",
    "customer_with_lapsed_terms": "A customer with lapsed and current terms",
    "customer_without_terms": "A customer with no terms at all",
    "quote_against_committed_part": "Quotations against a part with live commitments",
    "shipped_against_committed_part": "Shipped orders against a part with live commitments",
    "unaccounted_commitment": "A branch whose commitments fall short of its committed total",
    "commitments_never_exceed_committed": (
        "No branch whose orders hold more than its committed total"
    ),
    "non_ascii_text": "Names carrying accented characters",
    "discontinued_with_stock": "A discontinued part that still has stock",
}


def generate(
    store: MultiValueStore,
    seed: int = 20260828,
    parts: int = 3000,
    branches: int = 12,
    customers: int = 150,
    orders: int = 800,
) -> None:
    """Populate a store with a demonstration data set.

    Args:
        store: The store to write into
        seed: Random seed, so a data set can be recreated exactly
        parts: How many catalogue items to generate
        branches: How many branches to use, up to the twelve defined
        customers: How many customer accounts to generate
        orders: How many orders to generate
    """
    rng = random.Random(seed)
    branch_codes = [code for code, _, _, _ in BRANCHES[:branches]]

    # Buffered: each write otherwise rewrites its whole file, which makes seeding
    # three thousand parts quadratic and takes minutes rather than seconds.
    with store.bulk_write():
        _write_branches(store, branches)
        part_numbers = _write_products(store, rng, parts)
        account_numbers = _write_customers(store, rng, customers, branch_codes)
        _write_pricing(store, rng, account_numbers)

        # Orders are written before inventory because inventory depends on them.
        # In a real system a branch's committed quantity is not an independent
        # number: it is what the live orders hold. Generating the two separately
        # produced data where a branch showed nothing committed while three
        # orders held thirty-nine units of it -- a screen that is plausible,
        # internally contradictory, and impossible to explain on a call.
        holdings = _write_orders(
            store, rng, orders, part_numbers, account_numbers, branch_codes
        )
        _write_inventory(store, rng, part_numbers, branch_codes, holdings)

        # The dictionaries last, because they describe everything above.
        #
        # A MultiValue database is self-describing: someone handed an account
        # they have never seen runs LIST DICT INVENTORY and learns what each
        # field means. This store had no dictionaries, so the discovery tools --
        # the first thing a stranger would reach for -- returned nothing.
        _write_dictionaries(store)


def _write_dictionaries(store: MultiValueStore) -> None:
    """Write each file's dictionary, in UniVerse's own D-type layout.

    Args:
        store: The store to write into
    """
    for file_name, items in DICTIONARIES.items():
        dictionary_file = store_name_for(file_name)

        for item_name, definition in items.items():
            # The tuple is already in field order -- type, location, conversion,
            # heading, format, single or multi -- so it is written as it stands
            # rather than unpacked and reassembled.
            store.write(dictionary_file, item_name, list(definition))


def obligation_checks() -> dict[str, Callable[[MultiValueStore], bool]]:
    """Return the check that verifies each obligation, keyed the same way.

    Separate from `verify_obligations` so a test can hold this against
    `SEED_OBLIGATIONS` and fail when they drift.

    They can drift silently. The two are hand-written and keyed by the same
    strings, and `verify_obligations` iterates the checks -- so an obligation
    added here without a check is never evaluated, and the verifier still reports
    that every obligation is met.

    Returns:
        One predicate per obligation, keyed by the obligation's name
    """
    return {
        "part_at_every_branch": _has_part_at_every_branch,
        "part_at_one_branch": _has_part_at_one_branch,
        "part_without_inventory": _has_part_without_inventory,
        "branch_entirely_committed": lambda s: _has_position(s, lambda h, c: h > 0 and c == h),
        "committed_exceeds_on_hand": lambda s: _has_position(s, lambda h, c: c > h),
        "unknown_branch_code": _has_unknown_branch_code,
        "customer_with_lapsed_terms": _has_lapsed_terms,
        "customer_without_terms": _has_customer_without_terms,
        "quote_against_committed_part": lambda s: _has_state_against_committed(s, "QUOTE"),
        "shipped_against_committed_part": lambda s: _has_state_against_committed(s, "SHIPPED"),
        "unaccounted_commitment": _has_unaccounted_commitment,
        "commitments_never_exceed_committed": _commitments_never_exceed_committed,
        "non_ascii_text": _has_non_ascii_text,
        "discontinued_with_stock": _has_discontinued_with_stock,
    }


def verify_obligations(store: MultiValueStore) -> list[str]:
    """Return the obligations this data set fails to meet.

    Args:
        store: A seeded store

    Returns:
        Descriptions of unmet obligations; empty when the data exercises every rule

    Raises:
        RuntimeError: If an obligation has no check, which would pass vacuously
    """
    checks = obligation_checks()

    # Refused rather than reported, because the failure mode is a verifier that
    # cheerfully says everything is met while never having looked at one of them.
    unchecked = set(SEED_OBLIGATIONS) - set(checks)
    if unchecked:
        raise RuntimeError(
            f"These obligations have no check and would never be verified: "
            f"{', '.join(sorted(unchecked))}"
        )

    return [SEED_OBLIGATIONS[key] for key, check in checks.items() if not check(store)]


# -- generation ---------------------------------------------------------------


def _write_branches(store: MultiValueStore, count: int) -> None:
    """Write the BRANCH file."""
    for code, name, city, phone in BRANCHES[:count]:
        store.write("BRANCH", code, [name, city, "CO-FRONT", phone])


def _write_products(store: MultiValueStore, rng: random.Random, count: int) -> list[str]:
    """Write the PRODUCT file, returning every part number generated."""
    part_numbers: list[str] = []
    categories = list(CATEGORIES)

    for index in range(count):
        category = categories[index % len(categories)]
        _, manufacturers = CATEGORIES[category]
        manufacturer = rng.choice(manufacturers)
        description = _describe(rng, category)
        part_number = f"{_prefix(manufacturer)}-{category}{index:05d}"

        # The last five parts are discontinued, and all of them keep their stock,
        # so a discontinued part with inventory can be shown. Discontinued means
        # not stocked going forward, not absent -- a screen that hid these would
        # tell a customer there is none of something sitting on the shelf.
        status = "D" if index >= count - 5 else "A"

        store.write(
            "PRODUCT",
            part_number,
            [
                description,
                manufacturer,
                f"MPN{index:05d}",
                rng.choice(["EA", "EA", "EA", "FT", "BOX", "CTN"]),
                category,
                f"{rng.uniform(1.5, 480.0):.2f}",
                status,
            ],
        )
        part_numbers.append(part_number)

    return part_numbers


def _write_inventory(
    store: MultiValueStore,
    rng: random.Random,
    part_numbers: list[str],
    branch_codes: list[str],
    holdings: dict[tuple[str, str], int],
) -> None:
    """Write the INVENTORY file, placing every required stock condition.

    Args:
        store: The store to write into
        rng: The generator, so a data set can be recreated exactly
        part_numbers: Every part in the catalogue
        branch_codes: Every branch
        holdings: Units held per part and branch by orders in a holding state
    """
    # The first part is stocked everywhere; the second at one branch only; the
    # third is left without an inventory record entirely.
    _write_position(store, part_numbers[0], branch_codes, rng, holdings, spread="all")
    _write_position(store, part_numbers[1], branch_codes[:1], rng, holdings, spread="one")

    # An unknown branch code, so the application must display a code it cannot
    # resolve rather than dropping the row.
    _write_position(
        store, part_numbers[3], [*branch_codes[:3], "ZZZ"], rng, holdings, spread="unknown"
    )

    for part_number in part_numbers[4:]:
        stocking = rng.sample(branch_codes, k=rng.randint(1, len(branch_codes)))
        _write_position(store, part_number, stocking, rng, holdings, spread="normal")


# How many units an allocation nobody released leaves stranded, on the one
# position deliberately given that condition. The number is arbitrary; what
# matters is that it is not zero, so the unaccounted row has something to show.
STRANDED_ALLOCATION = 7


def _write_position(
    store: MultiValueStore,
    part_number: str,
    branch_codes: list[str],
    rng: random.Random,
    holdings: dict[tuple[str, str], int],
    spread: str,
) -> None:
    """Write one INVENTORY record across the given branches.

    Committed quantities come from the orders rather than the generator. That is
    what makes the commitments screen truthful: expanding a branch shows the
    orders holding its committed stock, and they add up because the committed
    figure was those orders in the first place.

    Three positions on the first record are given deliberate exceptions --
    entirely committed, oversold, and a stranded allocation -- because each is a
    condition a counter representative meets and the interface has to handle.

    None of them is a discrepancy the arithmetic cannot express: committed is
    never allowed to fall below what the orders hold, which would make the
    unaccounted figure negative and the screen unreadable.

    Args:
        store: The store to write into
        part_number: The part this record is for
        branch_codes: The branches it is stocked at
        rng: The generator, so a data set can be recreated exactly
        holdings: Units held per part and branch by orders in a holding state
        spread: Which of the required stock conditions this record carries
    """
    on_hand: list[str] = []
    committed: list[str] = []
    on_order: list[str] = []
    bins: list[str] = []

    for position, branch_code in enumerate(branch_codes):
        promised = holdings.get((part_number, branch_code), 0)

        # An allocation left behind by an order closed without releasing it.
        # Committed exceeds what any order explains, which is exactly the
        # unaccounted row the commitments screen exists to surface.
        if spread == "all" and position == 2:
            promised += STRANDED_ALLOCATION

        if spread == "all" and position == 0:
            # Entirely committed: nothing free to sell despite stock on the shelf.
            held = promised
        elif spread == "all" and position == 1:
            # Oversold: more promised than present, which happens and must not
            # produce a negative figure on screen.
            held = max(0, promised - 15)
        else:
            # Enough to cover what is promised, plus whatever else is on the
            # shelf. Committed can never exceed this by accident.
            held = promised + rng.choice([0, 0, rng.randint(1, 40), rng.randint(40, 600)])

        on_hand.append(str(held))
        committed.append(str(promised))
        on_order.append(str(rng.choice([0, 0, 0, rng.randint(50, 300)])))
        bins.append(rng.choice(["A", "B", "C", "D"]) + f"-{rng.randint(1, 40):02d}" if held else "")

    store.write("INVENTORY", part_number, [list(branch_codes), on_hand, committed, on_order, bins])


def _write_customers(
    store: MultiValueStore, rng: random.Random, count: int, branch_codes: list[str]
) -> list[str]:
    """Write the CUSTOMER file, returning every account number generated."""
    accounts: list[str] = []

    for index in range(count):
        account = f"C-{10000 + index}"
        name = CUSTOMER_NAMES[index % len(CUSTOMER_NAMES)]
        if index >= len(CUSTOMER_NAMES):
            name = f"{name} {index // len(CUSTOMER_NAMES) + 1}"

        # Contacts are parallel with their telephone numbers, and the first
        # contact carries two numbers as subvalues — the only three-level
        # structure in the data, kept because the record view demonstrates it.
        contacts = ["Ray Delgado", "Marta Quinn"]
        numbers: list[str | list[str]] = [
            [f"303-555-{rng.randint(100, 999):04d}", f"720-555-{rng.randint(100, 999):04d}"],
            f"303-555-{rng.randint(100, 999):04d}",
        ]

        store.write(
            "CUSTOMER",
            account,
            [
                name,
                [f"{rng.randint(100, 9999)} Wazee St", f"Suite {rng.randint(100, 900)}"],
                contacts,
                numbers,
                rng.choice(PAYMENT_TERMS),
                # The last few customers have no price class, so the list-price
                # path has an example.
                "" if index >= count - 3 else rng.choice(PRICE_CLASSES),
                rng.choice(branch_codes),
            ],
        )
        accounts.append(account)

    return accounts


def _write_pricing(store: MultiValueStore, rng: random.Random, accounts: list[str]) -> None:
    """Write the PRICING file, including terms that have lapsed."""
    today = date.today()

    for price_class in PRICE_CLASSES:
        for category in CATEGORIES:
            current_multiplier = f"{rng.uniform(0.55, 0.92):.2f}"
            multipliers = [current_multiplier]
            starts = [(today - timedelta(days=240)).isoformat()]
            ends = [(today + timedelta(days=120)).isoformat()]

            # Roughly a third of terms carry an expired promotion beside the
            # standing one, so disregarded terms are visible on screen.
            if rng.random() < 0.34:
                multipliers.append(f"{rng.uniform(0.45, 0.60):.2f}")
                starts.append((today - timedelta(days=200)).isoformat())
                ends.append((today - timedelta(days=40)).isoformat())

            store.write("PRICING", f"{price_class}*{category}", [multipliers, starts, ends])


def _write_orders(
    store: MultiValueStore,
    rng: random.Random,
    count: int,
    part_numbers: list[str],
    accounts: list[str],
    branch_codes: list[str],
) -> dict[tuple[str, str], int]:
    """Write the ORDER file, and report what those orders hold.

    Args:
        store: The store to write into
        rng: The generator, so a data set can be recreated exactly
        count: How many orders to write
        part_numbers: Every part in the catalogue
        accounts: Every customer account
        branch_codes: Every branch

    Returns:
        Units held per part and branch, counting only states that hold stock
    """
    from .validation import STATES_HOLDING_STOCK

    today = date.today()
    states = [state for state, weight in ORDER_STATES_WITH_WEIGHT for _ in range(weight)]
    holdings: dict[tuple[str, str], int] = {}

    def record(state: str, parts: list[str], quantities: list[int], branches: list[str]) -> None:
        """Add an order's lines to the running total, if its state holds stock."""
        if state not in STATES_HOLDING_STOCK:
            return
        for part, quantity, branch in zip(parts, quantities, branches, strict=True):
            holdings[(part, branch)] = holdings.get((part, branch), 0) + quantity

    # The part stocked everywhere carries a live commitment, a quotation and a
    # shipped order against the same branch, so the excluded states are visibly
    # excluded -- the screen has to show the allocated twenty and neither of the
    # other two, which is only a demonstration if all three exist.
    committed_part = part_numbers[0]
    fixed_orders = [
        ("ALLOCATED", branch_codes[0], 20),
        ("QUOTE", branch_codes[0], 20),
        ("SHIPPED", branch_codes[0], 20),
        # Position 1 of that record is deliberately oversold, and position 2
        # deliberately carries a stranded allocation. Both need real orders
        # behind them or the inventory record would claim commitments that no
        # order in the file could account for.
        ("CONFIRMED", branch_codes[1], 25),
        ("PICKING", branch_codes[2], 12),
    ]

    for index, (state, branch_code, quantity) in enumerate(fixed_orders):
        store.write(
            "ORDER",
            f"SO-{100000 + index}",
            [
                accounts[index % len(accounts)],
                (today - timedelta(days=3)).isoformat(),
                state,
                [committed_part],
                [str(quantity)],
                [branch_code],
                [(today + timedelta(days=5)).isoformat()],
            ],
        )
        record(state, [committed_part], [quantity], [branch_code])

    for index in range(len(fixed_orders), count):
        line_count = rng.randint(1, 4)
        lines = rng.sample(part_numbers, k=line_count)
        quantities = [rng.randint(1, 40) for _ in lines]
        branches = [rng.choice(branch_codes) for _ in lines]
        state = rng.choice(states)

        store.write(
            "ORDER",
            f"SO-{100000 + index}",
            [
                rng.choice(accounts),
                (today - timedelta(days=rng.randint(0, 60))).isoformat(),
                state,
                lines,
                [str(quantity) for quantity in quantities],
                branches,
                [(today + timedelta(days=rng.randint(1, 30))).isoformat() for _ in lines],
            ],
        )
        record(state, lines, quantities, branches)

    return holdings


def _describe(rng: random.Random, category: str) -> str:
    """Build a plausible product description for a category."""
    pattern = rng.choice(PRODUCT_PATTERNS[category])
    return pattern.format(
        amp=rng.choice([15, 20, 30, 40, 50, 60, 100]),
        pole=rng.choice([1, 2, 3]),
        gauge=rng.choice([14, 12, 10, 8, 6]),
        colour=rng.choice(["Black", "White", "Green", "Red", "Blue"]),
        depth=rng.choice(["1-1/2", "2-1/8"]),
        size=rng.choice(["1/2", "3/4", "1", "1-1/4", "2"]),
        lumen=rng.choice([2000, 4000, 8000, 12000, 20000]),
        kva=rng.choice([3, 5, 10, 15, 25, 45]),
    )


def _prefix(manufacturer: str) -> str:
    """Build a short manufacturer prefix for a part number."""
    words = manufacturer.replace("&", "").split()
    return "".join(word[0] for word in words[:3]).upper()


# -- obligation checks --------------------------------------------------------


def _values(fields: list[Any], index: int) -> list[str]:
    """Return field `index` as a list of scalars."""
    if index >= len(fields):
        return []
    field = fields[index]
    return [str(value) for value in field] if isinstance(field, list) else [str(field)]


def _has_part_at_every_branch(store: MultiValueStore) -> bool:
    branch_count = len(store.keys("BRANCH"))
    return any(
        len(_values(store.read("INVENTORY", key), 0)) == branch_count
        for key in store.keys("INVENTORY")
    )


def _has_part_at_one_branch(store: MultiValueStore) -> bool:
    return any(
        len(_values(store.read("INVENTORY", key), 0)) == 1 for key in store.keys("INVENTORY")
    )


def _has_part_without_inventory(store: MultiValueStore) -> bool:
    return bool(set(store.keys("PRODUCT")) - set(store.keys("INVENTORY")))


def _has_position(store: MultiValueStore, predicate: Callable[[int, int], bool]) -> bool:
    for key in store.keys("INVENTORY"):
        fields = store.read("INVENTORY", key)
        for held, promised in zip(_values(fields, 1), _values(fields, 2), strict=False):
            if predicate(int(held or 0), int(promised or 0)):
                return True
    return False


def _has_unknown_branch_code(store: MultiValueStore) -> bool:
    known = set(store.keys("BRANCH"))
    return any(
        set(_values(store.read("INVENTORY", key), 0)) - known for key in store.keys("INVENTORY")
    )


def _has_lapsed_terms(store: MultiValueStore) -> bool:
    today = date.today().isoformat()
    return any(
        any(ends < today for ends in _values(store.read("PRICING", key), 2))
        for key in store.keys("PRICING")
    )


def _has_customer_without_terms(store: MultiValueStore) -> bool:
    return any(not _values(store.read("CUSTOMER", key), 5)[0] for key in store.keys("CUSTOMER"))


def _has_state_against_committed(store: MultiValueStore, state: str) -> bool:
    from .validation import STATES_HOLDING_STOCK

    in_state: set[str] = set()
    committed: set[str] = set()
    for key in store.keys("ORDER"):
        fields = store.read("ORDER", key)
        order_state = str(fields[2]).upper()
        parts = set(_values(fields, 3))
        if order_state == state:
            in_state |= parts
        if order_state in STATES_HOLDING_STOCK:
            committed |= parts

    return bool(in_state & committed)


def _has_unaccounted_commitment(store: MultiValueStore) -> bool:
    """Whether some branch's committed total exceeds what its orders account for.

    Placed deliberately rather than arrived at by chance: one position on the
    first record carries an allocation left behind by an order that was closed
    without releasing it, which is a thing that happens and which the commitments
    screen has to be able to say out loud.
    """
    accounted = _units_held_by_orders(store)

    for key in store.keys("INVENTORY"):
        fields = store.read("INVENTORY", key)
        for branch, promised in zip(_values(fields, 0), _values(fields, 2), strict=False):
            if int(promised or 0) > accounted.get((key, branch), 0):
                return True

    return False


def _commitments_never_exceed_committed(store: MultiValueStore) -> bool:
    """Whether every branch's committed total covers what its orders hold.

    The commitments screen shows the orders holding a branch's committed stock
    and, beside them, the part of that total no order explains. The arithmetic
    only closes in one direction: orders holding more than the record says is
    committed would make that figure negative, and there is nothing truthful to
    put on the screen in that case.

    So the data must not contain it. This is the invariant, checked here rather
    than left for the integration suite to discover, because a data set that
    breaks it produces failures that look like parser bugs.
    """
    accounted = _units_held_by_orders(store)

    for key in store.keys("INVENTORY"):
        fields = store.read("INVENTORY", key)
        for branch, promised in zip(_values(fields, 0), _values(fields, 2), strict=False):
            if accounted.get((key, branch), 0) > int(promised or 0):
                return False

    return True


def _units_held_by_orders(store: MultiValueStore) -> dict[tuple[str, str], int]:
    """Units held per part and branch by orders in a state that holds stock."""
    from .validation import STATES_HOLDING_STOCK

    accounted: dict[tuple[str, str], int] = {}

    for key in store.keys("ORDER"):
        fields = store.read("ORDER", key)
        if str(fields[2]).upper() not in STATES_HOLDING_STOCK:
            continue

        parts = _values(fields, 3)
        quantities = _values(fields, 4)
        branches = _values(fields, 5)

        for part, quantity, branch in zip(parts, quantities, branches, strict=False):
            accounted[(part, branch)] = accounted.get((part, branch), 0) + int(quantity or 0)

    return accounted


def _has_non_ascii_text(store: MultiValueStore) -> bool:
    return any(
        any(ord(char) > 127 for char in str(store.read("CUSTOMER", key)[0]))
        for key in store.keys("CUSTOMER")
    )


def _has_discontinued_with_stock(store: MultiValueStore) -> bool:
    for key in store.keys("PRODUCT"):
        if str(store.read("PRODUCT", key)[6]) != "D":
            continue
        if not store.exists("INVENTORY", key):
            continue
        if any(int(held or 0) > 0 for held in _values(store.read("INVENTORY", key), 1)):
            return True
    return False
