# mvstore

A demonstration MultiValue store. It holds records in genuine attribute, value
and subvalue format and answers the read verbs this feature uses — `LIST`,
`SELECT`, `SSELECT`, `COUNT`, and `WHO` for the health check the MCP server makes
every thirty seconds.

## Why it exists

There is no freely obtainable Universe instance that can be deployed to a cloud
host for evaluation, and licensing prevents redistributing one. Without a
stand-in, none of this feature could be demonstrated or tested end to end. That
gap is the justification recorded against Article VII in the implementation plan.

## Why it stores real delimiters

The application shows a raw inventory record to the user, beside the structured
form the screen is built from. If this store held JSON and formatted it as
MultiValue on the way out, that screen would display a rendering the store never
held — which makes the screen dishonest about the one thing it exists to prove.

So records are stored with `CHAR(254)`, `CHAR(253)` and `CHAR(252)` between
fields, values and subvalues, exactly as a real system stores them.

## What it is not

It is not a database. It answers four read verbs and nothing else, has no query
planner, no indexes, no transactions and no concurrency control beyond a lock
around writes. It is a test fixture that happens to be deployable.

## Running it

```bash
uv venv --python 3.12 .venv
uv pip install -e ".[dev]" --python .venv/Scripts/python.exe
.venv/Scripts/python.exe -m pytest
```
