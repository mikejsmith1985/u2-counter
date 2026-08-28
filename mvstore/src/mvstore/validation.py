"""Structural rules for the ERP files, from contracts/multivalue-files.md.

The rules about parallel fields carry the most weight, because breaking one is
silent. A branch shown against another branch's quantities looks entirely correct
on screen: the numbers are real, the branch is real, and the pairing is wrong.
Nothing downstream can detect that, so it is caught here.
"""

from __future__ import annotations

from datetime import date

from .store import FieldValue

# Order states, and which of them hold stock. The set is closed so that a typo
# in data cannot inflate what appears committed (FR-018).
ORDER_STATES = ("QUOTE", "CONFIRMED", "ALLOCATED", "PICKING", "SHIPPED", "CANCELLED")
STATES_HOLDING_STOCK = frozenset({"CONFIRMED", "ALLOCATED", "PICKING"})

# Files whose structure is declared in the contract, with how many of their
# leading fields are parallel.
_PARALLEL_FIELD_COUNTS = {
    "INVENTORY": 5,
    "PRICING": 3,
}

# ORDER's line-item fields are parallel, but they start at field 4 rather than
# field 1, because the first three are the header.
_ORDER_LINE_FIELDS = (3, 4, 5, 6)

# A contract multiplier discounts from list. Above one is a markup, which these
# terms do not express; at or below zero is not a price.
_LOWEST_MULTIPLIER = 0.0
_HIGHEST_MULTIPLIER = 1.0


class RecordInvalidError(Exception):
    """Raised when a record breaks a structural rule of its file."""


def normalise_parallel_fields(fields: list[FieldValue], parallel_count: int) -> list[list[str]]:
    """Align parallel fields to the length of the first, padding short ones.

    Padding rather than truncating is deliberate. A record whose bin field is one
    short means one branch has no bin; shortening the other fields to match would
    instead drop a branch and everything known about it.

    Args:
        fields: The record's fields
        parallel_count: How many leading fields are parallel

    Returns:
        The parallel fields, each as a list of the same length

    Raises:
        RecordInvalidError: If any field is longer than the first, which means a
            value exists for a subject that was never named
    """
    normalised = [
        _as_values(fields[index]) if index < len(fields) else [] for index in range(parallel_count)
    ]
    expected_length = len(normalised[0])

    for position, values in enumerate(normalised[1:], start=2):
        if len(values) > expected_length:
            raise RecordInvalidError(
                f"Field {position} holds {len(values)} values where field 1 names "
                f"{expected_length} subjects. A value with no subject cannot be attributed."
            )

    return [values + [""] * (expected_length - len(values)) for values in normalised]


def validate_record(file_name: str, fields: list[FieldValue]) -> None:
    """Check a record against its file's declared structure.

    A file with no declared layout is stored as given: only the files named in the
    contract carry rules.

    Args:
        file_name: The MultiValue file the record belongs to
        fields: The record's fields

    Raises:
        RecordInvalidError: If the record breaks a rule
    """
    validators = {
        "INVENTORY": _validate_inventory,
        "ORDER": _validate_order,
        "PRICING": _validate_pricing,
    }
    validator = validators.get(file_name.upper())
    if validator is not None:
        validator(fields)


def _validate_inventory(fields: list[FieldValue]) -> None:
    """Check an INVENTORY record.

    Committed exceeding on hand is permitted: real ERP data produces it, and the
    application clamps free-to-sell at zero rather than the store rejecting a
    record a real system would hold.
    """
    aligned = normalise_parallel_fields(fields, _PARALLEL_FIELD_COUNTS["INVENTORY"])
    branches, on_hand, committed, on_order = aligned[0], aligned[1], aligned[2], aligned[3]

    named = [branch for branch in branches if branch]
    duplicates = {branch for branch in named if named.count(branch) > 1}
    if duplicates:
        raise RecordInvalidError(
            f"Branch {', '.join(sorted(duplicates))} appears more than once. "
            "Availability for it would be ambiguous."
        )

    for label, quantities in (
        ("on hand", on_hand),
        ("committed", committed),
        ("on order", on_order),
    ):
        for quantity in quantities:
            _require_whole_number(quantity, f"{label} quantity")


def _validate_order(fields: list[FieldValue]) -> None:
    """Check an ORDER record, including that its state is one we recognise."""
    if len(fields) < 3:
        raise RecordInvalidError("An order needs a customer, a date and a state")

    state = str(fields[2]).upper()
    if state not in ORDER_STATES:
        raise RecordInvalidError(
            f"'{state}' is not an order state. Permitted: {', '.join(ORDER_STATES)}. "
            "The set is closed so an unrecognised state cannot hold stock."
        )

    lines = [
        _as_values(fields[index]) if index < len(fields) else [] for index in _ORDER_LINE_FIELDS
    ]
    lengths = {len(values) for values in lines}
    if len(lengths) > 1:
        raise RecordInvalidError(
            f"Order line fields hold {sorted(lengths)} values. Every line needs a part, "
            "a quantity, a branch and a promised date."
        )

    for quantity in lines[1]:
        _require_whole_number(quantity, "line quantity")
        if int(quantity) == 0:
            raise RecordInvalidError("An order line for no quantity is not a line")


def _validate_pricing(fields: list[FieldValue]) -> None:
    """Check a PRICING record's multipliers and effective windows."""
    aligned = normalise_parallel_fields(fields, _PARALLEL_FIELD_COUNTS["PRICING"])
    multipliers, effective_from, effective_to = aligned

    for multiplier in multipliers:
        _require_multiplier(multiplier)

    for starts, ends in zip(effective_from, effective_to, strict=True):
        if _as_date(starts) > _as_date(ends):
            raise RecordInvalidError(
                f"Terms effective from {starts} to {ends} end before they begin, "
                "so they could never apply."
            )


def _as_values(field: FieldValue) -> list[str]:
    """Return a field as a list of scalars, whether it holds one value or many."""
    if isinstance(field, list):
        return [str(value) for value in field]
    return [str(field)]


def _require_whole_number(value: str, label: str) -> None:
    """Raise unless a value is a non-negative whole number."""
    if value == "":
        return
    if not value.lstrip("-").isdigit():
        raise RecordInvalidError(f"'{value}' is not a number, so it cannot be a {label}")
    if int(value) < 0:
        raise RecordInvalidError(f"A {label} cannot be negative: '{value}'")


def _require_multiplier(value: str) -> None:
    """Raise unless a value is a multiplier that discounts from list price."""
    try:
        multiplier = float(value)
    except ValueError as error:
        raise RecordInvalidError(f"'{value}' is not a multiplier") from error

    if not _LOWEST_MULTIPLIER < multiplier <= _HIGHEST_MULTIPLIER:
        raise RecordInvalidError(
            f"A multiplier of {value} is outside the range these terms express. "
            "Contract terms discount from list price."
        )


def _as_date(value: str) -> date:
    """Parse a stored date, which the contract fixes as YYYY-MM-DD."""
    try:
        return date.fromisoformat(value)
    except ValueError as error:
        raise RecordInvalidError(f"'{value}' is not a date in YYYY-MM-DD form") from error
