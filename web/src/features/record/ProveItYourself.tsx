/**
 * What this screen cannot prove, and the two things that can.
 *
 * The fair objection, asked in almost these words: how would anybody know this
 * is a real record rather than something the application was written to draw?
 * They would not. Every pixel here is chosen by this code, so a screen showing
 * separators is evidence of a screen showing separators and nothing else. Any
 * amount of better rendering answers a question nobody asked.
 *
 * Two things do move it, and neither is a picture.
 *
 * The first is the bytes themselves, handed over rather than described — copied
 * out, opened in whatever the reader trusts, and counted there. A fabricated
 * record would have to survive being parsed by somebody else's tools with its
 * parallel fields intact, which is a far harder thing to fake than a table.
 *
 * The second is this parser meeting a record it has never seen. Paste one in and
 * the same code renders it. A renderer that only knows how to draw its own data
 * fails immediately on somebody else's; one that handles a record from a real
 * system was not written to flatter a fixture.
 *
 * Neither is proof. The only proof is running it against a database the reader
 * controls, which is one command, and this says so rather than implying the
 * screen has settled it.
 */

import { useState } from "react";
import { MarkedRecord } from "../ask/MarkedRecord";
import { CopyButton } from "../connect/CopyButton";

/** Attribute mark: separates fields. */
const AM = "þ";

/** Value mark: separates values within a field. */
const VM = "ý";

/** A record from a different system entirely, offered so the box is not empty. */
const FOREIGN_EXAMPLE =
  ["ACME-1001", "WIDGET, BRASS, 3/4IN", "EA", "12.50"].join(AM) +
  AM +
  ["NORTH", "SOUTH", "EAST"].join(VM) +
  AM +
  ["4", "0", "17"].join(VM);

interface Props {
  /** The record currently on screen, so it can be handed over as bytes. */
  raw: string;
}

export function ProveItYourself({ raw }: Props): React.JSX.Element {
  const [pasted, setPasted] = useState("");
  const [isOpen, setIsOpen] = useState(false);

  const subject = pasted.trim().length > 0 ? pasted : "";

  return (
    <section className="prove">
      <details
        open={isOpen}
        onToggle={(event) => setIsOpen((event.currentTarget as HTMLDetailsElement).open)}
      >
        <summary className="prove__summary">
          How would you know this is real?
        </summary>

        <p className="prove__lead">
          You would not, from this screen. Every pixel on it is drawn by this
          application, so a picture of separators is a picture. Two things are
          worth more than the picture, and neither of them is us telling you.
        </p>

        <h4 className="prove__heading">Take the bytes and check them elsewhere</h4>
        <p>
          This is the record exactly as it came back, separators included. Paste it
          into anything you trust and count for yourself: the field separators are
          byte <code>254</code>, the value separators byte <code>253</code>. Every
          multi-valued field in a well-formed record has the same number of values,
          because position three of each one describes the same branch.
        </p>

        <div className="prove__bytes">
          <pre>{raw}</pre>
          <CopyButton text={raw} label="Copy the raw record" />
        </div>

        <h4 className="prove__heading">Give it a record it has never seen</h4>
        <p>
          A renderer written to flatter its own fixture falls over on somebody
          else&rsquo;s data. Paste any MultiValue record — from your own system, or
          the example below, which describes nothing in this database — and the same
          code renders it.
        </p>

        <div className="prove__try">
          <textarea
            className="prove__input"
            value={pasted}
            spellCheck={false}
            aria-label="Paste a MultiValue record to render"
            placeholder="Paste a record with attribute marks (byte 254) between its fields."
            onChange={(event) => setPasted(event.target.value)}
          />
          <div className="prove__actions">
            <button
              type="button"
              className="button button--quiet"
              onClick={() => setPasted(FOREIGN_EXAMPLE)}
            >
              Use an example from another system
            </button>
            {pasted.length > 0 && (
              <button
                type="button"
                className="button button--quiet"
                onClick={() => setPasted("")}
              >
                Clear
              </button>
            )}
          </div>
        </div>

        {subject.length > 0 && (
          <div className="prove__rendered">
            <MarkedRecord raw={subject} />
          </div>
        )}

        <div className="prove__note">
          <strong>Neither of these is proof.</strong> The only thing that settles it
          is pointing the server at a database you control, which is four
          environment variables and one command — it connects, proves the write
          refusal by attempting one, and reads your dictionaries. It is under
          &ldquo;Use your own data&rdquo; in the header, and nothing about it needs
          anything from us.
        </div>
      </details>
    </section>
  );
}
