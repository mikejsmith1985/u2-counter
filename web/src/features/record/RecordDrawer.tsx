/**
 * The stored record, beside the structured form the screen is built from.
 *
 * This screen exists to be checked. Anyone can open it and confirm that branch
 * three on the grid is branch three in the record — that nothing was flattened,
 * reordered or invented on the way. The separators are rendered visibly and
 * labelled rather than hidden, because hiding them would defeat the point.
 */

import { ParallelFields } from "./ParallelFields";
import { ProveItYourself } from "./ProveItYourself";
import { useEffect, useRef } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, ApiFailure } from "../../api/client";
import type { MarkDescription } from "../../api/types";
import { useReturnFocus } from "../../components/useReturnFocus";
import { ProvenanceBadges } from "../../components/ProvenanceBadges";

interface Props {
  partNumber: string;
  onClose: () => void;
}

/** Which style each separator gets, by its code point. */
const MARK_MODIFIER: Record<number, string> = {
  254: "am",
  253: "vm",
  252: "sm",
};

/** Short label for each separator, for the inline badge. */
const MARK_LABEL: Record<number, string> = {
  254: "AM",
  253: "VM",
  252: "SM",
};

export function RecordDrawer({ partNumber, onClose }: Props): React.JSX.Element {
  useReturnFocus();

  const panelRef = useRef<HTMLDivElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);

  const { data, error, isPending } = useQuery({
    queryKey: ["record", partNumber],
    queryFn: ({ signal }) => api.record(partNumber, signal),
  });

  // The drawer covers the governance strip, and this is the screen most likely
  // to be photographed: a MultiValue record on screen, with no badge, is an
  // image someone can present in good faith as production data.
  const { data: session } = useQuery({
    queryKey: ["session"],
    queryFn: ({ signal }) => api.session(signal),
  });

  // Focus moves into the drawer when it opens and Escape closes it. Without
  // both, a keyboard user opens the drawer and is stranded behind it.
  useEffect(() => {
    closeRef.current?.focus();

    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === "Escape") {
        onClose();
        return;
      }

      if (event.key !== "Tab" || !panelRef.current) {
        return;
      }

      // Focus is trapped inside the panel: tabbing past the last control wraps
      // to the first rather than wandering into the page behind.
      const focusable = panelRef.current.querySelectorAll<HTMLElement>(
        'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
      );

      if (focusable.length === 0) return;

      const first = focusable[0];
      const last = focusable[focusable.length - 1];

      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    }

    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

  const failure = error instanceof ApiFailure ? error : null;

  return (
    <div
      className="drawer"
      onClick={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
    >
      <div
        className="drawer__panel"
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="record-title"
      >
        <div className="drawer__header">
          <h2 className="panel__title" id="record-title">
            The record as the system stores it
          </h2>
          <span className="drawer__badges">
            {session && (
              <ProvenanceBadges
                isReadOnly={session.isReadOnly}
                isSharedLogin={session.databaseLoginIsShared}
                isDemonstrationData={session.isDemonstrationData}
              />
            )}
          </span>

          <button type="button" className="button button--quiet" onClick={onClose} ref={closeRef}>
            Close (Esc)
          </button>
        </div>

        {isPending && <p className="state__detail">Reading the record…</p>}

        {failure && (
          <p className="state__detail" role="alert">
            {failure.kind === "not-found"
              ? "There is no inventory record for this part, so there is nothing to show."
              : failure.message}
          </p>
        )}

        {data && (
          <>
            <p className="state__detail">
              {data.fileName} · {data.recordId} — every branch's position for this part
              lives in one record. Position three in each field belongs to the same
              branch.
            </p>

            <MarkLegend marks={data.marks} />

            <div className="record-panes">
              <div>
                <h3 className="panel__title">Stored</h3>
                <div className="record-pane">
                  <MarkedRecord raw={data.rawRecord} marks={data.marks} />
                </div>
              </div>

              <div>
                <h3 className="panel__title">Parsed</h3>
                <div className="record-pane">
                  <ParallelFields parsed={data.parsed} />
                </div>
              </div>
            </div>

            <h3 className="panel__title" style={{ marginTop: "0.8rem" }}>
              What was run
            </h3>
            <div className="record-query">{data.query}</div>

            {/* The objection this whole panel invites, answered rather than
                left for the reader to raise on their own: nothing drawn here
                can settle whether the data is genuine. */}
            <ProveItYourself raw={data.rawRecord} />
          </>
        )}
      </div>
    </div>
  );
}

/** Names each separator present, so a reader unfamiliar with them can follow. */
export function MarkLegend({ marks }: { marks: MarkDescription[] }): React.JSX.Element {
  return (
    <div className="record-legend">
      {marks.map((mark) => (
        <span key={mark.code}>
          <span className={`mark mark--${MARK_MODIFIER[mark.code] ?? "am"}`}>
            {MARK_LABEL[mark.code] ?? "?"}
          </span>{" "}
          {mark.name} — separates {mark.separates.toLowerCase()}
        </span>
      ))}
    </div>
  );
}

/**
 * The stored record with its separators shown as labelled badges.
 *
 * The characters themselves are invisible on a screen, so rendering them raw
 * would show one unbroken string and teach nobody anything. Replacing them with
 * a badge shows the structure without pretending the record contains something
 * it does not.
 */
export function MarkedRecord({
  raw,
  marks,
}: {
  raw: string;
  marks: MarkDescription[];
}): React.JSX.Element {
  const codes = new Set(marks.map((mark) => mark.code));
  const pieces: React.JSX.Element[] = [];
  let buffer = "";

  const flush = (key: string): void => {
    if (buffer.length > 0) {
      pieces.push(<span key={`text-${key}`}>{buffer}</span>);
      buffer = "";
    }
  };

  for (let index = 0; index < raw.length; index += 1) {
    const code = raw.charCodeAt(index);

    if (codes.has(code)) {
      flush(String(index));
      pieces.push(
        <span
          key={`mark-${index}`}
          className={`mark mark--${MARK_MODIFIER[code] ?? "am"}`}
          title={`Character ${code}`}
        >
          {MARK_LABEL[code] ?? code}
        </span>,
      );
    } else {
      buffer += raw[index];
    }
  }

  flush("end");

  return <>{pieces}</>;
}
