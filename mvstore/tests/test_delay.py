"""The store can be made slow, so a caller's timeout can be measured.

A real Universe under load answers slowly rather than not at all, and that is
the failure a calling application most needs to get right: the difference
between "we could not reach the stock data" and "there is none" is the
difference between ringing a customer back and telling them something untrue.

A store that only ever answers instantly cannot be used to check that, which is
why the delay exists and why it is tested rather than assumed.
"""

from __future__ import annotations

import time

import pytest

from mvstore import driver
from mvstore.store import MultiValueStore


@pytest.fixture
def store(tmp_path) -> MultiValueStore:
    """A store holding one record, which is all these tests read."""
    built = MultiValueStore(tmp_path)
    built.write("INVENTORY", "PART-1", [["DEN"], ["10"], ["0"], ["0"], ["A-01"]])

    return built


def test_reads_are_immediate_when_no_delay_is_set(store, monkeypatch) -> None:
    monkeypatch.delenv("MVSTORE_DELAY_MS", raising=False)

    session = driver.Session(store)
    inventory = driver.File("INVENTORY", session)

    started = time.perf_counter()
    inventory.read("PART-1")
    elapsed = time.perf_counter() - started

    # Generous, because the assertion is "not delayed", not "fast". A tighter
    # bound would fail on a loaded machine for a reason unrelated to the code.
    assert elapsed < 0.2


def test_a_configured_delay_is_applied_to_a_read(store, monkeypatch) -> None:
    monkeypatch.setenv("MVSTORE_DELAY_MS", "250")

    session = driver.Session(store)
    inventory = driver.File("INVENTORY", session)

    started = time.perf_counter()
    inventory.read("PART-1")
    elapsed = time.perf_counter() - started

    assert elapsed >= 0.25


def test_a_configured_delay_is_applied_to_a_query(store, monkeypatch) -> None:
    # Both paths, because the application reads records and runs queries, and a
    # delay on only one of them would leave the other untested against a slow
    # database.
    monkeypatch.setenv("MVSTORE_DELAY_MS", "250")

    session = driver.Session(store)
    command = driver.Command("LIST INVENTORY", session)

    started = time.perf_counter()
    command.run()
    elapsed = time.perf_counter() - started

    assert elapsed >= 0.25


@pytest.mark.parametrize("configured", ["", "   ", "not-a-number", "-100"])
def test_an_unusable_delay_setting_does_not_delay_and_does_not_raise(
    store, monkeypatch, configured: str
) -> None:
    # A misconfigured delay must not stop the store answering. Refusing to serve
    # because a diagnostic setting was mistyped would turn a typo into an outage.
    monkeypatch.setenv("MVSTORE_DELAY_MS", configured)

    session = driver.Session(store)
    inventory = driver.File("INVENTORY", session)

    started = time.perf_counter()
    record = inventory.read("PART-1")
    elapsed = time.perf_counter() - started

    assert record
    assert elapsed < 0.2


def test_the_switch_file_turns_the_delay_on_while_running(store, monkeypatch, tmp_path) -> None:
    """A store can start fast and become slow, which is how it happens."""
    switch = tmp_path / "delay.txt"
    monkeypatch.delenv("MVSTORE_DELAY_MS", raising=False)
    monkeypatch.setenv("MVSTORE_DELAY_FILE", str(switch))

    session = driver.Session(store)
    inventory = driver.File("INVENTORY", session)

    # Nothing yet: the file does not exist, so nothing has been asked for.
    started = time.perf_counter()
    inventory.read("PART-1")
    assert time.perf_counter() - started < 0.2

    switch.write_text("250", encoding="utf-8")

    started = time.perf_counter()
    inventory.read("PART-1")
    assert time.perf_counter() - started >= 0.25


def test_removing_the_switch_file_makes_the_store_fast_again(
    store, monkeypatch, tmp_path
) -> None:
    # The recovery path. A fault that cannot be turned off leaves the suite
    # unable to test anything after it.
    switch = tmp_path / "delay.txt"
    switch.write_text("250", encoding="utf-8")
    monkeypatch.setenv("MVSTORE_DELAY_FILE", str(switch))

    session = driver.Session(store)
    inventory = driver.File("INVENTORY", session)

    switch.unlink()

    started = time.perf_counter()
    inventory.read("PART-1")
    assert time.perf_counter() - started < 0.2
