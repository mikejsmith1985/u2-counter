"""Writing to the store, and the one mistake that would matter.

The read-only driver is unchanged and still refuses everything; these exercise
the separate module that can write. Two switches have to agree before anything
happens, and the interesting tests are not about whether a write lands — they are
about whether it lands without disturbing anything else.

That is the MultiValue-specific hazard. An INVENTORY record holds parallel
fields, so position n of the branch list, the on-hand list and the committed list
all describe the same branch. A write that rebuilds the record can leave one
field a value shorter than its siblings, shifting every position after it. The
record stays well formed, nothing errors, and a quantity is now read against the
wrong branch. These tests exist because that failure is invisible.
"""

from __future__ import annotations

import pytest

from mvstore import writable_driver
from mvstore.driver import UOError
from mvstore.seed import generate
from mvstore.store import AM, VM, MultiValueStore, parse_record


@pytest.fixture
def writable(tmp_path, monkeypatch):
    """A seeded store, reached through the writable driver with writes on."""
    monkeypatch.setenv("MVSTORE_DATA_PATH", str(tmp_path))
    monkeypatch.setenv("MVSTORE_WRITABLE", "true")

    generate(MultiValueStore(tmp_path), parts=20, branches=6, customers=5, orders=6, seed=1)

    return writable_driver.connect(host="h", user="u", password="p", account="DEMO")


@pytest.fixture
def refusing(tmp_path, monkeypatch):
    """The same driver with writes not permitted."""
    monkeypatch.setenv("MVSTORE_DATA_PATH", str(tmp_path))
    monkeypatch.delenv("MVSTORE_WRITABLE", raising=False)

    generate(MultiValueStore(tmp_path), parts=20, branches=6, customers=5, orders=6, seed=1)

    return writable_driver.connect(host="h", user="u", password="p", account="DEMO")


def a_part(session) -> str:
    """A part number that has an inventory record."""
    return sorted(session.store.keys("INVENTORY"))[0]


class TestTheReadOnlyDriverIsUntouched:
    """The claim the rest of the project rests on, asserted rather than assumed."""

    def test_the_original_driver_still_refuses_every_write(self, tmp_path, monkeypatch):
        # Even with writes permitted in the process. Selecting the read-only
        # driver is meant to be sufficient on its own -- somebody auditing this
        # should not have to reason about environment variables.
        from mvstore import driver as read_only

        monkeypatch.setenv("MVSTORE_DATA_PATH", str(tmp_path))
        monkeypatch.setenv("MVSTORE_WRITABLE", "true")

        generate(MultiValueStore(tmp_path), parts=10, branches=3, customers=3, orders=3, seed=1)

        session = read_only.connect(host="h", user="u", password="p", account="DEMO")
        handle = read_only.File("INVENTORY", session=session)

        with pytest.raises(read_only.UOError):
            handle.write("ANY", ["x"])


class TestBothSwitchesMustAgree:
    def test_loading_the_writable_driver_is_not_permission_to_write(self, refusing):
        # The driver name says which code is loaded. MVSTORE_WRITABLE says
        # whether it may act. A deployment that picked this module by accident
        # still writes nothing.
        handle = writable_driver.File("INVENTORY", session=refusing)

        with pytest.raises(UOError, match="MVSTORE_WRITABLE"):
            handle.write(a_part(refusing), ["x"])

    def test_a_dictionary_is_refused_even_with_writes_on(self, writable):
        # Changing a dictionary is a schema change. A caller that meant to has
        # picked the wrong tool.
        handle = writable_driver.File("DICT INVENTORY", session=writable)

        with pytest.raises(UOError, match="dictionary"):
            handle.write("ON.HAND", ["D", "2"])


class TestChangingOneValueDisturbsNothingElse:
    """The failure this module exists to make impossible."""

    def test_every_other_field_keeps_its_length(self, writable):
        part = a_part(writable)
        handle = writable_driver.File("INVENTORY", session=writable)

        before = [
            len(field) if isinstance(field, list) else 1
            for field in parse_record(handle.read(part))
        ]

        handle.update_value(part, position=2, index=0, value="4242")

        after = [
            len(field) if isinstance(field, list) else 1
            for field in parse_record(handle.read(part))
        ]

        # The whole point. A field that came back one value shorter would shift
        # every position after it onto the wrong branch, and nothing would say so.
        assert after == before

    def test_the_branch_at_that_position_is_still_the_same_branch(self, writable):
        part = a_part(writable)
        handle = writable_driver.File("INVENTORY", session=writable)

        branches_before = parse_record(handle.read(part))[0]

        handle.update_value(part, position=2, index=2, value="777")

        record = parse_record(handle.read(part))

        assert record[0] == branches_before
        assert record[1][2] == "777"

    def test_the_values_beside_it_are_untouched(self, writable):
        part = a_part(writable)
        handle = writable_driver.File("INVENTORY", session=writable)

        on_hand_before = list(parse_record(handle.read(part))[1])

        handle.update_value(part, position=2, index=1, value="99")

        on_hand_after = parse_record(handle.read(part))[1]

        for index, value in enumerate(on_hand_before):
            if index != 1:
                assert on_hand_after[index] == value

    def test_a_single_valued_field_stays_single_valued(self, writable):
        # A field holding one value must not become a list of one. The record's
        # text would change without its meaning changing, and the raw view shows
        # that text.
        handle = writable_driver.File("PRODUCT", session=writable)
        part = sorted(writable.store.keys("PRODUCT"))[0]

        handle.update_value(part, position=1, index=0, value="Rewritten description")

        raw = handle.read(part)

        assert raw.split(AM)[0] == "Rewritten description"
        assert VM not in raw.split(AM)[0]


class TestItRefusesRatherThanImprovises:
    @pytest.mark.parametrize("position", [0, -1, 99])
    def test_a_field_that_does_not_exist_is_refused(self, writable, position):
        # Creating one would be a layout change made by a typo.
        handle = writable_driver.File("INVENTORY", session=writable)

        with pytest.raises(UOError, match="does not exist"):
            handle.update_value(a_part(writable), position=position, index=0, value="1")

    def test_a_value_past_the_end_of_a_field_is_refused(self, writable):
        # Padding one parallel field and not its siblings is precisely the bug.
        handle = writable_driver.File("INVENTORY", session=writable)

        with pytest.raises(UOError, match="Refusing to pad"):
            handle.update_value(a_part(writable), position=2, index=999, value="1")

    @pytest.mark.parametrize("bad", [f"a{VM}b", "a\tb", "a\nb", "a\rb"])
    def test_a_value_carrying_a_separator_is_refused(self, writable, bad):
        # It would change the record's shape rather than its contents.
        handle = writable_driver.File("INVENTORY", session=writable)

        with pytest.raises(UOError):
            handle.update_value(a_part(writable), position=2, index=0, value=bad)


class TestWritingAWholeRecord:
    def test_a_written_record_reads_back_as_written(self, writable):
        handle = writable_driver.File("PRODUCT", session=writable)

        handle.write("NEW-PART-1", ["A description", "Maker", "MPN", "EA"])

        assert parse_record(handle.read("NEW-PART-1"))[0] == "A description"

    def test_a_record_carrying_a_line_break_is_refused(self, writable):
        # The store keeps one record per line, so a line break would truncate
        # everything after it -- silently, and only on the next read.
        handle = writable_driver.File("PRODUCT", session=writable)

        with pytest.raises(UOError):
            handle.write("NEW-PART-2", ["a\nb"])
