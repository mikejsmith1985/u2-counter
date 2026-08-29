/**
 * How to point this at your own database and your own model.
 *
 * The question anybody serious asks within a minute of seeing this, and one the
 * demonstration cannot answer by itself: nobody evaluating software wants to
 * send their inventory through somebody else's API key, and nobody wants to
 * judge a system on data they have never seen.
 *
 * So this says exactly what to set and what happens then. It is deliberately
 * short — four environment variables and one command — because the length of
 * these instructions is itself part of the claim.
 */

import { useEffect, useRef } from "react";

interface Props {
  /** Close the panel. */
  onClose: () => void;
}

export function ConnectPanel({ onClose }: Props): React.JSX.Element {
  const panelRef = useRef<HTMLDivElement>(null);

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
            Point this at your own data
          </h2>
          <span className="header__spacer" />
          <button type="button" className="button button--quiet" onClick={onClose}>
            Close (Esc)
          </button>
        </div>

        <div className="connect">
          <p className="connect__lead">
            Two halves, and they are independent. You can run the server against your
            database without any model at all, and you should do that first.
          </p>

          <section className="connect__section">
            <h3>1 &middot; Your database</h3>
            <p>
              Clone the fork, install <code>uopy</code> from your own U2 client tools,
              and set four things. Nothing else needs configuring.
            </p>
            <pre>
{`$env:U2_HOST     = 'uv.internal.example.com'
$env:U2_USER     = 'a login on that machine'
$env:U2_PASSWORD = '…'
$env:U2_ACCOUNT  = 'YOUR.ACCOUNT'

.venv\\Scripts\\python scripts\\try-it-here.py`}
            </pre>
            <p>
              That command connects, proves the write refusal by attempting one,
              lists your files and reads a dictionary. It prints either{" "}
              <em>12 checks passed</em> or the exact step that failed and what to do
              about it.
            </p>

            <div className="connect__note connect__note--good">
              <strong>Writes are refused by default.</strong> <code>U2_READ_ONLY</code>{" "}
              defaults to true in this fork — you opt in to writes, rather than
              remembering to opt out. The tool list has no write, delete or arbitrary
              query in it, so there is nothing for a model to reach for.
            </div>

            <div className="connect__note connect__note--warn">
              <strong>Point it at a restored copy the first time.</strong> Not because
              it writes — it refuses to, and the command above shows that. Because
              &ldquo;point a new tool at production&rdquo; deserves that answer
              regardless of who is asking.
            </div>
          </section>

          <section className="connect__section">
            <h3>2 &middot; Your model</h3>
            <p>
              The assistant reads one variable. Set your own key and the questions go
              to your account, not mine.
            </p>
            <pre>{`$env:ANTHROPIC_API_KEY = 'sk-ant-…'`}</pre>
            <p>
              Leave it unset and the assistant simply does not appear — everything else
              works unchanged. This deployment pins Claude Haiku and caps output,
              tool calls, questions per session and spend per day, because the key
              behind it is personal. Yours is yours to configure.
            </p>
          </section>

          <section className="connect__section">
            <h3>3 &middot; What fits your schema, and what does not</h3>
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
                      <strong>Runs as-is.</strong> Its files, fields and headings come
                      from your dictionaries. Nothing is written into the page.
                    </td>
                  </tr>
                  <tr>
                    <th scope="row">The counter screens</th>
                    <td>
                      Need a mapping. They are compiled to this demonstration&rsquo;s
                      layout; every file name and field position lives in one class
                      (<code>ErpFiles.cs</code>), so it is one file&rsquo;s work rather
                      than a hunt — but mapping a real ERP schema is a real job and no
                      amount of tidy code makes it trivial.
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </section>

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
