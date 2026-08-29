/**
 * Who is signed in, what they may do, and which database account served them.
 *
 * Small, permanent, and the part an applications director will stop on. A
 * shared database login is surfaced to the person using it rather than buried in
 * a log, because it is a limitation they should know about while they work: the
 * database cannot tell them apart from anyone else using the same account.
 */

import type { SessionResponse } from "../../api/types";

interface Props {
  session: SessionResponse | null;
  onShowActivity: () => void;
}

export function GovernanceStrip({ session, onShowActivity }: Props): React.JSX.Element {
  if (!session) {
    return (
      <footer className="governance">
        <span>Not signed in</span>
      </footer>
    );
  }

  return (
    <footer className="governance" aria-label="Session and data status">
      <span>
        Signed in as <strong>{session.displayName}</strong>
      </span>

      <span>
        Branch <span className="governance__mono">{session.homeBranchCode}</span>
      </span>

      <span>
        Database login <span className="governance__mono">{session.databaseLogin}</span>
      </span>

      {session.databaseLoginIsShared && (
        <span
          className="governance__badge governance__badge--shared"
          title="Several people use this database account, so the database cannot tell them apart"
        >
          SHARED LOGIN
        </span>
      )}

      {session.isReadOnly && (
        <span className="governance__badge governance__badge--readonly">READ ONLY</span>
      )}

      {session.isDemonstrationData && (
        <span className="governance__badge governance__badge--demo">DEMONSTRATION DATA</span>
      )}

      <span className="header__spacer" />

      <button type="button" className="button button--quiet" onClick={onShowActivity}>
        Recent activity
      </button>
    </footer>
  );
}
