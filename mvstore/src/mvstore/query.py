"""The read verbs this feature uses, and nothing else.

`LIST`, `SELECT`, `SSELECT` and `COUNT`. Every other verb is refused here as
well as at the MCP server in front of this store. Two independent refusals are
deliberate: the server's protects a real database, and this one makes the
server's own enforcement testable end to end against a store that would not
comply even if asked.

Selection follows MultiValue semantics rather than relational ones. A record
matches a criterion when *any* value in the named field matches, because one
inventory record holds every branch and asking for stock at Aurora must find the
record that mentions Aurora at any position.
"""

from __future__ import annotations

import os
import re
from dataclasses import dataclass, field
from typing import Any

from .dictionaries import is_dictionary_name, store_name_for
from .store import MultiValueStore

# The only verbs this store answers.
#
# WHO is here because the MCP server asks it every thirty seconds to check the
# connection is alive. A store that refuses it makes the server log a health
# check failure on a connection that is perfectly healthy -- and a log full of
# warnings nobody should act on is a log nobody reads when something is wrong.
#
# It is a read in every sense that matters: it names the account and nothing else.
READ_VERBS = frozenset({"LIST", "SELECT", "SSELECT", "COUNT", "WHO"})

# Verbs that answer about the session rather than about a file.
SESSION_VERBS = frozenset({"WHO"})

# `F3` names field 3. Universe uses dictionary names; this store uses positions,
# because it has no dictionary and inventing one would be a second thing to keep
# correct.
_FIELD_REFERENCE = re.compile(r"^F(\d+)$", re.IGNORECASE)

# Splits a query into words while keeping quoted values whole.
_TOKENS = re.compile(r'"[^"]*"|\S+')


class QueryError(Exception):
    """Raised when a query is malformed or names a verb this store will not run."""


@dataclass
class QueryResult:
    """What a query produced.

    Attributes:
        verb: The verb that ran
        file_name: The file it ran against
        record_ids: Matching keys, empty for COUNT
        count: How many records matched
        is_complete: False when a SAMPLE clause capped the result
        is_keys_only: True when the query asked for `@ID` alone, so a listing
            prints keys without their records
    """

    verb: str
    file_name: str
    record_ids: list[str] = field(default_factory=list)
    count: int = 0
    is_keys_only: bool = False
    is_complete: bool = True


@dataclass
class _Criterion:
    """One `WITH F<n> = "value" ...` clause."""

    field_index: int
    accepted_values: list[str]


def _answer_session_verb(verb: str) -> QueryResult:
    """Answer a verb that describes the session rather than a file.

    Args:
        verb: The verb, already upper-cased and known to be a session verb

    Returns:
        A result naming the session rather than any file
    """
    account = os.environ.get("U2_ACCOUNT", "DEMO")

    # No file, and no records. The caller wants to know the session is alive;
    # naming the account is the whole of the answer.
    return QueryResult(verb=verb, file_name=account, record_ids=[], count=1)


def run_query(store: MultiValueStore, query: str) -> QueryResult:
    """Run a read query against the store.

    Args:
        store: The store to read
        query: The query text

    Returns:
        The matching keys, or a count

    Raises:
        QueryError: If the query is empty, malformed, or names a refused verb
    """
    tokens = _TOKENS.findall(query.strip())
    if not tokens:
        raise QueryError("A query cannot be empty")

    verb = tokens[0].upper()
    if verb not in READ_VERBS:
        raise QueryError(
            f"'{verb}' is not a read verb this store answers. "
            f"Permitted: {', '.join(sorted(READ_VERBS))}"
        )

    if verb in SESSION_VERBS:
        return _answer_session_verb(verb)

    if len(tokens) < 2:
        raise QueryError(f"'{verb}' names no file to act on")

    # `DICT PRODUCT` is one file name written as two words. Taking only the
    # first token left the file as "DICT" and pushed the real name into the
    # criteria, where it failed as an unexpected word -- so every attempt to list
    # a dictionary reported a malformed query rather than a missing feature.
    if tokens[1].upper() == "DICT" and len(tokens) > 2:
        file_name = f"DICT {tokens[2].upper()}"
        remaining = tokens[3:]
    else:
        file_name = tokens[1].upper()
        remaining = tokens[2:]

    # Output field names say what to display, not what to match. `@ID` asks for
    # the keys alone, which is what UniVerse then prints -- one key per line and
    # nothing else. Recorded rather than discarded, because a caller who asked
    # for keys and received whole records has to take the records apart again,
    # and the tools that ask this way parse what comes back by line.
    #
    # Only @-prefixed names are recognised, so an ordinary typo is still reported
    # rather than quietly ignored.
    output_fields = [token.upper() for token in remaining if token.startswith("@")]
    remaining = [token for token in remaining if not token.startswith("@")]
    is_keys_only = output_fields == ["@ID"]

    explicit_keys, remaining = _take_explicit_keys(remaining)
    sample_limit, remaining = _take_sample_limit(remaining)
    criteria = _parse_criteria(remaining)

    # The name the caller used stays on the result, because that is what they
    # asked for; the store is read under the name it actually holds.
    store_file = store_name_for(file_name) if is_dictionary_name(file_name) else file_name

    candidates = explicit_keys if explicit_keys else store.keys(store_file)
    matching = [key for key in candidates if _matches(store, store_file, key, criteria)]
    matching.sort()

    if verb == "COUNT":
        return QueryResult(verb=verb, file_name=file_name, count=len(matching))

    is_complete = True
    if sample_limit is not None and len(matching) > sample_limit:
        matching = matching[:sample_limit]
        is_complete = False

    return QueryResult(
        verb=verb,
        file_name=file_name,
        record_ids=matching,
        count=len(matching),
        is_complete=is_complete,
        is_keys_only=is_keys_only,
    )


def _take_explicit_keys(tokens: list[str]) -> tuple[list[str], list[str]]:
    """Take leading record keys, which appear before any clause.

    Returns:
        The keys taken, and the tokens that remain
    """
    keys: list[str] = []
    index = 0
    while index < len(tokens) and tokens[index].upper() not in {"WITH", "AND", "SAMPLE", "BY"}:
        keys.append(tokens[index].strip('"'))
        index += 1
    return keys, tokens[index:]


def _take_sample_limit(tokens: list[str]) -> tuple[int | None, list[str]]:
    """Take a `SAMPLE <n>` clause from anywhere in the remaining tokens.

    Returns:
        The limit if present, and the tokens that remain
    """
    for index, token in enumerate(tokens):
        if token.upper() != "SAMPLE":
            continue
        if index + 1 >= len(tokens) or not tokens[index + 1].isdigit():
            raise QueryError("SAMPLE must be followed by a count")
        return int(tokens[index + 1]), tokens[:index] + tokens[index + 2 :]
    return None, tokens


def _parse_criteria(tokens: list[str]) -> list[_Criterion]:
    """Parse every `WITH F<n> = "value" ...` clause.

    Args:
        tokens: Tokens remaining after the verb, file, keys and sample clause

    Returns:
        One criterion per WITH clause

    Raises:
        QueryError: If a clause is malformed or names a field by a name rather
            than a position
    """
    criteria: list[_Criterion] = []
    index = 0

    while index < len(tokens):
        token = tokens[index].upper()

        if token in {"AND", "BY"}:
            index += 1
            continue

        if token != "WITH":
            raise QueryError(f"Unexpected '{tokens[index]}' in query")

        if index + 2 >= len(tokens) or tokens[index + 2] != "=":
            raise QueryError('A WITH clause must read: WITH F<n> = "value"')

        criteria.append(_criterion_from(tokens[index + 1], tokens[index + 3 :]))
        index += 3 + len(criteria[-1].accepted_values)

    return criteria


def _criterion_from(field_token: str, value_tokens: list[str]) -> _Criterion:
    """Build one criterion from its field reference and the values after it."""
    match = _FIELD_REFERENCE.match(field_token)
    if match is None:
        raise QueryError(
            f"'{field_token}' is not a field reference. This store names fields by "
            "position, as F1, F2 and so on"
        )

    accepted: list[str] = []
    for token in value_tokens:
        if not token.startswith('"'):
            break
        accepted.append(token.strip('"'))

    if not accepted:
        raise QueryError(f"WITH {field_token} names no value to match")

    return _Criterion(field_index=int(match.group(1)), accepted_values=accepted)


def _matches(
    store: MultiValueStore, file_name: str, record_id: str, criteria: list[_Criterion]
) -> bool:
    """Return whether one record satisfies every criterion."""
    if not criteria:
        return store.exists(file_name, record_id)

    try:
        fields = store.read(file_name, record_id)
    except Exception:
        return False

    return all(_field_matches(fields, criterion) for criterion in criteria)


def _field_matches(fields: list[Any], criterion: _Criterion) -> bool:
    """Return whether any value in the named field matches the criterion.

    Any rather than all: one inventory record holds every branch, so a record
    mentioning Aurora at position two matches a query for Aurora.
    """
    position = criterion.field_index - 1
    if position < 0 or position >= len(fields):
        return False

    return any(value in criterion.accepted_values for value in _flatten(fields[position]))


def _flatten(field_value: object) -> list[str]:
    """Return every scalar in a field, at whatever depth it sits."""
    if isinstance(field_value, list):
        flattened: list[str] = []
        for item in field_value:
            flattened.extend(_flatten(item))
        return flattened
    return [str(field_value)]
