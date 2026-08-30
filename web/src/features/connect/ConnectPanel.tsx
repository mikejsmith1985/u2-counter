/**
 * The two ways to use this, and how to take the second one.
 *
 * These are genuinely different things and the old version of this panel blurred
 * them. Looking at a demonstration costs nothing and proves comparatively little.
 * Running it against your own database proves the claim, and it is also the only
 * version where nobody has to send their inventory through somebody else's API
 * key or trust somebody else's hosting.
 *
 * So the second path is the one this page is built around: both repositories are
 * public, every command has a copy button, the commands rewrite themselves with
 * whatever values you type, and if you would rather not do it by hand there is a
 * paragraph to hand to your own coding agent. The length of these instructions is
 * itself part of the claim, so the aim is that nobody has to type anything twice.
 */

import { useEffect, useRef, useState } from "react";
import { CopyButton } from "./CopyButton";
import {
  cloneCommands,
  REPOSITORIES,
  serverEnvironment,
  setupPrompt,
  VERIFY_COMMAND,
  type OwnValues,
} from "./setupPrompt";

interface Props {
  /** Close the panel. */
  onClose: () => void;
}

/** Nothing filled in yet; every command shows a visible placeholder instead. */
const NOTHING_SUPPLIED: OwnValues = { host: "", user: "", account: "" };

export function ConnectPanel({ onClose }: Props): React.JSX.Element {
  const panelRef = useRef<HTMLDivElement>(null);
  const [values, setValues] = useState<OwnValues>(NOTHING_SUPPLIED);

  useEffect(() => {
    panelRef.current?.focus();

    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === "Escape") {
        onClose();
      }
    }

    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

  return (
    <div
      className="drawer"
      onClick={(event) => {
        if (event.target === event.currentTarget) {
          onClose();
        }
      }}
    >
      <div
        ref={panelRef}
        className="drawer__panel"
        role="dialog"
        aria-modal="true"
        aria-labelledby="connect-title"
        tabIndex={-1}
      >
        <div className="drawer__header">
          <h2 id="connect-title" className="connect__title">
            Use your own data
          </h2>
          <span className="header__spacer" />
          <button type="button" className="button button--quiet" onClick={onClose}>
            Close (Esc)
          </button>
        </div>

        <div className="connect">
          <p className="connect__lead">
            There are two ways to look at this, and they answer different questions.
          </p>

          <PathSummary />
          <RunItYourself values={values} onChange={setValues} />
          <WhatFits />

          <p className="connect__close">
            Everything above is a read. If any of it does not behave as described,
            that is worth telling me about — it would be a defect, and finding those
            is most of what this project has been.
          </p>
        </div>
      </div>
    </div>
  );
}

/**
 * The choice, stated plainly before either path is explained.
 *
 * Separated out because somebody who only wanted the hosted demonstration should
 * be able to stop reading here, and somebody who wants their own instance should
 * know immediately that it exists rather than finding it at the bottom.
 */
function PathSummary(): React.JSX.Element {
  return (
    <section className="connect__section">
      <div className="scroll">
        <table className="connect__table">
          <thead>
            <tr>
              <th scope="col">Path</th>
              <th scope="col">What runs</th>
              <th scope="col">What it proves</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <th scope="row">Look at it here</th>
              <td>
                My deployment, demonstration data, my API key — capped to Claude
                Haiku with limits on tool calls, questions and spend per day.
              </td>
              <td>
                That the thing works, and what it looks like. Nothing about your
                data, because it has never seen any.
              </td>
            </tr>
            <tr>
              <th scope="row">
                <strong>Run it yourself</strong>
              </th>
              <td>
                Your machine, your database, your API key. Both repositories are
                public; nothing phones home and nothing reaches me.
              </td>
              <td>
                Whether it reads <em>your</em> files with <em>your</em> field
                names. That is the only version of the claim worth anything.
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  );
}

/**
 * The second path, in the order somebody would actually do it.
 *
 * @param values What has been typed into the form so far.
 * @param onChange Called with the updated values as they type.
 */
function RunItYourself({
  values,
  onChange,
}: {
  values: OwnValues;
  onChange: (next: OwnValues) => void;
}): React.JSX.Element {
  const environment = serverEnvironment(values);
  const prompt = setupPrompt(values);

  return (
    <section className="connect__section">
      <h3>Run it against your own instance</h3>

      <p>
        Fill these in and every command below rewrites itself with your values.
        Nothing you type here is sent anywhere — it never leaves this page, and
        there is deliberately no password box.
      </p>

      <ValuesForm values={values} onChange={onChange} />

      <h4>1 &middot; Clone both halves</h4>
      <CodeBlock text={cloneCommands()} />
      <p className="connect__aside">
        The server is a fork of{" "}
        <a href="https://github.com/bpamiri/u2-mcp" target="_blank" rel="noreferrer">
          bpamiri/u2-mcp
        </a>
        . What was changed, and why, is{" "}
        <a href={REPOSITORIES.hardening} target="_blank" rel="noreferrer">
          one reviewable difference
        </a>{" "}
        — ten defects, each with a test that fails against the original.
      </p>

      <h4>2 &middot; Tell the server where your database is</h4>
      <CodeBlock text={environment} />
      <p className="connect__aside">
        Install <code>uopy</code> from your own Rocket U2 client tools; it is not on
        PyPI. Set these in the environment only — never in a file.
      </p>

      <h4>3 &middot; Prove the connection before anything else</h4>
      <CodeBlock text={VERIFY_COMMAND} />
      <p>
        That connects, <strong>attempts a write in order to prove the refusal</strong>,
        lists your files and reads a dictionary. It prints either a count of passed
        checks or the exact step that failed and what to do about it.
      </p>

      <div className="connect__note connect__note--good">
        <strong>Writes are refused by default.</strong> <code>U2_READ_ONLY</code>{" "}
        defaults to true in this fork — you opt in to writes rather than remembering
        to opt out. The tool list has no write and no delete in it, so there is
        nothing for a model to reach for. It does include a selection tool, which
        refuses any verb that is not <code>SELECT</code> or <code>SSELECT</code> —
        checked in the application and again at the server.
      </div>

      <div className="connect__note connect__note--warn">
        <strong>Point it at a restored copy the first time.</strong> Not because it
        writes — it refuses to, and step 3 shows that. Because &ldquo;point a new
        tool at production&rdquo; deserves that answer regardless of who is asking.
      </div>

      <h4>4 &middot; Your own model, or none at all</h4>
      <CodeBlock text={`$env:ANTHROPIC_API_KEY = 'sk-ant-…'`} />
      <p>
        Leave it unset and the assistant simply does not appear; everything else
        works unchanged. Set it and the questions go to your account, not mine.
      </p>

      <h4>Or hand the whole thing to your own agent</h4>
      <p>
        The setup is short but it is still the point where most evaluations stop.
        This is the entire thing written as an instruction — paste it into Claude
        Code, Cursor, or whatever you already use.
      </p>
      <div className="connect__prompt">
        <CopyButton text={prompt} label="Copy the setup prompt" />
        <details>
          <summary>Read it first</summary>
          <pre>{prompt}</pre>
        </details>
      </div>
    </section>
  );
}

/**
 * Three boxes that rewrite the commands above.
 *
 * @param values Current contents.
 * @param onChange Called with the updated values.
 */
function ValuesForm({
  values,
  onChange,
}: {
  values: OwnValues;
  onChange: (next: OwnValues) => void;
}): React.JSX.Element {
  const fields: { key: keyof OwnValues; label: string; hint: string }[] = [
    { key: "host", label: "Host", hint: "uv.internal.example.com" },
    { key: "user", label: "User", hint: "a login on that machine" },
    { key: "account", label: "Account", hint: "YOUR.ACCOUNT" },
  ];

  return (
    <div className="connect__form">
      {fields.map((field) => (
        <label key={field.key} className="connect__field">
          <span className="connect__field-label">{field.label}</span>
          <input
            type="text"
            className="connect__input"
            value={values[field.key]}
            placeholder={field.hint}
            spellCheck={false}
            autoComplete="off"
            onChange={(event) =>
              onChange({ ...values, [field.key]: event.target.value })
            }
          />
        </label>
      ))}
      <p className="connect__field-note">
        No password box on purpose. It belongs in your environment, not in a form.
      </p>
    </div>
  );
}

/**
 * A command with a copy button on it.
 *
 * @param text The command.
 */
function CodeBlock({ text }: { text: string }): React.JSX.Element {
  return (
    <div className="connect__code">
      <pre>{text}</pre>
      <CopyButton text={text} />
    </div>
  );
}

/**
 * Which parts fit an unfamiliar schema and which do not.
 *
 * Kept because the honest answer is "two of the three", and saying so is worth
 * more than a page that implies everything transplants cleanly. Anybody who has
 * mapped an ERP schema knows it does not.
 */
function WhatFits(): React.JSX.Element {
  return (
    <section className="connect__section">
      <h3>What fits your schema, and what does not</h3>
      <div className="scroll">
        <table className="connect__table">
          <thead>
            <tr>
              <th scope="col">Part</th>
              <th scope="col">Against your account</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <th scope="row">The MCP server</th>
              <td>
                <strong>Runs as-is.</strong> File and record names are parameters,
                and the dictionary tools read your schema rather than assuming one.
              </td>
            </tr>
            <tr>
              <th scope="row">The explore screen</th>
              <td>
                <strong>Runs as-is.</strong> Its files, fields and headings come from
                your dictionaries. Nothing is written into the page.
              </td>
            </tr>
            <tr>
              <th scope="row">The counter screens</th>
              <td>
                Need a mapping. They are compiled to this demonstration&rsquo;s
                layout; every file name and field position lives in one class (
                <code>ErpFiles.cs</code>), so it is one file&rsquo;s work rather than
                a hunt — but mapping a real ERP schema is a real job and no amount of
                tidy code makes it trivial.
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  );
}
