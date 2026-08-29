/**
 * The states that are not a successful answer.
 *
 * Each is visually distinct from the others and from an empty grid. The one
 * that matters most is Unreachable: if a failure to reach the data rendered the
 * same as "no stock", a representative would tell a customer there is none when
 * nobody managed to ask.
 */

import type { ApiFailure } from "../../../api/client";
import { useReadiness } from "../../../api/readiness";

interface FailureProps {
  failure: ApiFailure;
  onRetry: () => void;
}

/** The system could not reach the stock data. Never rendered as an empty result. */
export function UnreachableState({ failure, onRetry }: FailureProps): React.JSX.Element {
  return (
    <div className="state state--unreachable" role="alert">
      <p className="state__title">The system could not reach the stock data</p>
      <p className="state__detail">
        {failure.message} This is not the same as the part being out of stock — nothing
        was read, so nothing is known either way.
      </p>
      <button type="button" className="button" onClick={onRetry}>
        Try again
      </button>
    </div>
  );
}

/** The ERP declined the request. */
export function RefusedState({ failure, onRetry }: FailureProps): React.JSX.Element {
  return (
    <div className="state" role="alert">
      <p className="state__title">The system declined that request</p>
      <p className="state__detail">{failure.message}</p>
      <button type="button" className="button button--quiet" onClick={onRetry}>
        Try again
      </button>
    </div>
  );
}

/**
 * A record could not be read without guessing at what it means.
 *
 * The one failure where refusing is the feature. An INVENTORY record whose
 * quantity field is longer than its branch field describes stock at a branch it
 * does not name, and there is no safe way to decide which branch that was --
 * every option invents an answer a representative would then repeat.
 *
 * So the screen says what happened and who can fix it, and shows no figures at
 * all. Nothing here is retryable: the record will parse the same way next time.
 */
export function MalformedRecordState({
  failure,
  partNumber,
}: {
  failure: ApiFailure;
  partNumber: string;
}): React.JSX.Element {
  return (
    <div className="state" role="alert">
      <p className="state__title">The record for {partNumber} could not be read</p>
      <p className="state__detail">{failure.message}</p>
      <p className="state__detail">
        No figures are shown, because reading this record would mean guessing at
        what it says. Someone with access to the ERP will need to look at it.
      </p>
    </div>
  );
}

/**
 * The request itself was wrong.
 *
 * Its own state because it used to fall through to "the system could not be
 * reached", which sent a representative chasing an outage that was not
 * happening — and told them nothing about the thing they could actually fix.
 *
 * No retry button: repeating a malformed request produces the same answer.
 */
export function InvalidRequestState({ failure }: { failure: ApiFailure }): React.JSX.Element {
  return (
    <div className="state" role="alert">
      <p className="state__title">That request could not be understood</p>
      <p className="state__detail">{failure.message}</p>
      <p className="state__detail">
        The system is working. Try searching again from the box above.
      </p>
    </div>
  );
}

/** The part does not exist. */
export function NotFoundState({ partNumber }: { partNumber: string }): React.JSX.Element {
  return (
    <div className="state">
      <p className="state__title">No part numbered {partNumber}</p>
      <p className="state__detail">
        Check the number, or search by part of the description instead.
      </p>
    </div>
  );
}

/**
 * The part exists but has no inventory record.
 *
 * Distinct from zero stock, and the wording says so outright: telling a customer
 * there is none when nobody has counted is a different and worse answer.
 */
export function StockUnknownState(): React.JSX.Element {
  return (
    <div className="state">
      <p className="state__title">Stock is not recorded for this part</p>
      <p className="state__detail">
        This part is in the catalogue but has no inventory record, so its stock is
        unknown rather than zero. Nobody has counted it.
      </p>
    </div>
  );
}

/** Waiting for an answer. */
export function LoadingState(): React.JSX.Element {
  return (
    <div className="state" role="status" aria-live="polite">
      <p className="state__detail">Reading stock…</p>
    </div>
  );
}

/** Nothing chosen yet. */
export function EmptyState(): React.JSX.Element {
  const { isReady, catalogueCount } = useReadiness();

  if (!isReady) {
    // The waking state. This application scales to zero, so the first arrival
    // after a quiet period is looking at a container that is still starting.
    // Saying so is the difference between a system that is starting and one that
    // appears to be broken.
    return (
      <div className="state">
        <p className="state__title">Waking up</p>
        <p className="state__detail">
          Nobody has used this for a while, so it powered itself down rather than
          bill for sitting idle. Reading the catalogue now — a few seconds, and
          only on the first visit after a quiet period.
        </p>
      </div>
    );
  }

  return (
    <div className="state">
      <p className="state__title">Search for a part</p>
      <p className="state__detail">
        Type a part number, a description or a manufacturer. Press <kbd>/</kbd> to
        jump to the search box at any time.
      </p>
      {catalogueCount > 0 && (
        <p className="state__detail">
          <span className="figures">{catalogueCount.toLocaleString()}</span> parts
          searchable.
        </p>
      )}
    </div>
  );
}

/**
 * Chooses the right failure state for a failure kind.
 */
export function FailureState({
  failure,
  partNumber,
  onRetry,
}: {
  failure: ApiFailure;
  partNumber: string;
  onRetry: () => void;
}): React.JSX.Element {
  if (failure.kind === "not-found") {
    return <NotFoundState partNumber={partNumber} />;
  }

  if (failure.kind === "refused") {
    return <RefusedState failure={failure} onRetry={onRetry} />;
  }

  if (failure.kind === "invalid") {
    return <InvalidRequestState failure={failure} />;
  }

  if (failure.kind === "malformed-record") {
    return <MalformedRecordState failure={failure} partNumber={partNumber} />;
  }

  // Everything left is a failure to reach the data, which includes the network
  // being down. Unreachable is the honest default: it is the answer that makes a
  // representative ring the customer back rather than quote them a figure.
  return <UnreachableState failure={failure} onRetry={onRetry} />;
}
