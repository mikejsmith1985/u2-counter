"""Load the demonstration data into a real UniVerse account.

The demonstration store and a real UniVerse hold the same records in the same
format — attribute, value and subvalue marks, parallel fields — because the store
was built to that format rather than to something convenient. That is what makes
this script short: it reads records out of one and writes them into the other
without transforming anything.

It writes, and the MCP server does not. That is deliberate and worth being clear
about: loading a demonstration account is an administrator's job, done once,
before anyone uses the application. It talks to `uopy` directly rather than
through the server, so the server keeps its property of having no write path at
all — a loader that went through it would be an argument for adding one.

Usage:
    python scripts/load-universe.py --account /data/counter

    U2_HOST, U2_USER, U2_PASSWORD and U2_ACCOUNT are read from the environment,
    the same names the MCP server uses. The password is never printed.
"""

from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

# The demonstration store is the source. Imported rather than re-read, so the
# marks and the field order come from the one place that defines them.
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "mvstore" / "src"))

from mvstore.store import AM, MultiValueStore  # noqa: E402

# How each file is created. The numbers are UniVerse's own: type 18 is a dynamic
# hashed file, which is what almost every application file is, and the modulo and
# separation are starting sizes UniVerse will grow past on its own.
#
# Written out rather than defaulted so a reader can see that nothing clever is
# happening — these are ordinary files, created the ordinary way.
FILE_DEFINITIONS: dict[str, str] = {
    "PRODUCT": "CREATE.FILE PRODUCT 18 1009 4",
    "INVENTORY": "CREATE.FILE INVENTORY 18 1009 4",
    "BRANCH": "CREATE.FILE BRANCH 18 11 2",
    "CUSTOMER": "CREATE.FILE CUSTOMER 18 101 2",
    "PRICING": "CREATE.FILE PRICING 18 53 2",
    "ORDER": "CREATE.FILE ORDER 18 307 2",
}

# Reported every this many records, so a load of three thousand says something
# while it runs rather than appearing to have hung.
PROGRESS_EVERY = 250


def main() -> int:
    """Create the files and load them. Returns a process exit code."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--data",
        default=str(Path(__file__).resolve().parent.parent / "mvstore" / "data"),
        help="Where the demonstration store's files are",
    )
    parser.add_argument(
        "--drop",
        action="store_true",
        help="Delete each file before creating it. Refuses unless --i-mean-it",
    )
    parser.add_argument(
        "--i-mean-it",
        action="store_true",
        help="Confirm that --drop may destroy data in the target account",
    )
    arguments = parser.parse_args()

    if arguments.drop and not arguments.i_mean_it:
        # Two flags rather than one. This script points at whatever account the
        # environment names, and the environment is exactly the thing that is
        # wrong when somebody is loading demonstration data into the wrong place.
        print(
            "--drop deletes files in the account named by U2_ACCOUNT. "
            "Pass --i-mean-it as well if that is what you want.",
            file=sys.stderr,
        )
        return 2

    try:
        import uopy
    except ImportError:
        print(
            "uopy is not installed. It ships with the UniVerse client; install it "
            "into this environment before running the loader.",
            file=sys.stderr,
        )
        return 1

    missing = [name for name in ("U2_HOST", "U2_USER", "U2_PASSWORD", "U2_ACCOUNT")
               if not os.environ.get(name)]
    if missing:
        print(f"Not configured: {', '.join(missing)}", file=sys.stderr)
        return 1

    store = MultiValueStore(Path(arguments.data))

    # Named, and the password deliberately not. Whoever runs this needs to know
    # which account they are about to write into; nobody needs the password
    # echoed into a terminal history.
    print(f"Connecting to {os.environ['U2_ACCOUNT']} on {os.environ['U2_HOST']} "
          f"as {os.environ['U2_USER']}")

    session = uopy.connect(
        host=os.environ["U2_HOST"],
        user=os.environ["U2_USER"],
        password=os.environ["U2_PASSWORD"],
        account=os.environ["U2_ACCOUNT"],
    )

    try:
        for name, create in FILE_DEFINITIONS.items():
            _prepare_file(uopy, session, name, create, drop=arguments.drop)
            _load_file(uopy, session, store, name)
    finally:
        session.close()

    print("\nLoaded. The same records, in the same format, in a real UniVerse.")
    return 0


def _prepare_file(uopy, session, name: str, create: str, drop: bool) -> None:
    """Create one file, leaving an existing one alone unless asked to drop it."""
    if drop:
        _run(uopy, session, f"DELETE.FILE {name}", tolerate_failure=True)

    # A file that already exists makes CREATE.FILE fail, and that is not an
    # error worth stopping for: re-running the loader against a prepared account
    # is the normal case.
    _run(uopy, session, create, tolerate_failure=True)


def _load_file(uopy, session, store: MultiValueStore, name: str) -> None:
    """Write every record of one file, exactly as the store holds it."""
    keys = store.keys(name)
    print(f"\n{name}: {len(keys)} records")

    target = uopy.File(name, session=session)

    for index, key in enumerate(keys, start=1):
        # The stored form, not a parsed one. Parsing and rebuilding would put
        # this script's understanding of the format between the two databases,
        # and any mistake in it would look like a difference between them.
        raw = store.raw(name, key)

        target.write(key, uopy.DynArray(raw.split(AM)))

        if index % PROGRESS_EVERY == 0:
            print(f"  {index}/{len(keys)}")

    target.close()


def _run(uopy, session, command: str, tolerate_failure: bool = False) -> str:
    """Run one TCL command, returning what UniVerse printed."""
    try:
        instruction = uopy.Command(command, session=session)
        instruction.run()
        return instruction.response
    except Exception as error:  # noqa: BLE001 - uopy raises several unrelated types
        if tolerate_failure:
            return str(error)
        raise


if __name__ == "__main__":
    raise SystemExit(main())
