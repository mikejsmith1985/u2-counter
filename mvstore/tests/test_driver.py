"""The driver must present exactly the shapes the MCP server already calls.

A seam that changes the caller changes what is being tested, so these assert the
uopy surface the server uses: connect, File, Command, List, and a UOError raised
where Universe would raise one.
"""

import pytest

from mvstore import driver
from mvstore.seed import generate
from mvstore.store import AM, MultiValueStore


@pytest.fixture(scope="module")
def data_path(tmp_path_factory):
    """A seeded data directory the driver reads."""
    path = tmp_path_factory.mktemp("driver-data")
    generate(MultiValueStore(path), seed=7, parts=40, branches=6, customers=8, orders=20)
    return path


@pytest.fixture
def session(data_path, monkeypatch):
    """A driver session pointed at the seeded data."""
    monkeypatch.setenv("MVSTORE_DATA_PATH", str(data_path))
    return driver.connect(host="ignored", user="ignored", password="ignored", account="DEMO")


class TestSession:
    """Connecting and disconnecting."""

    def test_connecting_yields_an_active_session(self, session) -> None:
        """A session starts usable."""
        assert session.is_active is True

    def test_closing_deactivates_it(self, session) -> None:
        """The server checks `is_active` before reusing a session."""
        session.close()

        assert session.is_active is False

    def test_credentials_are_accepted_and_ignored(self, data_path, monkeypatch) -> None:
        """There is no server to authenticate against, and no pretence that there is."""
        monkeypatch.setenv("MVSTORE_DATA_PATH", str(data_path))

        assert driver.connect(user="anyone", password="anything").is_active is True


class TestFile:
    """Opening and reading files."""

    def test_a_known_file_opens(self, session) -> None:
        """A file holding records opens without complaint."""
        assert driver.File("PRODUCT", session=session).name == "PRODUCT"

    def test_an_unknown_file_raises(self, session) -> None:
        """Universe reports a missing file, so this does too."""
        with pytest.raises(driver.UOError):
            driver.File("NOSUCHFILE", session=session)

    def test_a_record_reads_back_in_its_stored_form(self, session) -> None:
        """The server parses attribute marks itself, so it must receive them."""
        handle = driver.File("PRODUCT", session=session)
        key = session.store.keys("PRODUCT")[0]

        assert AM in handle.read(key)

    def test_a_missing_record_raises(self, session) -> None:
        """Not found is an error, not an empty record."""
        with pytest.raises(driver.UOError):
            driver.File("PRODUCT", session=session).read("NOSUCH")


class TestReadOnly:
    """The store answers reads. Writes are refused at the driver."""

    def test_writing_is_refused(self, session) -> None:
        """Refusing here tests the server's enforcement against a store that would
        not comply even if that enforcement failed."""
        with pytest.raises(driver.UOError):
            driver.File("PRODUCT", session=session).write("P1", "anything")

    def test_deleting_is_refused(self, session) -> None:
        """Deletion is refused for the same reason."""
        with pytest.raises(driver.UOError):
            driver.File("PRODUCT", session=session).delete("P1")

    def test_a_subroutine_cannot_run(self, session) -> None:
        """No BASIC exists here, so none can appear to execute."""
        with pytest.raises(driver.UOError):
            driver.Subroutine("GET.CUSTOMER", 2, session=session).call()


class TestCommand:
    """Running the read verbs."""

    def test_count_reports_a_figure(self, session) -> None:
        """COUNT prints a count, as Universe does."""
        command = driver.Command("COUNT PRODUCT", session=session)
        command.run()

        assert "records counted" in command.response

    def test_list_returns_records_with_their_marks(self, session) -> None:
        """LIST output carries attribute marks for the server to separate."""
        command = driver.Command("LIST BRANCH", session=session)
        command.run()

        assert AM in command.response
        assert "records listed" in command.response

    def test_select_fills_the_session_list(self, session) -> None:
        """SELECT leaves a list for List() to iterate, as Universe does."""
        command = driver.Command("SELECT PRODUCT", session=session)
        command.run()

        assert "records selected" in command.response
        assert session.select_list

    def test_a_refused_verb_raises(self, session) -> None:
        """The store refuses anything that is not a read verb."""
        with pytest.raises(driver.UOError):
            driver.Command("SH -c id", session=session).run()

    def test_a_destructive_verb_raises(self, session) -> None:
        """Even if the server's blocklist failed, this would still refuse."""
        with pytest.raises(driver.UOError):
            driver.Command("DELETE.FILE PRODUCT", session=session).run()


class TestList:
    """Iterating a select list."""

    def test_the_list_yields_every_selected_record(self, session) -> None:
        """Iteration returns each id once, then None."""
        driver.Command("SELECT BRANCH", session=session).run()
        select = driver.List(session=session)

        collected = []
        while (record_id := select.next()) is not None:
            collected.append(record_id)

        assert sorted(collected) == sorted(session.store.keys("BRANCH"))

    def test_an_empty_list_yields_nothing(self, session) -> None:
        """With nothing selected, the first call returns None rather than raising."""
        assert driver.List(session=session).next() is None

class TestListingKeysAlone:
    """`LIST X @ID` prints keys and nothing else, as Universe does.

    A caller asking that way is parsing for keys. Printing whole records
    meant every line began with an attribute mark, so a reader taking the
    first word of each line took an entire record as though it were a key --
    and then looked those up, and found nothing.

    The fix was in place and had no test. This is the shape of defect that
    comes back, because the obvious change -- print the records, they are
    more useful -- looks like an improvement.
    """

    def test_only_the_keys_are_printed(self, session) -> None:
        """No marks, because there are no records on these lines."""
        command = driver.Command("LIST BRANCH @ID", session=session)
        command.run()

        assert AM not in command.response

    def test_each_line_is_a_key_a_caller_could_read_back(self, session) -> None:
        """The point of the format: the first word of a line is the key."""
        command = driver.Command("LIST BRANCH @ID", session=session)
        command.run()

        keys = [line for line in command.response.splitlines() if "records listed" not in line]
        handle = driver.File("BRANCH", session=session)

        for key in keys:
            assert handle.read(key.strip()), f"{key!r} is not a key that reads back"

    def test_it_still_closes_with_the_summary_universe_prints(self, session) -> None:
        """Callers look for that line to know the listing ended."""
        command = driver.Command("LIST BRANCH @ID", session=session)
        command.run()

        assert "records listed" in command.response


class TestAskingWithoutASession:
    """Every handle needs one, and says so rather than failing later.

    These are the "you have called this wrong" guards. Without them the
    failure arrives later as an attribute error on None, somewhere that has
    nothing to do with the mistake.
    """

    def test_a_file_needs_a_session(self) -> None:
        with pytest.raises(driver.UOError, match="session"):
            driver.File("BRANCH")

    def test_a_command_needs_a_session(self) -> None:
        with pytest.raises(driver.UOError, match="session"):
            driver.Command("LIST BRANCH")

    def test_a_select_list_needs_a_session(self) -> None:
        with pytest.raises(driver.UOError, match="session"):
            driver.List()

class TestAListingOfSomethingThatMoved:
    """The account is live between the selection and the read.

    A listing selects keys and then reads each one, which is two passes over
    a file somebody else may be writing to. A record deleted in between is
    ordinary, and it must not take the listing down with it -- the other
    records are still the answer to what was asked.
    """

    def test_a_record_that_vanished_is_skipped_and_the_rest_still_listed(
        self, session, tmp_path
    ) -> None:
        """One missing record must not empty the listing."""
        keys = sorted(session.store.keys("BRANCH"))
        assert len(keys) > 1, "the fixture needs more than one branch"

        command = driver.Command("LIST BRANCH", session=session)
        command.run()

        # Formatted directly, so the record can be removed between the two
        # passes the way a live account would remove it.
        session.store.delete("BRANCH", keys[0])

        listing = command._format_records("BRANCH", keys)

        assert keys[0] not in listing.split(chr(10))[0]

        for surviving in keys[1:]:
            assert surviving in listing, f"{surviving} was lost with {keys[0]}"

    def test_the_count_still_reports_what_was_selected(self, session) -> None:
        """The selection matched them; the read is a separate question.

        Reporting the smaller number would say the selection matched fewer
        records than it did, which is a claim about the criteria rather than
        about what happened afterwards.
        """
        keys = sorted(session.store.keys("BRANCH"))
        command = driver.Command("LIST BRANCH", session=session)
        command.run()

        session.store.delete("BRANCH", keys[0])

        assert f"{len(keys)} records listed" in command._format_records("BRANCH", keys)

