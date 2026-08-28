"""Records must survive storage exactly as written.

The raw-record view shows stored data to a user, so anything this store does to a
record on the way in or out is a lie the screen will tell. These tests hold it to
byte-for-byte fidelity, including the cases real MultiValue data actually
contains: empty interior fields, absent trailing fields, and subvalues.
"""

import pytest

from mvstore.store import AM, SM, VM, MultiValueStore, RecordNotFoundError


@pytest.fixture
def store(tmp_path):
    """A store backed by a temporary directory."""
    return MultiValueStore(tmp_path)


class TestRoundTrip:
    """What goes in comes out unchanged."""

    def test_a_simple_record_survives(self, store: MultiValueStore) -> None:
        """A record of plain fields reads back identically."""
        store.write("PRODUCT", "SQD-QO120", ["QO 20A Breaker", "Square D", "12.40"])

        assert store.read("PRODUCT", "SQD-QO120") == ["QO 20A Breaker", "Square D", "12.40"]

    def test_an_empty_interior_field_is_preserved(self, store: MultiValueStore) -> None:
        """Dropping an empty field would shift every field after it."""
        store.write("PRODUCT", "P1", ["first", "", "third"])

        assert store.read("PRODUCT", "P1") == ["first", "", "third"]

    def test_trailing_empty_fields_are_not_stored(self, store: MultiValueStore) -> None:
        """Real systems do not store trailing empties, so neither does this one."""
        store.write("PRODUCT", "P1", ["first", "second", "", ""])

        assert store.read("PRODUCT", "P1") == ["first", "second"]

    def test_multivalues_survive(self, store: MultiValueStore) -> None:
        """A field holding repeated values keeps every one of them."""
        store.write("INVENTORY", "P1", [["DEN", "AUR", "BOU"], ["142", "38", "0"]])

        assert store.read("INVENTORY", "P1") == [["DEN", "AUR", "BOU"], ["142", "38", "0"]]

    def test_an_empty_multivalue_keeps_its_position(self, store: MultiValueStore) -> None:
        """A missing middle value must not shift the values after it."""
        store.write("INVENTORY", "P1", [["DEN", "", "BOU"]])

        assert store.read("INVENTORY", "P1") == [["DEN", "", "BOU"]]

    def test_subvalues_survive(self, store: MultiValueStore) -> None:
        """The third level of the hierarchy round-trips as well as the first two."""
        store.write("CUSTOMER", "C1", [["Ray", "Marta"], [["303-0188", "720-0913"], "303-0190"]])

        assert store.read("CUSTOMER", "C1") == [
            ["Ray", "Marta"],
            [["303-0188", "720-0913"], "303-0190"],
        ]

    def test_text_in_any_alphabet_survives(self, store: MultiValueStore) -> None:
        """Accented names and currency symbols are business data, not decoration."""
        store.write("CUSTOMER", "C2", ["MÜLLER GmbH", "José Peña", "£1,250.00"])

        assert store.read("CUSTOMER", "C2") == ["MÜLLER GmbH", "José Peña", "£1,250.00"]


class TestStoredFormat:
    """The bytes on disk are genuine MultiValue, not a convenient substitute."""

    def test_fields_are_separated_by_attribute_marks(self, store: MultiValueStore) -> None:
        """The stored form uses the real separator, not a comma or a newline."""
        store.write("PRODUCT", "P1", ["one", "two"])

        assert store.raw("PRODUCT", "P1") == f"one{AM}two"

    def test_values_are_separated_by_value_marks(self, store: MultiValueStore) -> None:
        """Repeated values within a field use the real value mark."""
        store.write("INVENTORY", "P1", [["DEN", "AUR"]])

        assert store.raw("INVENTORY", "P1") == f"DEN{VM}AUR"

    def test_subvalues_are_separated_by_subvalue_marks(self, store: MultiValueStore) -> None:
        """The third level uses the real subvalue mark."""
        store.write("CUSTOMER", "C1", [[["a", "b"]]])

        assert store.raw("CUSTOMER", "C1") == f"a{SM}b"


class TestMissingRecords:
    """An absent record is a distinct answer from an empty one."""

    def test_reading_an_unknown_key_raises(self, store: MultiValueStore) -> None:
        """Not found must never be indistinguishable from found-but-empty."""
        with pytest.raises(RecordNotFoundError):
            store.read("PRODUCT", "NOSUCH")

    def test_reading_from_an_unknown_file_raises(self, store: MultiValueStore) -> None:
        """A file that does not exist is reported, not treated as empty."""
        with pytest.raises(RecordNotFoundError):
            store.read("NOSUCHFILE", "P1")

    def test_an_empty_record_is_not_a_missing_one(self, store: MultiValueStore) -> None:
        """A record written empty reads back as empty rather than raising."""
        store.write("PRODUCT", "P1", [])

        assert store.read("PRODUCT", "P1") == []


class TestListingAndCounting:
    """Enumerating what a file holds."""

    def test_keys_are_listed(self, store: MultiValueStore) -> None:
        """Every written key appears in the listing."""
        store.write("PRODUCT", "P1", ["a"])
        store.write("PRODUCT", "P2", ["b"])

        assert sorted(store.keys("PRODUCT")) == ["P1", "P2"]

    def test_an_unknown_file_lists_nothing(self, store: MultiValueStore) -> None:
        """Listing a file that does not exist is empty, not an error."""
        assert store.keys("NOSUCHFILE") == []

    def test_a_record_can_be_deleted(self, store: MultiValueStore) -> None:
        """Deletion removes the key from the listing."""
        store.write("PRODUCT", "P1", ["a"])
        store.delete("PRODUCT", "P1")

        assert store.keys("PRODUCT") == []


class TestBulkWriting:
    """Writing many records must not rewrite the file once per record.

    Every write persists the whole file, so writing n records one at a time is
    quadratic. At three thousand parts that is minutes rather than seconds, which
    makes seeding a chore and the test suite slow enough that people skip it.
    """

    def test_records_written_in_bulk_are_all_stored(self, store: MultiValueStore) -> None:
        """Deferring the save must not lose anything."""
        with store.bulk_write():
            for index in range(50):
                store.write("PRODUCT", f"P{index}", [f"part {index}"])

        assert len(store.keys("PRODUCT")) == 50

    def test_bulk_written_records_survive_a_new_instance(self, tmp_path) -> None:
        """The deferred save actually reaches disk when the block ends."""
        store = MultiValueStore(tmp_path)
        with store.bulk_write():
            store.write("PRODUCT", "P1", ["persisted"])

        assert MultiValueStore(tmp_path).read("PRODUCT", "P1") == ["persisted"]

    def test_a_failure_inside_the_block_still_persists_what_succeeded(self, tmp_path) -> None:
        """An interrupted seed leaves readable data rather than an empty file."""
        store = MultiValueStore(tmp_path)
        with pytest.raises(ValueError), store.bulk_write():
            store.write("PRODUCT", "P1", ["written"])
            raise ValueError("interrupted")

        assert MultiValueStore(tmp_path).read("PRODUCT", "P1") == ["written"]

    def test_reads_inside_the_block_see_pending_writes(self, store: MultiValueStore) -> None:
        """The generator reads back what it just wrote, so buffering must be visible."""
        with store.bulk_write():
            store.write("PRODUCT", "P1", ["pending"])

            assert store.read("PRODUCT", "P1") == ["pending"]

    def test_the_file_is_written_once_not_once_per_record(
        self, store: MultiValueStore, monkeypatch
    ) -> None:
        """The point of the block is fewer writes, so count them.

        Correctness alone would pass whether or not buffering happens, which is
        exactly how a no-op implementation slips through. This asserts the
        behaviour the block exists for.
        """
        write_count = 0
        original_write_text = type(store)._save_now

        def counting_save(self, file_name: str, records: dict[str, str]) -> None:
            nonlocal write_count
            write_count += 1
            original_write_text(self, file_name, records)

        monkeypatch.setattr(type(store), "_save_now", counting_save)

        with store.bulk_write():
            for index in range(50):
                store.write("PRODUCT", f"P{index}", [f"part {index}"])

        assert write_count == 1, f"the file was written {write_count} times for 50 records"


class TestDurability:
    """The store is file-backed, so a new instance sees what an earlier one wrote."""

    def test_records_survive_a_new_instance(self, tmp_path) -> None:
        """Restarting the store does not lose the demonstration data."""
        MultiValueStore(tmp_path).write("PRODUCT", "P1", ["persisted"])

        assert MultiValueStore(tmp_path).read("PRODUCT", "P1") == ["persisted"]
