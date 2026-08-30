/**
 * The instruction somebody hands to their own coding agent to do this setup for them.
 *
 * The honest reason this exists: the setup is four environment variables and one
 * command, and it is still the point where most people stop. Not because it is
 * hard, but because it lands in the gap between "reading about a tool" and
 * "having one running", and that gap is where evaluations go to die.
 *
 * So rather than asking someone to follow instructions, this gives them a
 * paragraph to paste into whatever agent they already work with. The agent has
 * the repositories, the variable names, the verification command, and — the part
 * that matters — the instruction to stop and report rather than to improvise if a
 * step fails. A setup that half-worked and said nothing is worse than one that
 * refused.
 */

/** What the person filled in, if anything. Blanks stay as visible placeholders. */
export interface OwnValues {
  /** The machine UniVerse is listening on. */
  host: string;
  /** A login on that machine. */
  user: string;
  /** The UniVerse account to open. */
  account: string;
}

/** Shown in place of a value the person has not supplied. */
const PLACEHOLDER: OwnValues = {
  host: "uv.internal.example.com",
  user: "a login on that machine",
  account: "YOUR.ACCOUNT",
};

/**
 * Fill a blank with its placeholder, so a half-completed form still copies
 * something runnable-looking rather than an empty string that fails obscurely.
 *
 * @param supplied What the person typed.
 * @param fallback What to show if they typed nothing.
 */
function orPlaceholder(supplied: string, fallback: string): string {
  const trimmed = supplied.trim();
  return trimmed.length > 0 ? trimmed : fallback;
}

/** Where the two halves live. */
export const REPOSITORIES = {
  /** The hardened MCP server — the fork, and the part that touches your database. */
  server: "https://github.com/mikejsmith1985/u2-mcp",
  /** The counter application — the API and the screens. */
  app: "https://github.com/mikejsmith1985/u2-counter",
  /** The hardening, as a reviewable difference against the upstream project. */
  hardening: "https://github.com/mikejsmith1985/u2-mcp/pull/1",
} as const;

/**
 * The environment the MCP server reads, with the person's own values in it.
 *
 * @param values What they typed into the form.
 */
export function serverEnvironment(values: OwnValues): string {
  const host = orPlaceholder(values.host, PLACEHOLDER.host);
  const user = orPlaceholder(values.user, PLACEHOLDER.user);
  const account = orPlaceholder(values.account, PLACEHOLDER.account);

  return [
    `$env:U2_HOST     = '${host}'`,
    `$env:U2_USER     = '${user}'`,
    `$env:U2_PASSWORD = 'the password for that login'`,
    `$env:U2_ACCOUNT  = '${account}'`,
  ].join("\n");
}

/** Clone both halves. */
export function cloneCommands(): string {
  return [
    `git clone ${REPOSITORIES.server}`,
    `git clone ${REPOSITORIES.app}`,
  ].join("\n");
}

/** The one command that proves the connection before anything else is attempted. */
export const VERIFY_COMMAND = ".venv\\Scripts\\python scripts\\try-it-here.py";

/**
 * The whole setup, written as an instruction for somebody else's coding agent.
 *
 * @param values What they typed into the form, folded into the text.
 */
export function setupPrompt(values: OwnValues): string {
  const host = orPlaceholder(values.host, PLACEHOLDER.host);
  const account = orPlaceholder(values.account, PLACEHOLDER.account);

  return `Set up a read-only MCP server against my Rocket UniVerse database, and the
trade-counter application that reads through it.

Two repositories:
  - ${REPOSITORIES.server} -- the MCP server (Python 3.12)
  - ${REPOSITORIES.app} -- the API and web application (.NET 9, React)

My database:
  host    ${host}
  account ${account}
  user and password: ask me, do not guess and do not write them into any file.

Please do this in order, and stop at the first step that does not behave as
described rather than working around it:

1. Clone the server repository and create a virtual environment from its
   pyproject. Install "uopy" from my own Rocket U2 client tools -- it is not on
   PyPI, so if you cannot find it, stop and tell me.

2. Set U2_HOST, U2_USER, U2_PASSWORD and U2_ACCOUNT in the environment only.
   Never in a file, never in a commit.

3. Run "${VERIFY_COMMAND}". It connects, attempts a write in order to prove the
   refusal, lists my files and reads one dictionary. It prints either a count of
   passed checks or the exact step that failed. Show me its output verbatim.

4. Only if that passed: clone the application repository, build it, and point its
   Erp:Endpoint at the MCP server. Leave Erp:Writable off.

5. Open the "Explore" screen and confirm it lists MY files with MY field names --
   they come from my dictionaries, not from anything written into the page. Tell
   me what it listed.

Two things to know before you start. Writes are refused by default in this fork:
U2_READ_ONLY defaults to true, and the tool list contains no write and no delete.
It does include a selection tool, which refuses any verb that is not SELECT or
SSELECT -- checked in the application and again at the server.

And the counter screens themselves -- as opposed to the Explore screen -- are
mapped to a demonstration schema, so they will not fit my files until that
mapping is changed; that mapping lives in one class, ErpFiles.cs.

Point this at a restored copy of my data the first time, not production.`;
}
