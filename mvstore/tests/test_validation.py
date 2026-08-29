"""Validation of records against the layouts in contracts/multivalue-files.md.

The rules that matter most are about parallel fields, because breaking one is
silent. A branch shown against another branch's quantities looks entirely correct
on screen — the numbers are real, the branch is real, and the pairing is wrong.
"""

import pytest

from mvstore.validation import RecordInvalidError, normalise_parallel_fields, validate_record


class TestParallelFieldAlignment:
    """Position n of every parallel field belongs to the same subject."""

    def test_a_short_field_is_padded(self) -> None:
        """A missing bin must not shift the branch it belongs to.

        Field 5 has three values where field 1 has four. Padding keeps DEN, AUR,
        BOU and COS aligned with their own quantities; truncating the others
        would drop a branch entirely.
        """
        fields = [
            ["DEN", "AUR", "BOU", "COS"],
            ["142", "38", "0", "85"],
            ["40", "12", "0", "6"],
            ["0", "0", "200", "0"],
            # Short on purpose: three bins for four branches, which is the case
            # under test. This used to be written full-length and overwritten on
            # the next line, so the record being tested was not the one the
            # reader saw.
            ["A-12", "C-04", ""],
        ]

        normalised = normalise_parallel_fields(fields, parallel_count=5)

        assert normalised[4] == ["A-12", "C-04", "", ""]

    def test_no_other_field_is_shortened_by_padding(self) -> None:
        """Padding one field must leave the rest exactly as they were."""
        fields = [["DEN", "AUR", "BOU"], ["1", "2", "3"], ["4", "5"]]

        normalised = normalise_parallel_fields(fields, parallel_count=3)

        assert normalised[0] == ["DEN", "AUR", "BOU"]
        assert normalised[1] == ["1", "2", "3"]

    def test_a_field_longer_than_the_first_is_rejected(self) -> None:
        """A quantity for a branch that was never named is malformed data.

        Padding cannot fix this: there is no branch to attribute the quantity to,
        and guessing one would invent a fact.
        """
        fields = [["DEN", "AUR"], ["142", "38", "99"]]

        with pytest.raises(RecordInvalidError) as error:
            normalise_parallel_fields(fields, parallel_count=2)

        assert "field 2" in str(error.value).lower()

    def test_a_scalar_field_becomes_a_single_value_list(self) -> None:
        """A record with one branch stores a string, not a list; both must work."""
        normalised = normalise_parallel_fields([["DEN"], "142", "40"], parallel_count=3)

        assert normalised[1] == ["142"]


class TestInventoryRules:
    """Rules specific to the INVENTORY layout."""

    def test_a_well_formed_record_passes(self) -> None:
        """The shape from the contract validates without complaint."""
        validate_record(
            "INVENTORY",
            [["DEN", "AUR"], ["142", "38"], ["40", "12"], ["0", "0"], ["A-12", "C-04"]],
        )

    def test_a_repeated_branch_is_rejected(self) -> None:
        """One record may hold a branch once; twice makes availability ambiguous."""
        with pytest.raises(RecordInvalidError) as error:
            validate_record("INVENTORY", [["DEN", "DEN"], ["1", "2"], ["0", "0"]])

        assert "DEN" in str(error.value)

    def test_a_negative_quantity_is_rejected(self) -> None:
        """Stock cannot be less than none."""
        with pytest.raises(RecordInvalidError):
            validate_record("INVENTORY", [["DEN"], ["-5"], ["0"]])

    def test_a_non_numeric_quantity_is_rejected(self) -> None:
        """A quantity that is not a number cannot be arithmetic on a screen."""
        with pytest.raises(RecordInvalidError):
            validate_record("INVENTORY", [["DEN"], ["lots"], ["0"]])

    def test_committed_may_exceed_on_hand(self) -> None:
        """Real ERP data does this. The application clamps; the store accepts."""
        validate_record("INVENTORY", [["DEN"], ["10"], ["25"], ["0"], ["A-1"]])


class TestOrderRules:
    """Rules specific to the ORDER layout."""

    def test_a_well_formed_order_passes(self) -> None:
        """The shape from the contract validates."""
        validate_record(
            "ORDER",
            [
                "C-10442",
                "2026-08-24",
                "ALLOCATED",
                ["P1", "P2"],
                ["25", "10"],
                ["DEN", "DEN"],
                ["2026-09-02", "2026-09-02"],
            ],
        )

    def test_an_unknown_state_is_rejected(self) -> None:
        """The state set is closed, so a typo cannot inflate committed stock."""
        with pytest.raises(RecordInvalidError) as error:
            validate_record(
                "ORDER", ["C-1", "2026-08-24", "CONFIMED", ["P1"], ["1"], ["DEN"], ["2026-09-02"]]
            )

        assert "CONFIMED" in str(error.value)

    def test_line_fields_of_unequal_length_are_rejected(self) -> None:
        """A quantity without a part is a line item that means nothing."""
        with pytest.raises(RecordInvalidError):
            validate_record(
                "ORDER",
                ["C-1", "2026-08-24", "CONFIRMED", ["P1"], ["1", "2"], ["DEN"], ["2026-09-02"]],
            )

    def test_a_zero_quantity_line_is_rejected(self) -> None:
        """A line for none of something is not an order line."""
        with pytest.raises(RecordInvalidError):
            validate_record(
                "ORDER", ["C-1", "2026-08-24", "CONFIRMED", ["P1"], ["0"], ["DEN"], ["2026-09-02"]]
            )


class TestPricingRules:
    """Rules specific to the PRICING layout."""

    def test_a_well_formed_terms_record_passes(self) -> None:
        """Two sets of terms, one current and one expired, is the contract's example."""
        validate_record(
            "PRICING",
            [["0.70", "0.62"], ["2026-01-01", "2026-05-01"], ["2026-12-31", "2026-06-30"]],
        )

    def test_a_multiplier_above_one_is_rejected(self) -> None:
        """Contract terms discount from list; a multiplier above one is a markup."""
        with pytest.raises(RecordInvalidError):
            validate_record("PRICING", [["1.20"], ["2026-01-01"], ["2026-12-31"]])

    def test_a_zero_multiplier_is_rejected(self) -> None:
        """Free is not a contract term; it is a data error."""
        with pytest.raises(RecordInvalidError):
            validate_record("PRICING", [["0"], ["2026-01-01"], ["2026-12-31"]])

    def test_an_inverted_date_window_is_rejected(self) -> None:
        """Terms that end before they begin can never apply."""
        with pytest.raises(RecordInvalidError):
            validate_record("PRICING", [["0.70"], ["2026-12-31"], ["2026-01-01"]])


class TestUnknownFiles:
    """A file with no declared layout is stored without structural checks."""

    def test_an_unvalidated_file_passes(self) -> None:
        """Only the files in the contract carry rules; others are stored as given."""
        validate_record("SOMETHING.ELSE", ["anything", ["at", "all"]])
