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
          Nobody has used this for a while, so it powered itself down. Reading the
          catalogue now — this takes a few seconds.
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

  return <UnreachableState failure={failure} onRetry={onRetry} />;
}
