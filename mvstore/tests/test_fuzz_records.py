"""What a record survives, generated rather than chosen.

Every other test in this suite uses records somebody wrote down, which means it
tests the cases somebody thought of. These generate them.

The property under test is the one the whole application rests on: a record
written and read back is the same record. If that ever fails, every figure on
every screen is suspect, and the failure would look like a parser bug in the
application rather than like a storage bug here.

Hypothesis shrinks a failure to its smallest form, so a break reports the
shortest record that causes it rather than the random one that happened to.
"""

from __future__ import annotations

import pytest
from hypothesis import HealthCheck, assume, given, settings
from hypothesis import strategies as st

from mvstore.store import AM, SM, VM, MultiValueStore, build_record, parse_record

# The three separators. A value containing one of them is not a value with an odd
# character in it -- it is a value that changes the record's shape, which is why
# they are excluded from generated text rather than left to chance.
MARKS = {AM, VM, SM}

# Tab and the line breaks are the storage format's own separators. The store
# refuses them on write, which is tested directly elsewhere.
STRUCTURAL = {"\t", "\n", "\r"}


def _is_safe(text: str) -> bool:
    """Whether text can appear inside a field without changing the record."""
    return not (set(text) & (MARKS | STRUCTURAL))


# Deliberately wide. Accented characters, currency symbols, CJK and emoji all
# occur in real customer and product data, and the encoding path has to carry
# them unchanged -- an earlier defect in the MCP server silently deleted every
# non-ASCII character from query output.
scalars = st.text(
    alphabet=st.characters(blacklist_categories=("Cs",)),
    min_size=0,
    max_size=40,
).filter(_is_safe)

subvalues = st.lists(scalars, min_size=1, max_size=3)
values = st.one_of(scalars, st.lists(st.one_of(scalars, subvalues), min_size=1, max_size=4))
records = st.lists(values, min_size=1, max_size=6)

keys = st.text(
    alphabet=st.characters(blacklist_categories=("Cs", "Cc")),
    min_size=1,
    max_size=30,
).filter(lambda key: _is_safe(key) and key.strip() == key and key.strip() != "")


class TestTheStoredFormIsStable:
    """Reading a record and writing it back does not keep changing it.

    Exact symmetry is not the property to assert here, because the format is
    deliberately lossy in two ways that match real MultiValue: trailing empty
    fields are not stored, and a field holding one value is stored as that value
    rather than as a list of one. Both are correct, and a test demanding
    `parse(build(x)) == x` fails on them while finding nothing.

    What must hold is that the loss happens once. If a second pass changed the
    record again, a record would drift every time it was read -- which is
    corruption, arriving slowly enough that nobody would connect it to reading.
    """

    @given(fields=records)
    def test_a_second_pass_changes_nothing(self, fields: list) -> None:
        once = parse_record(build_record(fields))
        twice = parse_record(build_record(once))

        assert twice == once

    @given(fields=records)
    def test_the_stored_text_is_stable_too(self, fields: list) -> None:
        once = build_record(fields)

        assert build_record(parse_record(once)) == once


class TestNothingIsLostThatWasNotEmpty:
    """The lossy cases are exactly the empty ones, and no others."""

    @given(fields=records)
    def test_a_record_ending_in_a_value_keeps_all_its_fields(
        self, fields: list
    ) -> None:
        # Only trailing *empty* fields are dropped. Give the record a final field
        # that holds something, and every field must survive.
        fields = [*fields, "end"]

        assert len(parse_record(build_record(fields))) == len(fields)

    @given(
        before=st.lists(scalars.filter(bool), min_size=1, max_size=3),
        after=st.lists(scalars.filter(bool), min_size=1, max_size=3),
    )
    def test_an_empty_field_in_the_middle_holds_its_position(
        self, before: list[str], after: list[str]
    ) -> None:
        # The silent version of this defect: drop an interior empty field and
        # every field after it moves up one, so quantities read against the
        # wrong heading and the screen stays entirely plausible.
        fields = [*before, "", *after]
        parsed = parse_record(build_record(fields))

        assert parsed == fields
        assert parsed[len(before)] == ""


class TestWritingAndReadingBack:
    """A record stored and retrieved is the record that was stored."""

    @settings(
        max_examples=60,
        deadline=None,
        suppress_health_check=[HealthCheck.function_scoped_fixture],
    )
    @given(key=keys, fields=records)
    def test_what_was_written_reads_back_in_its_stored_form(
        self, tmp_path_factory, key: str, fields: list
    ) -> None:
        store = MultiValueStore(tmp_path_factory.mktemp("roundtrip"))
        store.write("SCRATCH", key, fields)

        assert store.read("SCRATCH", key) == parse_record(build_record(fields))

    @settings(
        max_examples=40,
        deadline=None,
        suppress_health_check=[HealthCheck.function_scoped_fixture],
    )
    @given(key=keys, fields=records)
    def test_the_raw_form_is_the_one_the_builder_produces(
        self, tmp_path_factory, key: str, fields: list
    ) -> None:
        # The record view shows this text. If storage rewrote it, the screen
        # would be showing something the store never held -- which is the one
        # thing that view exists not to do.
        store = MultiValueStore(tmp_path_factory.mktemp("raw"))
        store.write("SCRATCH", key, fields)

        assert store.raw("SCRATCH", key) == build_record(fields)


class TestATrailingDropIsAlwaysRecoverable:
    """Dropped trailing fields come back, because the reader knows how many to expect.

    This is the property that makes the lossy storage safe rather than merely
    tolerable, and it covers an ordinary case: a product held at one branch, with
    nothing on order and no bin recorded, stores three fields where the reader
    expects five. If the reader could not restore them it would raise on a record
    that is perfectly valid, and the failure would look like a corrupt record
    rather than like a reader that could not count.
    """

    @settings(max_examples=80, deadline=None)
    @given(
        present=st.lists(
            st.integers(min_value=0, max_value=9999).map(str), min_size=1, max_size=1
        ),
        trailing_empty=st.integers(min_value=0, max_value=4),
    )
    def test_a_record_missing_its_empty_tail_reads_back_at_full_width(
        self, present: list[str], trailing_empty: int
    ) -> None:
        from mvstore.validation import normalise_parallel_fields

        kept = 5 - trailing_empty
        fields = [["B01"], present, *[[""]] * 4][:kept]

        restored = normalise_parallel_fields(parse_record(build_record(fields)), 5)

        assert len(restored) == 5
        assert restored[0] == ["B01"]
        if kept > 1:
            assert restored[1] == present


class TestParallelFieldsStayAligned:
    """Position n of every field belongs to the same branch.

    This is the property a parser gets wrong in a way nobody notices: reading
    position three of one field beside position four of another produces a screen
    that is entirely plausible and entirely wrong.
    """

    @settings(max_examples=60, deadline=None)
    @given(
        branches=st.lists(
            st.text(alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ", min_size=3, max_size=3),
            min_size=1,
            max_size=8,
            unique=True,
        ),
        data=st.data(),
    )
    def test_every_position_reads_back_against_its_own_branch(
        self, branches: list[str], data: st.DataObject
    ) -> None:
        from mvstore.validation import normalise_parallel_fields

        quantities = [
            data.draw(st.lists(
                st.integers(min_value=0, max_value=99999).map(str),
                min_size=len(branches),
                max_size=len(branches),
            ))
            for _ in range(3)
        ]

        fields = [branches, *quantities, [""] * len(branches)]
        aligned = normalise_parallel_fields(fields, parallel_count=5)

        # Every field is as long as the branch list, so no position can read
        # against a branch that is not there.
        for field in aligned:
            assert len(field) == len(branches)

        # And each position still holds what it was given.
        for position, branch in enumerate(branches):
            assert aligned[0][position] == branch
            for index in range(3):
                assert aligned[index + 1][position] == quantities[index][position]


class TestShortFieldsArePaddedNotShifted:
    """A field shorter than the branch list is padded at the end.

    Padding anywhere else moves every value after it onto a different branch,
    which is the silent version of this defect.
    """

    @settings(max_examples=60, deadline=None)
    @given(
        branch_count=st.integers(min_value=2, max_value=8),
        present=st.integers(min_value=0, max_value=8),
    )
    def test_the_values_that_exist_keep_their_positions(
        self, branch_count: int, present: int
    ) -> None:
        from mvstore.validation import normalise_parallel_fields

        assume(present <= branch_count)

        branches = [f"B{index:02d}" for index in range(branch_count)]
        bins = [f"bin{index}" for index in range(present)]

        aligned = normalise_parallel_fields([branches, bins], parallel_count=2)

        assert len(aligned[1]) == branch_count
        for position in range(present):
            assert aligned[1][position] == bins[position]
        for position in range(present, branch_count):
            assert aligned[1][position] == ""


class TestTheStoreRefusesWhatWouldCorruptIt:
    """Generated rather than chosen, because the interesting cases hide."""

    @settings(
        max_examples=40,
        deadline=None,
        suppress_health_check=[HealthCheck.function_scoped_fixture],
    )
    @given(
        prefix=st.text(max_size=10).filter(_is_safe),
        character=st.sampled_from(sorted(STRUCTURAL)),
        suffix=st.text(max_size=10).filter(_is_safe),
    )
    def test_any_structural_character_anywhere_is_refused(
        self, tmp_path_factory, prefix: str, character: str, suffix: str
    ) -> None:
        store = MultiValueStore(tmp_path_factory.mktemp("refuse"))

        with pytest.raises(ValueError):
            store.write("SCRATCH", "K-1", [prefix + character + suffix])
