"""The store describes itself, the way a MultiValue database does.

A UniVerse file has two parts: the data, and a dictionary describing it. The
dictionary is what makes the database self-describing -- someone handed an
account they have never seen runs `LIST DICT INVENTORY` and learns that field 2
is on-hand quantity, that it is multi-valued, and how to display it.

This store had no dictionaries, and the gap mattered more than it looked. The MCP
server's discovery tools exist precisely so that a stranger can point the server
at a database and find out what is in it, and against this store they returned
nothing. The one path such a person takes first was the one path never exercised.
"""

from __future__ import annotations

import pytest

from mvstore.dictionaries import DICTIONARIES, is_dictionary_name, store_name_for
from mvstore.query import run_query
from mvstore.seed import generate
from mvstore.store import MultiValueStore


@pytest.fixture
def store(tmp_path):
    """A seeded store, dictionaries included."""
    seeded = MultiValueStore(tmp_path)
    generate(seeded, parts=20, branches=9, customers=5, orders=6, seed=1)
    return seeded


class TestNaming:
    @pytest.mark.parametrize(
        ("written", "expected"),
        [
            ("DICT PRODUCT", "DICT.PRODUCT"),
            ("DICT.PRODUCT", "DICT.PRODUCT"),
            ("dict inventory", "DICT.INVENTORY"),
        ],
    )
    def test_a_dictionary_name_resolves_to_one_store_file(self, written, expected):
        # Universe writes the name with a space and a file on disk cannot, so the
        # translation happens once, here.
        assert store_name_for(written) == expected

    @pytest.mark.parametrize("name", ["DICT PRODUCT", "DICT.PRODUCT"])
    def test_a_dictionary_is_recognised(self, name):
        assert is_dictionary_name(name)

    def test_a_data_file_is_not_a_dictionary(self):
        assert not is_dictionary_name("PRODUCT")


class TestEveryFileIsDescribed:
    @pytest.mark.parametrize("file_name", sorted(DICTIONARIES))
    def test_the_dictionary_was_written(self, store, file_name):
        assert store.keys(store_name_for(file_name))

    @pytest.mark.parametrize("file_name", sorted(DICTIONARIES))
    def test_every_item_has_all_six_fields(self, store, file_name):
        # Type, location, conversion, heading, format, single or multi. A short
        # record would read as a different field in every position after the gap.
        for item_name in DICTIONARIES[file_name]:
            record = store.read(store_name_for(file_name), item_name)

            assert len(record) == 6, f"{file_name} {item_name} has {len(record)} fields"


class TestTheDescriptionMatchesTheData:
    """A dictionary that disagrees with the data is worse than none at all."""

    def test_the_parallel_fields_are_marked_multi_valued(self, store):
        # These five are the ones where reading the wrong position produces a
        # plausible, wrong answer. Marking them multi-valued is the only warning
        # a stranger gets.
        for item_name in ("BRANCH", "ON.HAND", "COMMITTED", "ON.ORDER", "BIN"):
            record = store.read("DICT.INVENTORY", item_name)

            assert record[5] == "M", f"{item_name} is not marked multi-valued"

    def test_the_field_positions_match_the_contract(self, store):
        # The positions the application reads. If these drift, a reader following
        # the dictionary and a reader following the code disagree, and only one
        # of them is checked by a test.
        expected = {"BRANCH": "1", "ON.HAND": "2", "COMMITTED": "3", "ON.ORDER": "4", "BIN": "5"}

        for item_name, position in expected.items():
            assert store.read("DICT.INVENTORY", item_name)[1] == position

    def test_money_carries_its_conversion(self, store):
        # Stored as an integer number of cents. A reader that missed MD2 would
        # report every price a hundred times too large, which is exactly the kind
        # of plausible wrong answer this project exists to prevent.
        assert store.read("DICT.PRODUCT", "LIST.PRICE")[2] == "MD2"


class TestListingADictionary:
    def test_the_items_come_back(self, store):
        result = run_query(store, "LIST DICT INVENTORY @ID SAMPLE 1000")

        assert result.file_name == "DICT INVENTORY"
        assert "ON.HAND" in result.record_ids

    def test_asking_for_the_key_alone_is_recorded(self, store):
        # Universe prints keys and nothing else for `@ID`, and the tools that ask
        # this way parse what comes back line by line.
        assert run_query(store, "LIST DICT INVENTORY @ID").is_keys_only

    def test_a_listing_without_an_output_field_is_not_keys_only(self, store):
        assert not run_query(store, "LIST BRANCH").is_keys_only

    def test_counting_a_dictionary_works_too(self, store):
        result = run_query(store, "COUNT DICT PRODUCT")

        assert result.count == len(DICTIONARIES["PRODUCT"])

    def test_an_output_field_is_not_mistaken_for_a_record_key(self, store):
        # A bare word after the file name is an explicit record key, which is
        # what `LIST BRANCH AUR` means. `@ID` is not a key -- it says which
        # column to print -- and taken as one it would send the query looking for
        # a record called "@ID" and finding nothing.
        #
        # That is precisely how this failed before: an empty listing rather than
        # an error, which is the kind of answer that gets believed.
        listed = run_query(store, "LIST BRANCH @ID")

        assert listed.count == len(store.keys("BRANCH"))
        assert listed.count > 0

    def test_a_named_key_still_selects_only_that_record(self, store):
        # The allowance must not cost the store its ordinary behaviour.
        one = run_query(store, "LIST BRANCH AUR")

        assert one.record_ids == ["AUR"]
