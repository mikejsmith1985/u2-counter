"""A uopy-compatible surface over the demonstration store.

The MCP server talks to Universe through `uopy`. This module presents the same
shapes — `connect`, `File`, `Command`, `List`, `UOError` — backed by the
demonstration store instead, so the server can be evaluated without a database.

It is deliberately the same objects the server already calls, rather than a new
interface the server would need to learn. A seam that changes the caller is a
seam that changes what is being tested.
"""

from __future__ import annotations

import os
from pathlib import Path
from typing import Any

from .query import QueryError, run_query
from .store import AM, MultiValueStore, RecordNotFoundError, build_record

# Where the demonstration data lives, unless told otherwise.
DEFAULT_DATA_PATH = Path(__file__).resolve().parent.parent.parent / "data"


class UOError(Exception):
    """Stands in for `uopy.UOError`."""


def _store_path() -> Path:
    """Return the data directory, from the environment or the default."""
    configured = os.environ.get("MVSTORE_DATA_PATH")
    return Path(configured) if configured else DEFAULT_DATA_PATH


class Session:
    """Stands in for a uopy session.

    Args:
        store: The store this session reads
    """

    def __init__(self, store: MultiValueStore) -> None:
        self.store = store
        self.is_active = True
        # A select list is held per session, exactly as Universe holds one, so
        # the SELECT-then-iterate pattern the server uses behaves the same way.
        self.select_list: list[str] = []

    def close(self) -> None:
        """Close the session."""
        self.is_active = False

    def tx_start(self) -> None:
        """Begin a transaction.

        The demonstration store is read-only in practice, so a transaction has
        nothing to protect. It is accepted rather than refused so that the
        server's own transaction handling can still be exercised.
        """

    def tx_commit(self) -> None:
        """Commit a transaction."""

    def tx_rollback(self) -> None:
        """Roll back a transaction."""


def connect(**kwargs: Any) -> Session:
    """Open a session against the demonstration store.

    Connection arguments are accepted and ignored: there is no server to reach,
    no credentials to check, and pretending otherwise would suggest an
    authentication that is not happening.

    Returns:
        A session
    """
    return Session(MultiValueStore(_store_path()))


class File:
    """Stands in for `uopy.File`.

    Args:
        name: MultiValue file name
        session: The session to read through

    Raises:
        UOError: If the file holds no records, which is how Universe reports a
            file that does not exist
    """

    def __init__(self, name: str, session: Session | None = None, **kwargs: Any) -> None:
        if session is None:
            raise UOError("A file needs a session")

        self.name = name
        self.session = session

        # `DICT CUSTOMERS` names the dictionary of CUSTOMERS. The demonstration
        # store has no dictionaries, so those reads find nothing rather than
        # returning a fiction.
        self._is_dictionary = name.upper().startswith("DICT ")

        if not self._is_dictionary and not session.store.keys(name):
            raise UOError(f"File '{name}' does not exist")

    def read(self, record_id: str) -> str:
        """Read a record, returning it in its stored form.

        Raises:
            UOError: If the record is absent
        """
        if self._is_dictionary:
            raise UOError(f"'{record_id}' is not in '{self.name}'")

        try:
            return self.session.store.raw(self.name, record_id)
        except RecordNotFoundError as error:
            raise UOError(str(error)) from error

    def write(self, record_id: str, data: Any) -> None:
        """Refuse to write.

        The demonstration store exists to be read. Refusing here means the
        server's read-only enforcement is tested against a store that would not
        comply even if the enforcement failed.

        Raises:
            UOError: Always
        """
        raise UOError(
            f"'{self.name}' is read-only. The demonstration store answers reads only."
        )

    def delete(self, record_id: str) -> None:
        """Refuse to delete.

        Raises:
            UOError: Always
        """
        raise UOError(
            f"'{self.name}' is read-only. The demonstration store answers reads only."
        )

    def close(self) -> None:
        """Close the file handle."""


class Command:
    """Stands in for `uopy.Command`.

    Args:
        command_text: The command to run
        session: The session to run it through
    """

    def __init__(self, command_text: str = "", session: Session | None = None) -> None:
        if session is None:
            raise UOError("A command needs a session")

        self.command_text = command_text
        self.session = session
        self.response = ""

    def run(self) -> None:
        """Run the command, setting `response` to what Universe would print.

        Raises:
            UOError: If the command is not one the store answers
        """
        try:
            result = run_query(self.session.store, self.command_text)
        except QueryError as error:
            raise UOError(str(error)) from error

        if result.verb == "COUNT":
            self.response = f"{result.count} records counted."
            return

        # SELECT and SSELECT leave a list for the session to iterate, and print a
        # summary rather than the records — as Universe does.
        if result.verb in {"SELECT", "SSELECT"}:
            self.session.select_list = list(result.record_ids)
            self.response = f"{result.count} records selected to list 0."
            return

        self.response = self._format_records(result.record_ids)

    def _format_records(self, record_ids: list[str]) -> str:
        """Render records the way a LIST prints them.

        Fields are joined with attribute marks rather than spaces, because the
        server converts those marks itself and would otherwise receive text it
        cannot separate back into fields.
        """
        file_name = self.command_text.split()[1].upper()
        lines: list[str] = []

        for record_id in record_ids:
            try:
                fields = self.session.store.read(file_name, record_id)
            except RecordNotFoundError:
                continue
            lines.append(f"{record_id}{AM}{build_record(fields)}")

        lines.append(f"{len(record_ids)} records listed.")
        return "\n".join(lines)


class List:
    """Stands in for `uopy.List`, iterating the session's select list.

    Args:
        session: The session holding the list
    """

    def __init__(self, session: Session | None = None, **kwargs: Any) -> None:
        if session is None:
            raise UOError("A list needs a session")

        self.session = session
        self._remaining = list(session.select_list)
        self._position = 0

    def next(self) -> str | None:
        """Return the next record id, or None when the list is exhausted."""
        if self._position >= len(self._remaining):
            return None

        record_id = self._remaining[self._position]
        self._position += 1
        return record_id


class Subroutine:
    """Stands in for `uopy.Subroutine`.

    The demonstration store runs no BASIC. Calling one raises, so a server
    configured to use this driver cannot appear to execute business logic that
    does not exist.
    """

    def __init__(self, name: str = "", num_args: int = 0, session: Session | None = None) -> None:
        self.name = name
        self.num_args = num_args
        self.args = [""] * num_args

    def set_arg(self, index: int, value: Any) -> None:
        """Set an argument."""
        self.args[index] = str(value)

    def get_arg(self, index: int) -> str:
        """Read an argument."""
        return self.args[index]

    def call(self) -> None:
        """Refuse to run.

        Raises:
            UOError: Always
        """
        raise UOError(
            f"'{self.name}' cannot run: the demonstration store executes no BASIC programs."
        )
