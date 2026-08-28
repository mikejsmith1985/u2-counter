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
