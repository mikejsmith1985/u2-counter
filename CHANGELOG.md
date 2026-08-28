# Changelog

All notable changes to this project are recorded here. This file is the single
source of truth for what changed (Article VI). Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- **Project scaffolding for the counter availability lookup.** A .NET 9 solution
  in `api/` with domain, infrastructure and API projects and two test projects; a
  React and TypeScript workspace in `web/`; a Python package in `mvstore/` for the
  demonstration MultiValue store.

- **`scripts/run-dev-clean.ps1`** — starts every service and records each process
  id with its start time in `.run/pids.json`, stopping only those recorded
  processes (Article II). Start times are compared before stopping, because
  operating systems reuse process ids and an id alone is not identity.

- **Code standards enforced as build failures rather than review notes**:
  `api/.editorconfig` makes the Article IV naming rules errors and treats nullable
  warnings and unpassed cancellation tokens as errors — the latter because a
  timeout that does not carry its token abandons the wait without stopping the
  work. `web/eslint.config.js` applies the same standards to TypeScript.
