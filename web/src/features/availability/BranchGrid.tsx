/**
 * Every branch's position for one part, readable at a glance.
 *
 * Colour carries the state, and so does the figure, and so does the text. A
 * branch that is out of stock must be distinguishable without relying on colour
 * alone — the eye finds the colour first, but nothing depends on seeing it.
 */

import type { BranchAvailability } from "../../api/types";

interface Props {
  branches: BranchAvailability[];
  /** Which branch the representative belongs to, shown first. */
  homeBranchCode: string;
  /** Opens the commitments for a branch. */
  onExpand: (branchCode: string) => void;
}

/** Class and wording for each stock state. */
const STATE_STYLE = {
  Available: { modifier: "available", wording: "free to sell" },
  AllCommitted: { modifier: "committed", wording: "all committed" },
  None: { modifier: "none", wording: "none in stock" },
} as const;

export function BranchGrid({ branches, homeBranchCode, onExpand }: Props): React.JSX.Element {
  if (branches.length === 0) {
    return (
      <p className="state__detail">No branch holds a position for this part.</p>
    );
  }

  // The representative's own branch leads: it is the answer they need first, and
  // scanning for it costs seconds on a call.
  const ordered = [...branches].sort((left, right) => {
    if (left.branchCode === homeBranchCode) return -1;
    if (right.branchCode === homeBranchCode) return 1;
    return right.freeToSell - left.freeToSell;
  });

  return (
    <ul className="branch-grid" aria-label="Availability by branch">
      {ordered.map((branch) => {
        const style = STATE_STYLE[branch.stockState];

        return (
          <li key={branch.branchCode}>
            <button
              type="button"
              className={`branch branch--${style.modifier}`}
              onClick={() => onExpand(branch.branchCode)}
              aria-label={
                `${branch.branchName}: ${branch.freeToSell} ${style.wording}, ` +
                `${branch.onHand} on hand, ${branch.committed} committed`
              }
            >
              <span className="branch__name">
                {branch.branchName}
                <span className="branch__code">{branch.branchCode}</span>
              </span>

              <span className="branch__free figures" aria-hidden="true">
                {branch.freeToSell}
              </span>

              {/* Wording as well as colour, so the state survives a monochrome
                  screen and a colour-blind reader. */}
              <span className="branch__detail">
                <span>{style.wording}</span>
              </span>

              <span className="branch__detail figures">
                <span>on hand {branch.onHand}</span>
                <span>committed {branch.committed}</span>
                {branch.onOrder > 0 && <span>on order {branch.onOrder}</span>}
                {branch.bin && <span>bin {branch.bin}</span>}
              </span>

              {!branch.isBranchKnown && (
                <span className="branch__unknown">
                  This branch code is not in the branch list
                </span>
              )}
            </button>
          </li>
        );
      })}
    </ul>
  );
}
