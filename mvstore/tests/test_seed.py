"""The demonstration data must exercise every rule, not merely exist.

Each obligation in contracts/multivalue-files.md is asserted here. A data set
where a rule never bites is a data set that proves nothing about that rule: a
reviewer checking that quotations do not hold stock needs a quotation to exist
against a part that also has live commitments.
"""

import pytest

from mvstore.seed import SEED_OBLIGATIONS, generate, verify_obligations
from mvstore.store import MultiValueStore
from mvstore.validation import STATES_HOLDING_STOCK, validate_record

# Small enough to generate quickly in a test, large enough that every obligation
# still has room to appear.
TEST_SCALE = {"parts": 300, "branches": 12, "customers": 40, "orders": 120}


@pytest.fixture(scope="module")
def seeded(tmp_path_factory):
    """A store seeded once and shared, since generation is the expensive part."""
    store = MultiValueStore(tmp_path_factory.mktemp("seed"))
    generate(store, seed=20260828, **TEST_SCALE)
    return store


class TestScale:
    """The generated set is the size it was asked for."""

    def test_every_file_is_populated(self, seeded: MultiValueStore) -> None:
        """All six ERP files hold records."""
        for file_name in ("PRODUCT", "INVENTORY", "BRANCH", "CUSTOMER", "PRICING", "ORDER"):
            assert seeded.keys(file_name), f"{file_name} is empty"

    def test_the_catalogue_is_the_requested_size(self, seeded: MultiValueStore) -> None:
        """Part count matches what was asked for."""
        assert len(seeded.keys("PRODUCT")) == TEST_SCALE["parts"]

    def test_every_branch_exists(self, seeded: MultiValueStore) -> None:
        """The branch file holds one record per branch."""
        assert len(seeded.keys("BRANCH")) == TEST_SCALE["branches"]


class TestGeneratedRecordsAreValid:
    """Everything generated satisfies the structural rules."""

    @pytest.mark.parametrize("file_name", ["INVENTORY", "ORDER", "PRICING"])
    def test_every_record_validates(self, seeded: MultiValueStore, file_name: str) -> None:
        """Not one generated record breaks its file's rules."""
        for key in seeded.keys(file_name):
            validate_record(file_name, seeded.read(file_name, key))


class TestSeedObligations:
    """Each condition the contract requires the data to contain."""

    def test_every_obligation_is_met(self, seeded: MultiValueStore) -> None:
        """The verifier finds every required condition present."""
        unmet = verify_obligations(seeded)

        assert unmet == [], f"Seed data does not exercise: {unmet}"

    def test_the_obligation_list_is_not_empty(self) -> None:
        """A verifier with nothing to check would pass vacuously."""
        assert len(SEED_OBLIGATIONS) >= 10

    def test_a_part_is_stocked_at_every_branch(self, seeded: MultiValueStore) -> None:
        """One end of the branch grid."""
        branch_count = len(seeded.keys("BRANCH"))
        widest = max(len(seeded.read("INVENTORY", key)[0]) for key in seeded.keys("INVENTORY"))

        assert widest == branch_count

    def test_a_part_has_no_inventory_record(self, seeded: MultiValueStore) -> None:
        """Unknown stock is a different state from zero, and needs an example."""
        parts = set(seeded.keys("PRODUCT"))
        stocked = set(seeded.keys("INVENTORY"))

        assert parts - stocked, "every part has stock, so unknown-stock cannot be shown"

    def test_a_branch_has_stock_entirely_committed(self, seeded: MultiValueStore) -> None:
        """The AllCommitted state needs an example to display."""
        assert _has_position(
            seeded, lambda on_hand, committed: on_hand > 0 and committed == on_hand
        )

    def test_a_branch_has_committed_exceeding_on_hand(self, seeded: MultiValueStore) -> None:
        """Real data does this; free-to-sell must clamp rather than go negative."""
        assert _has_position(seeded, lambda on_hand, committed: committed > on_hand)

    def test_quotations_exist_against_a_committed_part(self, seeded: MultiValueStore) -> None:
        """The excluded states must be visibly excluded, not merely absent."""
        quoted_parts = _parts_in_state(seeded, "QUOTE")
        committed_parts = set()
        for state in STATES_HOLDING_STOCK:
            committed_parts |= _parts_in_state(seeded, state)

        assert quoted_parts & committed_parts, "no part has both a quote and a live commitment"

    def test_text_in_other_alphabets_is_present(self, seeded: MultiValueStore) -> None:
        """FR-031 needs data that would break if text were stripped."""
        names = [str(seeded.read("CUSTOMER", key)[0]) for key in seeded.keys("CUSTOMER")]

        assert any(any(ord(char) > 127 for char in name) for name in names)


class TestDeterminism:
    """The same seed produces the same data, so a failure can be reproduced."""

    def test_two_runs_with_one_seed_match(self, tmp_path) -> None:
        """A reviewer investigating a defect must be able to recreate the data."""
        first = MultiValueStore(tmp_path / "first")
        second = MultiValueStore(tmp_path / "second")
        generate(first, seed=99, parts=50, branches=4, customers=10, orders=20)
        generate(second, seed=99, parts=50, branches=4, customers=10, orders=20)

        assert [first.raw("PRODUCT", key) for key in sorted(first.keys("PRODUCT"))] == [
            second.raw("PRODUCT", key) for key in sorted(second.keys("PRODUCT"))
        ]


def _has_position(store: MultiValueStore, predicate) -> bool:
    """Return whether any branch position in any record satisfies the predicate."""
    for key in store.keys("INVENTORY"):
        fields = store.read("INVENTORY", key)
        on_hand = _values(fields, 1)
        committed = _values(fields, 2)
        for held, promised in zip(on_hand, committed, strict=False):
            if predicate(int(held or 0), int(promised or 0)):
                return True
    return False


def _values(fields: list, index: int) -> list[str]:
    """Return field `index` as a list of scalars."""
    if index >= len(fields):
        return []
    field = fields[index]
    return [str(value) for value in field] if isinstance(field, list) else [str(field)]


def _parts_in_state(store: MultiValueStore, state: str) -> set[str]:
    """Return every part number appearing on an order in the given state."""
    parts: set[str] = set()
    for key in store.keys("ORDER"):
        fields = store.read("ORDER", key)
        if str(fields[2]).upper() == state:
            parts.update(_values(fields, 3))
    return parts
