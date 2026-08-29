"""A file name names a file in the store, and cannot reach outside it.

The store keeps one file per MultiValue file, and the file name arrives from the
caller: the MCP server exposes it as a tool parameter, so it is reachable input
rather than something the application always chooses itself.

Joined onto the root without checking, that name escapes trivially. `../` walks
up, and on Windows an absolute path does not join at all -- it replaces the root
outright, which is a pathlib behaviour that is easy to forget and reads as
harmless code.

The only thing that limited this was the `.mv` suffix the store appends, so a
reader could take any `.mv` file on the filesystem but nothing else. That is a
real limit and it is an accident, which is not the same as a control.
"""

from __future__ import annotations

import pytest

from mvstore.store import MultiValueStore, RecordNotFoundError

# Each of these is a name that must be refused rather than resolved. Kept as one
# list because the rule is the same for every entry: a file name is one segment.
ESCAPING_NAMES = [
    "../outside/SECRET",
    r"..\outside\SECRET",
    "../../etc/shadow",
    "sub/INVENTORY",
    r"sub\INVENTORY",
    "C:/Windows/System32/config/SAM",
    r"C:\Windows\System32\config\SAM",
    "/etc/passwd",
    r"\\server\share\FILE",
    "..",
    ".",
    "",
]


@pytest.fixture
def store(tmp_path):
    """A store with a file planted outside its root."""
    root = tmp_path / "data"
    root.mkdir()

    outside = tmp_path / "outside"
    outside.mkdir()
    (outside / "SECRET.mv").write_text("K-1\tpassword-material\n", encoding="utf-8")

    return MultiValueStore(root)


class TestReadingCannotLeaveTheRoot:
    @pytest.mark.parametrize("file_name", ESCAPING_NAMES)
    def test_a_name_that_is_not_one_segment_is_refused(self, store, file_name):
        with pytest.raises(ValueError):
            store.raw(file_name, "K-1")

    @pytest.mark.parametrize("file_name", ESCAPING_NAMES)
    def test_the_parsed_read_refuses_it_too(self, store, file_name):
        # Both entry points, because a caller reaching the store through the
        # other one would otherwise be unprotected.
        with pytest.raises(ValueError):
            store.read(file_name, "K-1")

    def test_the_planted_file_is_genuinely_readable_by_its_real_name(self, store, tmp_path):
        # Proving the test above rejects the traversal rather than merely
        # failing to find anything: under its own root the same file reads fine.
        reachable = MultiValueStore(tmp_path / "outside")
        assert reachable.raw("SECRET", "K-1") == "password-material"


class TestWritingCannotLeaveTheRoot:
    @pytest.mark.parametrize("file_name", ESCAPING_NAMES)
    def test_a_name_that_is_not_one_segment_is_refused(self, store, file_name):
        with pytest.raises(ValueError):
            store.write(file_name, "K-1", ["planted"])

    def test_nothing_is_created_outside_the_root(self, store, tmp_path):
        target = tmp_path / "planted"

        with pytest.raises(ValueError):
            store.write(str(target), "K-1", ["planted"])

        assert not target.with_suffix(".mv").exists()


class TestEveryOtherEntryPointIsGuardedToo:
    """One unguarded method is the whole guard, so each is covered by name."""

    @pytest.mark.parametrize("file_name", ESCAPING_NAMES)
    def test_listing_keys_refuses_an_escaping_name(self, store, file_name):
        with pytest.raises(ValueError):
            store.keys(file_name)

    @pytest.mark.parametrize("file_name", ESCAPING_NAMES)
    def test_asking_whether_a_record_exists_refuses_it(self, store, file_name):
        # This one leaks by its answer rather than its return value: left
        # unguarded it reports whether an arbitrary file holds a given key.
        with pytest.raises(ValueError):
            store.exists(file_name, "K-1")

    @pytest.mark.parametrize("file_name", ESCAPING_NAMES)
    def test_deleting_refuses_it(self, store, file_name):
        with pytest.raises(ValueError):
            store.delete(file_name, "K-1")


class TestOrdinaryNamesStillWork:
    """The guard must not cost the store its actual job."""

    @pytest.mark.parametrize("file_name", ["INVENTORY", "PRODUCTS", "SALES.ORDER", "A_B-1"])
    def test_a_real_file_name_is_accepted(self, store, file_name):
        store.write(file_name, "K-1", ["kept"])

        assert store.read(file_name, "K-1") == ["kept"]

    def test_a_missing_file_still_reports_the_record_as_absent(self, store):
        # Distinct from a refusal: this name is allowed, there is simply nothing
        # under it, and the caller needs to be able to tell those apart.
        with pytest.raises(RecordNotFoundError):
            store.read("NOSUCHFILE", "K-1")
