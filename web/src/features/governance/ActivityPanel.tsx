/**
 * What has been asked of the system, and by whom.
 *
 * The point of this panel is that it names a person for every request. An audit
 * file that records only that something happened is a debugging aid; one that
 * records who did it is the thing a review actually asks for.
 */

import { useEffect, useRef } from "react";
import { useQuery } from "@tanstack/react-query";
import { api } from "../../api/client";
import { useReturnFocus } from "../../components/useReturnFocus";

const RECENT_ENTRIES = 20;

interface Props {
  onClose: () => void;
}

export function ActivityPanel({ onClose }: Props): React.JSX.Element {
  useReturnFocus();

  const closeRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    closeRef.current?.focus();

    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === "Escape") onClose();
    }

    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

  const { data, isPending } = useQuery({
    queryKey: ["activity"],
    queryFn: ({ signal }) => api.activity(RECENT_ENTRIES, signal),
  });

  return (
    <div
      className="drawer"
      onClick={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
    >
      <div className="drawer__panel" role="dialog" aria-modal="true" aria-labelledby="activity-title">
        <div className="drawer__header">
          <h2 className="panel__title" id="activity-title">
            Recent activity
          </h2>
          <button type="button" className="button button--quiet" onClick={onClose} ref={closeRef}>
            Close (Esc)
          </button>
        </div>

        {isPending && <p className="state__detail">Reading the record…</p>}

        {data && data.entries.length === 0 && (
          <p className="state__detail">Nothing has been recorded yet in this session.</p>
        )}

        {data && data.entries.length > 0 && (
          <table className="commitments">
            <caption className="visually-hidden">Requests made in this session</caption>
            <thead>
              <tr>
                <th scope="col">When</th>
                <th scope="col">Who</th>
                <th scope="col">What</th>
                <th scope="col">Database login</th>
                <th scope="col">Outcome</th>
                <th scope="col" style={{ textAlign: "right" }}>
                  Took
                </th>
              </tr>
            </thead>
            <tbody>
              {data.entries.map((entry, index) => (
                <tr key={`${entry.occurredAt}-${index}`}>
                  <td className="figures">
                    {new Date(entry.occurredAt).toLocaleTimeString()}
                  </td>
                  <td>{entry.displayName}</td>
                  <td>
                    {entry.action}
                    {entry.targetKey && (
                      <span className="governance__mono"> {entry.targetKey}</span>
                    )}
                  </td>
                  <td className="governance__mono">
                    {entry.databaseLogin}
                    {entry.databaseLoginIsShared && " (shared)"}
                  </td>
                  <td>{entry.outcome}</td>
                  <td className="figures" style={{ textAlign: "right" }}>
                    {entry.durationMs} ms
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
