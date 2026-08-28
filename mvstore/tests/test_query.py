"""The four read verbs, and nothing else.

This store answers `LIST`, `SELECT`, `SSELECT` and `COUNT`. Every other verb is
refused, which matters as much as the ones that work: the MCP server in front of
this store enforces a read-only allowlist, and a store that quietly accepted a
write would make that enforcement untestable end to end.
"""

import pytest

from mvstore.query import QueryError, run_query
from mvstore.store import MultiValueStore


@pytest.fixture
def store(tmp_path):
    """A store holding a small inventory and product set."""
    store = MultiValueStore(tmp_path)
    store.write("PRODUCT", "SQD-QO120", ["QO 20A Breaker", "Square D", "BRK", "12.40", "A"])
    store.write("PRODUCT", "SQD-QO220", ["QO 20A 2-Pole", "Square D", "BRK", "24.80", "A"])
    store.write("PRODUCT", "IDL-4SQBOX", ["4in Square Box", "Ideal", "BOX", "3.15", "A"])
    store.write("PRODUCT", "OLD-RELAY", ["Legacy Relay", "Acme", "RLY", "88.00", "D"])
    store.write("INVENTORY", "SQD-QO120", [["DEN", "AUR"], ["142", "38"], ["40", "12"]])
    store.write("INVENTORY", "IDL-4SQBOX", [["DEN"], ["500"], ["0"]])
    return store


class TestList:
    """LIST returns records."""

    def test_listing_a_file_returns_every_key(self, store: MultiValueStore) -> None:
        """With no criteria, every record is returned."""
        result = run_query(store, "LIST PRODUCT")

        assert sorted(result.record_ids) == ["IDL-4SQBOX", "OLD-RELAY", "SQD-QO120", "SQD-QO220"]

    def test_listing_a_single_record_by_key(self, store: MultiValueStore) -> None:
        """A key after the file name selects just that record."""
        result = run_query(store, "LIST INVENTORY SQD-QO120")

        assert result.record_ids == ["SQD-QO120"]

    def test_listing_an_unknown_file_returns_nothing(self, store: MultiValueStore) -> None:
        """An absent file is empty, not an error: nothing has been written to it."""
        assert run_query(store, "LIST NOSUCHFILE").record_ids == []

    def test_a_sample_clause_limits_the_result(self, store: MultiValueStore) -> None:
        """SAMPLE caps how many records come back."""
        result = run_query(store, "LIST PRODUCT SAMPLE 2")

        assert len(result.record_ids) == 2
        assert result.is_complete is False

    def test_an_unlimited_result_is_marked_complete(self, store: MultiValueStore) -> None:
        """Without a cap, the answer is whole and says so."""
        assert run_query(store, "LIST PRODUCT").is_complete is True


class TestSelectionCriteria:
    """WITH clauses filter by field value."""

    def test_selecting_by_an_exact_field_value(self, store: MultiValueStore) -> None:
        """A WITH clause on a plain field matches exactly."""
        result = run_query(store, 'SELECT PRODUCT WITH F5 = "D"')

        assert result.record_ids == ["OLD-RELAY"]

    def test_selecting_matches_any_value_in_a_multivalued_field(
        self, store: MultiValueStore
    ) -> None:
        """A record matches when *any* value in the field matches.

        This is the behaviour that makes MultiValue selection different from SQL:
        one record holds many branches, and asking for stock at Aurora must find
        the record that mentions Aurora at any position.
        """
        result = run_query(store, 'SELECT INVENTORY WITH F1 = "AUR"')

        assert result.record_ids == ["SQD-QO120"]

    def test_selecting_accepts_several_alternatives(self, store: MultiValueStore) -> None:
        """Several quoted values after a field mean "any of these"."""
        result = run_query(store, 'SELECT PRODUCT WITH F3 = "BRK" "BOX"')

        assert sorted(result.record_ids) == ["IDL-4SQBOX", "SQD-QO120", "SQD-QO220"]

    def test_two_criteria_must_both_hold(self, store: MultiValueStore) -> None:
        """AND WITH narrows rather than widens."""
        result = run_query(store, 'SELECT PRODUCT WITH F3 = "BRK" AND WITH F2 = "Square D"')

        assert sorted(result.record_ids) == ["SQD-QO120", "SQD-QO220"]

    def test_a_criterion_matching_nothing_returns_nothing(self, store: MultiValueStore) -> None:
        """No match is an empty result, not an error."""
        assert run_query(store, 'SELECT PRODUCT WITH F2 = "Nobody"').record_ids == []

    def test_sselect_behaves_as_select(self, store: MultiValueStore) -> None:
        """SSELECT is a sorted SELECT; the selection itself is identical."""
        assert run_query(store, "SSELECT PRODUCT").record_ids == sorted(
            run_query(store, "SELECT PRODUCT").record_ids
        )


class TestCount:
    """COUNT returns a number, not records."""

    def test_counting_a_whole_file(self, store: MultiValueStore) -> None:
        """Every record is counted."""
        assert run_query(store, "COUNT PRODUCT").count == 4

    def test_counting_with_a_criterion(self, store: MultiValueStore) -> None:
        """Only matching records are counted."""
        assert run_query(store, 'COUNT PRODUCT WITH F3 = "BRK"').count == 2

    def test_counting_returns_no_record_ids(self, store: MultiValueStore) -> None:
        """A count is a figure; it does not carry the records with it."""
        assert run_query(store, "COUNT PRODUCT").record_ids == []


class TestRefusedVerbs:
    """Anything that is not one of the four read verbs is refused."""

    @pytest.mark.parametrize(
        "query",
        [
            "DELETE PRODUCT SQD-QO120",
            "CLEAR.FILE PRODUCT",
            "SH -c id",
            "!ls",
            "RUN BP EVIL",
            "LOGTO PAYROLL",
            "DROP TABLE PRODUCT",
            "UPDATE PRODUCT SET F1 = 'x'",
            "",
            "   ",
        ],
    )
    def test_the_verb_is_refused(self, store: MultiValueStore, query: str) -> None:
        """A store that quietly accepted these would make the layer above untestable."""
        with pytest.raises(QueryError):
            run_query(store, query)

    def test_a_query_without_a_file_name_is_refused(self, store: MultiValueStore) -> None:
        """A verb alone names nothing to act on."""
        with pytest.raises(QueryError):
            run_query(store, "LIST")
