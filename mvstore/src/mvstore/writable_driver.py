"""The demonstration store with a write path, kept apart from the read-only one.

Everything here is a deliberate second thing rather than a change to the first.
`driver.py` refuses every write unconditionally and continues to do so; nothing
in this file alters it. A deployment selects one or the other by name:

    U2_DRIVER=demo                        the read-only store (the default)
    U2_DRIVER=mvstore.writable_driver     the same store, writes allowed

That split is the whole design. The read-only guarantee elsewhere in this project
is stated as "no write path exists", and the honest way to add one is to leave
that sentence true of the thing it was said about. A reviewer can read
`driver.py`, see that its `write` raises unconditionally, and not have to take
anybody's word for what a flag does.

Why have a write path at all: "can it write?" is a fair question to ask of a
database tool, and answering it with a refusal only proves the refusal works. It
does not show whether the writing would be done correctly — which, for MultiValue,
is the part that is actually hard.

The correctness that matters here is field alignment. An INVENTORY record holds
parallel fields: position n of the branch list, the on-hand list and the committed
list all describe the same branch. Change one value by rebuilding the record
carelessly and every position after it shifts, so branch three's quantity becomes
branch four's. Nothing errors. The screen stays plausible. That is the failure
this module exists to make impossible, and `update_value` is the whole of it.
"""

from __future__ import annotations

import os
from typing import Any

# Re-exported unchanged. The server requires a driver to provide all of these,
# and the point is that only File differs -- a reader comparing the two modules
# should find one behaviour changed, not a second implementation to audit.
from .driver import (  # noqa: F401
    Command,
    List,
    Subroutine,
    UOError,
    connect,
)
from .driver import File as ReadOnlyFile
from .store import AM, SM, VM, build_record, parse_record


def is_writable() -> bool:
    """Whether writes are permitted in this process.

    Returns:
        True only when MVSTORE_WRITABLE is set to an affirmative value

    Remarks:
        Selecting this driver is not by itself permission to write. Two switches
        rather than one, because they answer different questions: the driver name
        says which code is loaded, and this says whether that code may act. A
        deployment that loaded this module by accident still writes nothing.

        Read fresh on each call rather than captured at import, so a test can
        enable writes for one case and expect them refused in the next.
    """
    return os.environ.get("MVSTORE_WRITABLE", "").strip().lower() in {"1", "true", "yes"}


class File(ReadOnlyFile):
    """A file that can be written to, when the process permits it.

    Args:
        name: MultiValue file name
        session: The session to read and write through
    """

    def write(self, record_id: str, data: Any) -> None:
        """Replace a record.

        Args:
            record_id: The key to write
            data: The record, as a marked string or as a list of fields

        Raises:
            UOError: If writes are not permitted, the file is a dictionary, or
                the record would corrupt the file
        """
        self._refuse_unless_writable()

        fields = parse_record(data) if isinstance(data, str) else list(data)

        try:
            self.session.store.write(self._store_name, record_id, fields)
        except ValueError as error:
            # The store refuses a record carrying a tab or a line break, because
            # either would end the row it is stored on and silently truncate
            # everything after it.
            raise UOError(str(error)) from error

    def delete(self, record_id: str) -> None:
        """Remove a record.

        Args:
            record_id: The key to remove

        Raises:
            UOError: If writes are not permitted, or the record is absent
        """
        self._refuse_unless_writable()

        try:
            self.session.store.delete(self._store_name, record_id)
        except Exception as error:  # noqa: BLE001 - reported as the driver's own error
            raise UOError(str(error)) from error

    def update_value(self, record_id: str, position: int, index: int, value: str) -> str:
        """Change one value of one field, leaving every other position where it was.

        Args:
            record_id: The key to change
            position: Which field, counting from one
            index: Which value within that field, counting from zero
            value: What to put there

        Returns:
            The record as it now stands, in its stored form

        Raises:
            UOError: If writes are not permitted, or the position does not exist

        Remarks:
            This exists because the obvious way to change a value is wrong in a
            way nothing reports.

            An INVENTORY record holds parallel fields: position n of the branch
            list, the on-hand list and the committed list all describe the same
            branch. Rebuild the record by splitting on marks and joining the
            pieces back together, and any field that happened to be shorter than
            the others -- a bin location nobody recorded, say -- comes back a
            value short. Every position after it shifts up by one, so branch
            three's quantity is now read against branch four's name.

            Nothing errors. The record is well formed. The screen is plausible
            and wrong, and the first anybody hears of it is a customer being
            promised stock that is somewhere else.

            So this changes exactly one value in place: it refuses a position
            that does not exist rather than creating it, refuses an index past
            the end of the field rather than padding to reach it, and leaves the
            length of every other field untouched. The caller reads the record
            back and checks -- `write_and_verify` in the application above does
            precisely that.
        """
        self._refuse_unless_writable()

        fields = parse_record(self.read(record_id))

        if position < 1 or position > len(fields):
            raise UOError(
                f"Field {position} does not exist in '{record_id}': the record has "
                f"{len(fields)} field(s). Refusing to create one -- a record that "
                "grew a field nobody asked for is how a layout drifts."
            )

        existing = fields[position - 1]
        values = existing if isinstance(existing, list) else [existing]

        if index < 0 or index >= len(values):
            raise UOError(
                f"Value {index} does not exist in field {position} of '{record_id}': "
                f"that field holds {len(values)} value(s). Refusing to pad -- padding "
                "one parallel field and not its siblings is what puts a quantity "
                "against the wrong branch."
            )

        # Every separator, not only the value mark.
        #
        # This checked VM and not AM, which is the wrong one to miss: a value
        # mark adds a value to a field, and an attribute mark splits the field
        # into two. Writing one<AM>two into field one of a four-field record
        # produced a five-field record -- every later read agreeing with it,
        # and every field after the first describing something it is not.
        #
        # It was caught afterwards by the alignment check and reported as
        # "written, but a field changed length", which is honest and far too
        # late. A write that has to be explained is one that should have been
        # refused.
        if any(mark in value for mark in (AM, VM, SM)) or any(
            character in value for character in ("\t", "\n", "\r")
        ):
            raise UOError(
                "A value cannot contain a separator or a line break: it would change "
                "the record's shape rather than its contents."
            )

        changed = list(values)
        changed[index] = value

        # Written back in the same shape it was read in. A field that held one
        # value must not become a list of one, because the record's text would
        # change without its meaning changing -- and the raw view shows that text.
        fields[position - 1] = changed if isinstance(existing, list) else changed[0]

        self.session.store.write(self._store_name, record_id, fields)

        return build_record(fields)

    def _refuse_unless_writable(self) -> None:
        """Stop unless this process permits writes and the target is data.

        Raises:
            UOError: If writes are off, or the target is a dictionary
        """
        if not is_writable():
            raise UOError(
                f"'{self.name}' is read-only. This driver can write, but "
                "MVSTORE_WRITABLE is not set -- selecting the driver is not the "
                "same as permitting a write."
            )

        if self._is_dictionary:
            # A dictionary describes the file's shape, so changing one is a
            # schema change. Nothing in this demonstration has any business
            # making one, and a caller that meant to has picked the wrong tool.
            raise UOError(f"'{self.name}' is a dictionary and is not writable here.")
