/**
 * Choosing which counter representative to be.
 *
 * This is not authentication and does not pretend to be. There is no password
 * here, and there could not be one that meant anything: the personas exist so a
 * reviewer can see the audit trail attribute work to a named person, and so the
 * governance strip has a name to show. Saying that plainly, on the screen, is the
 * point — an interface that looked like a login would be claiming a control this
 * demonstration does not have.
 *
 * It opens over the screen rather than in front of it. Someone arriving to look
 * at stock should not have to answer a question about identity before they can
 * see anything, so the first persona is already in use and this is how you
 * change it.
 */

import { useEffect, useRef } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api/client";
import type { Persona } from "../../api/types";
import { ReadOnlyNotice } from "../../components/ReadOnlyGate";
import { useReturnFocus } from "../../components/useReturnFocus";

interface Props {
  /** Who is signed in now, so their row can be marked. */
  currentSubject: string | null;
  /** Called when the panel should close, whether or not anything changed. */
  onClose: () => void;
}

export function SignIn({ currentSubject, onClose }: Props): React.JSX.Element {
  useReturnFocus();

  const panel = useRef<HTMLDivElement>(null);
  const queries = useQueryClient();

  const { data: personas } = useQuery({
    queryKey: ["personas"],
    queryFn: ({ signal }) => api.personas(signal),
  });

  const signIn = useMutation({
    mutationFn: (subject: string) => api.signIn(subject),
    onSuccess: async () => {
      // The session and the activity list both belong to whoever is signed in,
      // so both are stale the moment that changes. Leaving them would show the
      // previous person's name beside the new person's actions.
      await queries.invalidateQueries({ queryKey: ["session"] });
      await queries.invalidateQueries({ queryKey: ["activity"] });
      onClose();
    },
  });

  // Focus moves into the panel on open and Escape closes it, because this is a
  // dialog and a keyboard user is otherwise left behind on the page underneath.
  useEffect(() => {
    panel.current?.querySelector<HTMLElement>("button")?.focus();

    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === "Escape") {
        event.preventDefault();
        onClose();
      }
    }

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

  return (
    <div
      className="drawer"
      onClick={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
    >
      <div
        className="drawer__panel drawer--narrow"
        ref={panel}
        role="dialog"
        aria-modal="true"
        aria-labelledby="sign-in-title"
      >
        <div className="drawer__header">
          <h2 className="panel__title" id="sign-in-title">
            Who is at the counter
          </h2>
          <button type="button" className="button button--quiet" onClick={onClose}>
            Close
          </button>
        </div>

        <p className="drawer__note">
          These are demonstration identities, not accounts. There is no password,
          and choosing one proves nothing about who you are — it exists so the
          activity record has a person to name and the branch grid has a home
          branch to lead with.
        </p>

        <p className="drawer__note">
          Every persona reaches the database through the same shared login, which
          is why the record of who asked has to be kept here rather than left to
          the database.
        </p>

        <ul className="persona-list">
          {(personas ?? []).map((persona: Persona) => {
            const isCurrent = persona.subject === currentSubject;

            return (
              <li key={persona.subject}>
                <button
                  type="button"
                  className={isCurrent ? "persona persona--current" : "persona"}
                  aria-current={isCurrent ? "true" : undefined}
                  disabled={signIn.isPending}
                  onClick={() => signIn.mutate(persona.subject)}
                >
                  <span className="persona__name">{persona.displayName}</span>
                  <span className="persona__detail">
                    Branch {persona.homeBranchCode} · {persona.description}
                  </span>
                  {isCurrent && <span className="persona__marker">Signed in</span>}
                </button>
              </li>
            );
          })}
        </ul>

        {signIn.isError && (
          <p className="warning" role="alert">
            That identity could not be selected. Nothing has changed.
          </p>
        )}

        {/*
          The standing statement of what this application does to the ERP, put
          here because this is the panel someone opens when they are asking what
          it is allowed to do.
        */}
        <ReadOnlyNotice />
      </div>
    </div>
  );
}
