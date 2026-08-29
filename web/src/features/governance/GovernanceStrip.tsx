/**
 * Who is signed in, what they may do, and which database account served them.
 *
 * Small, permanent, and the part an applications director will stop on. A
 * shared database login is surfaced to the person using it rather than buried in
 * a log, because it is a limitation they should know about while they work: the
 * database cannot tell them apart from anyone else using the same account.
 */

import type { SessionResponse } from "../../api/types";
import { ProvenanceBadges } from "../../components/ProvenanceBadges";

interface Props {
  session: SessionResponse | null;
  onShowActivity: () => void;
  /** Open the persona picker. Named for what it does, not for a login. */
  onChangeIdentity: () => void;
}

export function GovernanceStrip({
  session,
  onShowActivity,
  onChangeIdentity,
}: Props): React.JSX.Element {
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
        Signed in as{" "}
        {/*
          Labelled explicitly. The visible text is a person's name, which tells a
          screen-reader user who is signed in and nothing about the fact that
          activating it changes that -- and a control whose purpose has to be
          inferred from its surroundings has no purpose to someone reading it on
          its own.
        */}
        <button
          type="button"
          className="governance__identity"
          onClick={onChangeIdentity}
          aria-label={`Signed in as ${session.displayName}. Activate to sign in as someone else.`}
        >
          <strong>{session.displayName}</strong>
        </button>
      </span>

      <span>
        Branch <span className="governance__mono">{session.homeBranchCode}</span>
      </span>

      <span>
        Database login <span className="governance__mono">{session.databaseLogin}</span>
      </span>

      <ProvenanceBadges
        isReadOnly={session.isReadOnly}
        isSharedLogin={session.databaseLoginIsShared}
        isDemonstrationData={session.isDemonstrationData}
      />

      <span className="header__spacer" />

      <button type="button" className="button button--quiet" onClick={onShowActivity}>
        Recent activity
      </button>
    </footer>
  );
}
