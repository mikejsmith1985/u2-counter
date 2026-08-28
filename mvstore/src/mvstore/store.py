"""File-backed storage of MultiValue records in their genuine stored format.

Records are held with the real separators between fields, values and subvalues,
not as JSON that is dressed up on the way out. The application shows a raw record
to a user beside the structured form; a store that faked the format would make
that screen display something it never held.
"""

from __future__ import annotations

import threading
from collections.abc import Iterator
from contextlib import contextmanager
from pathlib import Path

# The three MultiValue separators, in their hierarchy.
AM = chr(254)  # Attribute mark: separates fields
VM = chr(253)  # Value mark: separates values within a field
SM = chr(252)  # Subvalue mark: separates sub-items within a value

# A field is either a plain string, a list of values, or a list of values where
# some are themselves lists of subvalues.
FieldValue = str | list[str] | list[str | list[str]]

# Written as UTF-8 so that accented names and currency symbols survive. The
# separators are code points 252 to 254, which UTF-8 encodes as two bytes each;
# that is fine, because nothing here indexes the file by byte offset.
_ENCODING = "utf-8"


class RecordNotFoundError(Exception):
    """Raised when a key is absent. Distinct from a record that is empty."""


class MultiValueStore:
    """Records on disk, one file per MultiValue file, one line per record.

    Args:
        root: Directory holding the data files. Created if absent.
    """

    def __init__(self, root: Path | str) -> None:
        self.root = Path(root)
        self.root.mkdir(parents=True, exist_ok=True)
        # Writes are serialized because a partially written file would be
        # indistinguishable from a corrupted one on the next read.
        self._lock = threading.RLock()
        # Files held in memory during a bulk write. Empty at every other time, so
        # a single write still persists immediately.
        self._pending: dict[str, dict[str, str]] = {}
        self._is_bulk_writing = False
        # One parsed copy per file, keyed by the file's modification time.
        self._file_cache: dict[str, tuple[int, dict[str, str]]] = {}

    @contextmanager
    def bulk_write(self) -> Iterator[None]:
        """Hold writes in memory until the block ends, then persist once per file.

        Each write otherwise rewrites its whole file, which makes writing n
        records quadratic: seeding three thousand parts took minutes rather than
        seconds. Reads inside the block see pending writes, because the seed
        generator reads back what it has just written.

        Whatever succeeded is persisted even if the block raises, so an
        interrupted seed leaves readable data rather than an empty file.

        Yields:
            None
        """
        with self._lock:
            self._is_bulk_writing = True
            try:
                yield
            finally:
                self._is_bulk_writing = False
                for file_name, records in self._pending.items():
                    self._save_now(file_name, records)
                self._pending.clear()

    # -- paths ---------------------------------------------------------------

    def _path_for(self, file_name: str) -> Path:
        """Return the path holding one MultiValue file's records."""
        return self.root / f"{file_name}.mv"

    # -- reading -------------------------------------------------------------

    def raw(self, file_name: str, record_id: str) -> str:
        """Return a record exactly as stored, separators included.

        Args:
            file_name: MultiValue file name
            record_id: Record key

        Returns:
            The stored record string

        Raises:
            RecordNotFoundError: If the file or the key is absent
        """
        records = self._load(file_name)
        if record_id not in records:
            raise RecordNotFoundError(f"'{record_id}' is not in '{file_name}'")
        return records[record_id]

    def read(self, file_name: str, record_id: str) -> list[FieldValue]:
        """Return a record parsed into fields, values and subvalues.

        Args:
            file_name: MultiValue file name
            record_id: Record key

        Returns:
            One entry per field, in order

        Raises:
            RecordNotFoundError: If the file or the key is absent
        """
        return parse_record(self.raw(file_name, record_id))

    def keys(self, file_name: str) -> list[str]:
        """Return every key in a file, or an empty list if the file is absent."""
        return list(self._load(file_name))

    def exists(self, file_name: str, record_id: str) -> bool:
        """Return whether a key is present."""
        return record_id in self._load(file_name)

    # -- writing -------------------------------------------------------------

    def write(self, file_name: str, record_id: str, fields: list[FieldValue]) -> None:
        """Store a record, replacing any record already under that key.

        Args:
            file_name: MultiValue file name
            record_id: Record key
            fields: One entry per field, in order
        """
        with self._lock:
            records = self._load(file_name)
            records[record_id] = build_record(fields)
            self._save(file_name, records)

    def delete(self, file_name: str, record_id: str) -> None:
        """Remove a record.

        Raises:
            RecordNotFoundError: If the key is absent
        """
        with self._lock:
            records = self._load(file_name)
            if record_id not in records:
                raise RecordNotFoundError(f"'{record_id}' is not in '{file_name}'")
            del records[record_id]
            self._save(file_name, records)

    # -- persistence ---------------------------------------------------------

    def _load(self, file_name: str) -> dict[str, str]:
        """Read every record in a file, keyed by record id.

        A file that does not exist holds no records, which is not an error: it is
        simply a file nothing has been written to yet.

        During a bulk write the in-memory copy is authoritative, so a caller
        reading back what it has just written sees it.
        """
        if file_name in self._pending:
            return self._pending[file_name]

        path = self._path_for(file_name)
        if not path.exists():
            return {}

        # Cached against the file's modification time. Without this, reading n
        # records re-reads and re-parses the whole file n times, which is
        # quadratic: listing three thousand products took thirty seconds.
        # Comparing the timestamp rather than trusting the cache means a file
        # changed by the seed script is still picked up.
        stamp = path.stat().st_mtime_ns
        cached = self._file_cache.get(file_name)
        if cached is not None and cached[0] == stamp:
            return cached[1]

        records: dict[str, str] = {}
        for line in path.read_text(encoding=_ENCODING).split("\n"):
            if not line:
                continue
            # The key is separated from the record by a tab, which cannot appear
            # in a MultiValue record: the store rejects one on write.
            key, _, body = line.partition("\t")
            records[key] = body

        self._file_cache[file_name] = (stamp, records)
        return records

    def _save(self, file_name: str, records: dict[str, str]) -> None:
        """Persist a file, or hold it in memory while a bulk write is open."""
        if self._is_bulk_writing:
            self._pending[file_name] = records
            return
        self._save_now(file_name, records)

    def _save_now(self, file_name: str, records: dict[str, str]) -> None:
        """Write every record in a file, replacing what was there."""
        lines = [f"{key}\t{body}" for key, body in records.items()]
        self._path_for(file_name).write_text("\n".join(lines) + "\n", encoding=_ENCODING)


def parse_record(raw: str) -> list[FieldValue]:
    """Split a stored record into fields, values and subvalues.

    A field containing no value mark stays a plain string rather than becoming a
    one-item list, because a caller reading field 1 of a product record wants a
    description, not a list holding a description.

    Args:
        raw: The record as stored

    Returns:
        One entry per field, in order
    """
    if raw == "":
        return []

    fields: list[FieldValue] = []
    for field in raw.split(AM):
        if VM in field:
            fields.append([_parse_value(value) for value in field.split(VM)])
        elif SM in field:
            # Subvalues without a value mark: rare, and legal. Typed explicitly
            # because a bare list[str] nested in a list is not the same type as
            # the union this function promises.
            single_value: list[str | list[str]] = [field.split(SM)]
            fields.append(single_value)
        else:
            fields.append(field)
    return fields


def _parse_value(value: str) -> str | list[str]:
    """Split one value into subvalues, or leave it as a string if it has none."""
    return value.split(SM) if SM in value else value


def build_record(fields: list[FieldValue]) -> str:
    """Assemble fields into a stored record.

    Trailing empty fields are dropped, because that is what a real MultiValue
    system stores; interior empty fields are kept, because dropping one would
    shift every field after it.

    Args:
        fields: One entry per field, in order

    Returns:
        The record as it will be stored
    """
    encoded = [_encode_field(field) for field in fields]

    while encoded and encoded[-1] == "":
        encoded.pop()

    return AM.join(encoded)


def _encode_field(field: FieldValue) -> str:
    """Encode one field, at whichever depth it carries."""
    if isinstance(field, list):
        return VM.join(
            SM.join(str(sub) for sub in value) if isinstance(value, list) else str(value)
            for value in field
        )
    return str(field)
